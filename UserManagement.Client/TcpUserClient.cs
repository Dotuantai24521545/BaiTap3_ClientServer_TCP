using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UserManagement.Shared;

namespace QuanLiNguoiDung
{
    public sealed class TcpUserClient : IDisposable
    {
        private readonly object gate = new object();
        private readonly SemaphoreSlim writes = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<Response>> pending = new ConcurrentDictionary<string, TaskCompletionSource<Response>>();
        private TcpClient client;
        public string Token { get; private set; }
        public bool Connected { get { lock (gate) return client != null; } }
        public event Action Disconnected;
        public async Task ConnectAsync(string host, int port)
        {
            Disconnect();
            var socket = new TcpClient { NoDelay = true };
            // Capture before publishing the TcpClient: Dispose may clear TcpClient.Client while connect is pending.
            Socket transport = socket.Client;
            lock (gate) client = socket;
            try
            {
                // TcpClient.EndConnect in .NET Framework dereferences its mutable Client property.
                // Bind EndConnect to the captured Socket instead so concurrent Close yields a handled disposal error.
                Task connecting = Task.Factory.FromAsync<string, int>(transport.BeginConnect, transport.EndConnect, host, port, null);
                var observeFailure = connecting.ContinueWith(faulted => { var observed = faulted.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                if (await Task.WhenAny(connecting, Task.Delay(5000)).ConfigureAwait(false) != connecting) throw new IOException("Connection timed out.");
                await connecting.ConfigureAwait(false);
                lock (gate) if (client != socket) throw new IOException("Connection was closed.");
                var receive = ReceiveAsync(socket);
                var heartbeat = HeartbeatAsync(socket);
            }
            catch (SocketException) { Abort(socket); throw; }
            catch (IOException) { Abort(socket); throw; }
            catch (ObjectDisposedException) { Abort(socket); throw new IOException("Connection was closed."); }
        }
        public async Task<Response> SendAsync(Request request)
        {
            TcpClient socket; lock (gate) socket = client;
            if (socket == null) throw new IOException("Not connected.");
            if (request.Command == Commands.Profile || request.Command == Commands.Logout) request.Token = Token;
            var completion = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(request.RequestId, completion)) throw new InvalidOperationException("Duplicate request identifier.");
            try
            {
                using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                using (deadline.Token.Register(() => Abort(socket)))
                {
                    await writes.WaitAsync(deadline.Token).ConfigureAwait(false);
                    try { await FrameCodec.WriteAsync(StreamFor(socket), request, deadline.Token).ConfigureAwait(false); }
                    finally { writes.Release(); }
                    Response response = await completion.Task.ConfigureAwait(false);
                    if (request.Command == Commands.Login && response.Code == Codes.Ok) {
                        lock (gate) {
                            if (client != socket) throw new IOException("Connection was closed.");
                            Token = response.Token;
                        }
                    }
                    return response;
                }
            }
            catch (OperationCanceledException) { Abort(socket); throw new IOException("Request timed out."); }
            catch (ObjectDisposedException) { Abort(socket); throw new IOException("Connection was closed."); }
            catch (SocketException) { Abort(socket); throw; }
            catch (IOException) { Abort(socket); throw; }
            finally { TaskCompletionSource<Response> removed; pending.TryRemove(request.RequestId, out removed); }
        }
        private NetworkStream StreamFor(TcpClient socket)
        {
            // Serialize TcpClient.GetStream with Close; TcpClient also clears Client during disposal.
            lock (gate) {
                if (client != socket) throw new IOException("Connection was closed.");
                try { return socket.GetStream(); }
                catch (InvalidOperationException error) { throw new IOException("Connection was closed.", error); }
            }
        }
        private async Task ReceiveAsync(TcpClient socket)
        {
            try
            {
                while (true)
                {
                    byte[] bytes = await FrameCodec.ReadAsync(StreamFor(socket), CancellationToken.None).ConfigureAwait(false);
                    if (bytes == null) break;
                    var response = FrameCodec.Deserialize<Response>(bytes);
                    TaskCompletionSource<Response> completion;
                    if (response == null || response.Version != 1 || response.RequestId == null || !pending.TryGetValue(response.RequestId, out completion))
                        throw new InvalidDataException("Unexpected response.");
                    completion.TrySetResult(response);
                }
            }
            catch (SocketException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (SerializationException) { }
            finally { Abort(socket); }
        }
        private async Task HeartbeatAsync(TcpClient socket)
        {
            try
            {
                while (true)
                {
                    await Task.Delay(10000).ConfigureAwait(false);
                    lock (gate) if (client != socket) return;
                    await SendAsync(new Request { Command = Commands.Echo, Echo = "heartbeat" }).ConfigureAwait(false);
                }
            }
            catch (SocketException) { Abort(socket); }
            catch (IOException) { Abort(socket); }
        }
        public void Disconnect() { TcpClient old; lock (gate) old = client; if (old != null) Abort(old); }
        private void Abort(TcpClient socket)
        {
            lock (gate)
            {
                socket.Close();
                if (client != socket) return;
                client = null; Token = null;
                foreach (var request in pending.Values) request.TrySetException(new IOException("Connection was closed."));
            }
            var handler = Disconnected; if (handler != null) handler();
        }
        public void Dispose() { Disconnect(); }
    }
}
