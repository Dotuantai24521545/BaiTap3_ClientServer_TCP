using System;
using System.Collections.Concurrent;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UserManagement.Shared;

namespace UserManagement.Server
{
    public sealed class TcpUserServer
    {
        private readonly RequestProcessor processor;
        private readonly ConcurrentDictionary<Guid, TcpClient> clients = new ConcurrentDictionary<Guid, TcpClient>();
        private readonly ConcurrentDictionary<Guid, Task> sessions = new ConcurrentDictionary<Guid, Task>();
        private readonly TimeSpan idleTimeout;
        public event Action<string> Log;
        public TaskCompletionSource<int> Ready { get; } = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ClientCount { get { return clients.Count; } }
        public TcpUserServer(RequestProcessor processor, TimeSpan? idleTimeout = null) { this.processor = processor; this.idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(45); }
        public async Task RunAsync(int port, CancellationToken token)
        {
            var listener = new TcpListener(IPAddress.Any, port);
            try
            {
                listener.Start(); Ready.TrySetResult(((IPEndPoint)listener.LocalEndpoint).Port); WriteLog("LISTEN port=" + ((IPEndPoint)listener.LocalEndpoint).Port);
                using (token.Register(() => listener.Stop()))
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        TcpClient client;
                        try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                        catch (SocketException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
                        catch (ObjectDisposedException) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
                        if (token.IsCancellationRequested) { client.Close(); token.ThrowIfCancellationRequested(); }
                        if (clients.Count >= 128) { client.Close(); WriteLog("CAPACITY rejected connection"); continue; }
                        client.NoDelay = true;
                        var state = new ClientSession(); clients.TryAdd(state.Id, client);
                        Task service = ServeAsync(state, client, token); // Không await trong vòng accept.
                        sessions[state.Id] = service;
                        var cleanup = service.ContinueWith(completed => { Task removed; sessions.TryRemove(state.Id, out removed); }, TaskScheduler.Default);
                    }
                }
            }
            catch (OperationCanceledException) { WriteLog("STOP requested"); }
            catch (SocketException) { Ready.TrySetException(new IOException("Không thể mở cổng TCP.")); WriteLog("LISTEN failed"); }
            finally
            {
                listener.Stop();
                foreach (var client in clients.Values) client.Close();
                await Task.WhenAll(sessions.Values.ToArray()).ConfigureAwait(false);
                WriteLog("STOP complete clients=" + ClientCount);
            }
        }
        private async Task ServeAsync(ClientSession state, TcpClient client, CancellationToken stop)
        {
            string id = state.Id.ToString("N").Substring(0, 8); WriteLog("CONNECT session=" + id);
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    while (!stop.IsCancellationRequested)
                    {
                        byte[] bytes;
                        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop))
                        {
                            deadline.CancelAfter(idleTimeout);
                            using (deadline.Token.Register(() => client.Close())) bytes = await FrameCodec.ReadAsync(stream, deadline.Token).ConfigureAwait(false);
                        }
                        if (bytes == null) { WriteLog("EOF session=" + id); break; }
                        Request request;
                        try { request = FrameCodec.Deserialize<Request>(bytes); }
                        catch (SerializationException) { await FrameCodec.WriteAsync(stream, Response.For(null, Codes.Invalid), stop); continue; }
                        Response response;
                        try { response = await processor.HandleAsync(state, request, stop).ConfigureAwait(false); }
                        catch (SqlException) { response = Response.For(request, Codes.Database); WriteLog("DATABASE unavailable session=" + id); }
                        catch (FormatException) { response = Response.For(request, Codes.Internal); WriteLog("DATABASE hash format invalid session=" + id); }
                        catch (InvalidOperationException) { response = Response.For(request, Codes.Internal); WriteLog("STATE error session=" + id); }
                        await FrameCodec.WriteAsync(stream, response, stop).ConfigureAwait(false);
                        // Không ghi request, mật khẩu, hash, salt hay token vào nhật ký.
                        WriteLog("RESPONSE session=" + id + " code=" + response.Code);
                        if (request != null && request.Command == Commands.Logout && response.Code == Codes.Ok) { WriteLog("LOGOUT session=" + id); break; }
                    }
                }
            }
            catch (OperationCanceledException) { WriteLog("CANCEL session=" + id); }
            catch (SocketException) { WriteLog("SOCKET closed session=" + id); }
            catch (IOException) { WriteLog("IO closed or invalid frame session=" + id); }
            catch (ObjectDisposedException) { WriteLog("CLOSED session=" + id); }
            finally { state.Clear(); client.Close(); TcpClient removed; clients.TryRemove(state.Id, out removed); WriteLog("REMOVE session=" + id + " clients=" + ClientCount); }
        }
        private void WriteLog(string text) { var handler = Log; if (handler != null) handler(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text); }
    }
}
