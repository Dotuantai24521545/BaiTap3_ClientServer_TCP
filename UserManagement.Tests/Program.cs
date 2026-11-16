using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuanLiNguoiDung;
using UserManagement.Server;
using UserManagement.Shared;

namespace UserManagement.Tests
{
    // Chỉ dùng trong kiểm thử; ứng dụng server thật không có chế độ thay SQL bằng bộ nhớ.
    public sealed class MemoryRepository : IUserRepository
    {
        public readonly ConcurrentDictionary<string, StoredUser> Users = new ConcurrentDictionary<string, StoredUser>(StringComparer.OrdinalIgnoreCase);
        private int nextId;
        public Task<StoredUser> FindAsync(string username, CancellationToken token)
        { StoredUser user; Users.TryGetValue(username, out user); return Task.FromResult(user); }
        public Task<bool> CreateAsync(Request request, string hash, string salt, CancellationToken token)
        {
            var user = new StoredUser { PasswordHash = hash, PasswordSalt = salt,
                Info = new UserInfo { UserId = Interlocked.Increment(ref nextId), Username = request.Username, FullName = request.FullName, Email = request.Email, Birthday = request.Birthday } };
            return Task.FromResult(Users.TryAdd(request.Username, user));
        }
        public Task UpdateLastLoginAsync(int id, CancellationToken token) { return Task.CompletedTask; }
    }
    internal sealed class ChunkedStream : MemoryStream
    {
        private readonly int chunk;
        public ChunkedStream(byte[] data, int chunk) : base(data) { this.chunk = chunk; }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { return base.ReadAsync(buffer, offset, Math.Min(count, chunk), token); }
    }
    internal static class Program
    {
        private static readonly List<string> results = new List<string>();
        private static void Check(bool condition, string name)
        {
            string line = (condition ? "PASS " : "FAIL ") + name; results.Add(line); Console.WriteLine(line);
            if (!condition) throw new InvalidOperationException(name);
        }
        private static Request Register(string username)
        { return new Request { Command = Commands.Register, Username = username, Password = "Test1234!", Email = username + "@example.com", FullName = "Người dùng thử " + username, Birthday = "2004-01-02" }; }
        private static async Task FramesAsync()
        {
            var first = new Request { Command = Commands.Echo, Echo = "Tiếng Việt\nDòng thứ hai & < >" };
            var second = new Request { Command = Commands.Echo, Echo = "Thông điệp thứ hai" };
            using (var written = new MemoryStream())
            {
                await FrameCodec.WriteAsync(written, first, CancellationToken.None);
                await FrameCodec.WriteAsync(written, second, CancellationToken.None);
                using (var split = new ChunkedStream(written.ToArray(), 1))
                {
                    var a = FrameCodec.Deserialize<Request>(await FrameCodec.ReadAsync(split, CancellationToken.None));
                    var b = FrameCodec.Deserialize<Request>(await FrameCodec.ReadAsync(split, CancellationToken.None));
                    Check(a.Echo == first.Echo, "F01 UTF-8 and embedded newline survive one-byte reads");
                    Check(b.RequestId == second.RequestId && b.Echo == second.Echo, "F02 coalesced frames remain separate");
                    Check(await FrameCodec.ReadAsync(split, CancellationToken.None) == null, "F03 clean EOF before next frame");
                }
                byte[] raw = written.ToArray();
                using (var truncated = new MemoryStream(raw.Take(5).ToArray()))
                {
                    bool rejected = false; try { await FrameCodec.ReadAsync(truncated, CancellationToken.None); } catch (EndOfStreamException) { rejected = true; }
                    Check(rejected, "F04 truncated payload rejected");
                }
            }
            foreach (byte[] header in new[] { new byte[] { 0,0,0,0 }, new byte[] { 0,1,0,1 }, new byte[] { 255,255,255,255 } })
            {
                bool rejected = false;
                using (var invalid = new MemoryStream(header)) try { await FrameCodec.ReadAsync(invalid, CancellationToken.None); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "F05 invalid zero/oversize frame length rejected");
            }
            bool malformed = false; try { FrameCodec.Deserialize<Request>(Encoding.UTF8.GetBytes("not json")); } catch (SerializationException) { malformed = true; }
            Check(malformed, "F06 malformed JSON rejected");
        }
        private static async Task AuthenticationAsync()
        {
            var repository = new MemoryRepository(); DateTime now = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
            var processor = new RequestProcessor(repository, tokenLifetime: TimeSpan.FromMinutes(30), clock: () => now);
            var state = new ClientSession();
            Check((await processor.HandleAsync(state, Register("alpha"), CancellationToken.None)).Code == Codes.Ok, "A01 registration accepted");
            Check((await processor.HandleAsync(state, Register("alpha"), CancellationToken.None)).Code == Codes.Exists, "A02 duplicate registration rejected");
            Check((await processor.HandleAsync(state, Register("bravo"), CancellationToken.None)).Code == Codes.Ok, "A03 second account accepted");
            Check(repository.Users["alpha"].PasswordSalt != repository.Users["bravo"].PasswordSalt && repository.Users["alpha"].PasswordHash != repository.Users["bravo"].PasswordHash,
                "A04 same password produces independent salts and hashes");
            var invalid = Register("charlie"); invalid.Email = "not-an-email";
            Check((await processor.HandleAsync(state, invalid, CancellationToken.None)).Code == Codes.Invalid, "A05 invalid email rejected server-side");
            invalid = Register("charlie"); invalid.Birthday = "2999-01-01";
            Check((await processor.HandleAsync(state, invalid, CancellationToken.None)).Code == Codes.Invalid, "A06 future birthday rejected");
            Check((await processor.HandleAsync(state, new Request { Command = Commands.Profile }, CancellationToken.None)).Code == Codes.Unauthorized, "A07 profile requires authentication");
            var badPassword = await processor.HandleAsync(state, new Request { Command = Commands.Login, Username = "alpha", Password = "wrong123!" }, CancellationToken.None);
            var unknownUser = await processor.HandleAsync(state, new Request { Command = Commands.Login, Username = "absent", Password = "wrong123!" }, CancellationToken.None);
            Check(badPassword.Code == Codes.AuthFailed && unknownUser.Code == badPassword.Code, "A08 wrong username/password share one error");
            var login = await processor.HandleAsync(state, new Request { Command = Commands.Login, Username = "alpha", Password = "Test1234!" }, CancellationToken.None);
            Check(login.Code == Codes.Ok && Convert.FromBase64String(login.Token).Length == 32, "A09 login issues a 256-bit random session token");
            string serialized = Encoding.UTF8.GetString(FrameCodec.Serialize(login));
            Check(!serialized.Contains("Password") && !serialized.Contains(repository.Users["alpha"].PasswordHash) && !serialized.Contains(repository.Users["alpha"].PasswordSalt), "A10 response excludes password hash and salt");
            var profileRequest = new Request { Command = Commands.Profile, Token = login.Token };
            Check((await processor.HandleAsync(state, profileRequest, CancellationToken.None)).User.FullName == repository.Users["alpha"].Info.FullName, "A11 own profile returned");
            Check((await processor.HandleAsync(new ClientSession(), profileRequest, CancellationToken.None)).Code == Codes.Unauthorized, "A12 token bound to its connection");
            var wrongToken = new Request { Command = Commands.Profile, Token = "invalid" };
            Check((await processor.HandleAsync(state, wrongToken, CancellationToken.None)).Code == Codes.Unauthorized, "A13 invalid token rejected");
            now = now.AddMinutes(31);
            Check((await processor.HandleAsync(state, profileRequest, CancellationToken.None)).Code == Codes.Expired && state.Token == null, "A14 expired session revoked");
            login = await processor.HandleAsync(state, new Request { Command = Commands.Login, Username = "alpha", Password = "Test1234!" }, CancellationToken.None);
            Check((await processor.HandleAsync(state, new Request { Command = Commands.Logout, Token = login.Token }, CancellationToken.None)).Code == Codes.Ok && state.Username == null,
                "A15 logout clears session");
            Check((await processor.HandleAsync(state, profileRequest, CancellationToken.None)).Code == Codes.Unauthorized, "A16 old token rejected after logout");
            Check((await processor.HandleAsync(state, new Request { Command = "UNKNOWN" }, CancellationToken.None)).Code == Codes.Unknown, "A17 unknown command has declared error code");
            Check((await processor.HandleAsync(state, new Request { Command = Commands.Echo, Version = 99 }, CancellationToken.None)).Code == Codes.Invalid, "A18 wrong protocol version rejected");
            Check(Validation.Email("tai@example.com") && !Validation.Email("tai@example.com\r\nother"), "V01 email validation handles control characters");
            Check(!Validation.Username("' OR 1=1--") && !Validation.Username("ab"), "V02 username whitelist and length validation");
            Check(!Validation.Password("short") && Validation.Password("Test1234!"), "V03 password length validation");
        }
        private static async Task NetworkAsync()
        {
            using (var stop = new CancellationTokenSource())
            {
                var server = new TcpUserServer(new RequestProcessor(new MemoryRepository()));
                Task running = server.RunAsync(0, stop.Token); int port = await server.Ready.Task;
                try
                {
                    using (var a = new TcpUserClient()) using (var b = new TcpUserClient())
                    {
                        await Task.WhenAll(a.ConnectAsync("127.0.0.1", port), b.ConnectAsync("127.0.0.1", port));
                        async Task EchoTwenty(TcpUserClient client, string prefix)
                        {
                            for (int i = 0; i < 20; i++)
                            {
                                string text = prefix + i + "\nTiếng Việt";
                                var result = await client.SendAsync(new Request { Command = Commands.Echo, Echo = text });
                                if (result.Code != Codes.Ok || result.Echo != text) throw new IOException("Echo mismatch.");
                            }
                        }
                        await Task.WhenAll(EchoTwenty(a, "A"), EchoTwenty(b, "B")); Check(true, "N01 two TCP clients each receive twenty distinct messages");
                        var results = await Task.WhenAll(a.SendAsync(Register("sameuser")), b.SendAsync(Register("sameuser")));
                        Check(results.Count(x => x.Code == Codes.Ok) == 1 && results.Count(x => x.Code == Codes.Exists) == 1, "N02 concurrent same-name registration has one winner (test repository)");
                        Check((await a.SendAsync(new Request { Command = Commands.Login, Username = "sameuser", Password = "Test1234!" })).Code == Codes.Ok, "N03 login over TCP");
                        Check((await a.SendAsync(new Request { Command = Commands.Profile })).Code == Codes.Ok, "N04 authenticated profile over TCP");
                        Check((await a.SendAsync(new Request { Command = Commands.Logout })).Code == Codes.Ok, "N05 explicit logout over TCP");
                    }
                    for (int i = 0; i < 50 && server.ClientCount != 0; i++) await Task.Delay(20);
                    Check(server.ClientCount == 0, "N06 EOF/close removes all sessions");
                }
                finally { stop.Cancel(); await running; }
                Check(server.ClientCount == 0, "N07 graceful server shutdown");
            }
        }
        private static void RenderForms(string output)
        {
            Directory.CreateDirectory(output);
            using (var login = new LoginForm()) using (var register = new RegisterForm()) using (var main = new MainForm())
            {
                foreach (var item in new[] { Tuple.Create<Form,string>(login,"login"), Tuple.Create<Form,string>(register,"register"), Tuple.Create<Form,string>(main,"main") })
                {
                    item.Item1.ShowInTaskbar = false; item.Item1.StartPosition = FormStartPosition.Manual;
                    item.Item1.Location = new Point(-10000, -10000); item.Item1.Show(); item.Item1.PerformLayout();
                    using (var bitmap = new Bitmap(item.Item1.Width, item.Item1.Height))
                    { item.Item1.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output,item.Item2+".png")); }
                    item.Item1.Hide();
                }
            }
            Check(true, "UI01 original forms instantiate and render offscreen (not a live TCP screenshot)");
            Check(!typeof(LoginForm).Assembly.GetReferencedAssemblies().Any(x => x.Name.Contains("SqlClient") || x.Name == "System.Data"), "UI02 client assembly has no SQL data access reference");
        }
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Contains("--verify-ports")) return LabTestPorts.Verify();
            string output = args.FirstOrDefault(x => !x.StartsWith("--")) ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "artifacts");
            Directory.CreateDirectory(output);
            if (args.Contains("--ui-local"))
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                return UiVerification.RunOffline(output);
            }
            if (args.Contains("--ui"))
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string uiRoot = args.FirstOrDefault(x => x.StartsWith("--root="));
                if (uiRoot == null) throw new ArgumentException("UI verification requires --root=<project path>.");
                return UiVerification.Run(output, uiRoot.Substring(7));
            }
            if (args.Contains("--sql"))
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string rootArgument = args.FirstOrDefault(x => x.StartsWith("--root="));
                if (rootArgument == null) throw new ArgumentException("SQL verification requires --root=<project path>.");
                return SqlVerification.Run(output, rootArgument.Substring(7));
            }
            int code = 0;
            try
            {
                FramesAsync().GetAwaiter().GetResult(); AuthenticationAsync().GetAwaiter().GetResult();
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); RenderForms(output);
                // Render tạo WindowsFormsSynchronizationContext nhưng test console không chạy message loop.
                SynchronizationContext.SetSynchronizationContext(null);
                if (args.Contains("--network"))
                {
                    try { NetworkAsync().GetAwaiter().GetResult(); }
                    catch (SocketException) { results.Add("BLOCKED TCP network connection not permitted by execution environment"); code = 2; }
                    catch (IOException) { results.Add("BLOCKED TCP stream/network connection could not be completed"); code = 2; }
                }
                else results.Add("NOT_RUN live TCP network tests (pass --network to enable)");
                results.Add("NOT_RUN real SQL Server registration/login/migration (SQL instance unavailable during this run)");
                results.Add("NOT_RUN two-machine LAN, actual Wi-Fi loss, live screenshot evidence and remote Git publication");
            }
            catch (Exception error) { results.Add("FAIL " + error.Message); Console.WriteLine(error); code = 1; }
            results.Add("ExitCode=" + code);
            File.WriteAllLines(Path.Combine(output, "test-results.txt"), results, new UTF8Encoding(false));
            Console.WriteLine("Results written. ExitCode=" + code); return code;
        }
    }
}
