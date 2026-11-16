using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuanLiNguoiDung;
using UserManagement.Server;
using UserManagement.Shared;

namespace UserManagement.Tests
{
    // Runs only when explicitly launched with --sql. Never changes an existing application database.
    internal static class SqlVerification
    {
        private const string Password = "LabTcp2026!";
        private static readonly List<string> results = new List<string>();
        private static readonly ConcurrentQueue<string> logs = new ConcurrentQueue<string>();
        private static string connectionString;
        private static string output;
        private static EvidenceWindow window;
        private static int exitCode = 1;

        public static int Run(string destination, string root)
        {
            output = Path.GetFullPath(destination); Directory.CreateDirectory(output);
            using (window = new EvidenceWindow())
            {
                window.Shown += async (sender, args) => {
                    try { await VerifyAsync(root); exitCode = 0; }
                    catch (Exception error) { results.Add("FAIL " + error); Console.WriteLine(error); }
                    finally {
                        results.Add("NOT_RUN physical Wi-Fi loss and two-machine LAN");
                        results.Add("ExitCode=" + exitCode);
                        File.WriteAllLines(Path.Combine(output, "sql-test-results.txt"), results, new UTF8Encoding(false));
                        File.WriteAllLines(Path.Combine(output, "server-live.log"), logs, new UTF8Encoding(false));
                        window.Close();
                    }
                };
                Application.Run(window);
            }
            return exitCode;
        }
        private static void Check(bool condition, string name)
        {
            string line = (condition ? "PASS " : "FAIL ") + name;
            results.Add(line); Console.WriteLine(line);
            if (!condition) throw new InvalidOperationException(name);
        }
        private static async Task ExecuteAsync(string sql, string cs = null)
        {
            using (var c = new SqlConnection(cs ?? connectionString))
            using (var command = new SqlCommand(sql, c) { CommandTimeout = 30 })
            { await c.OpenAsync(); await command.ExecuteNonQueryAsync(); }
        }
        private static async Task<object> ScalarAsync(string sql)
        {
            using (var c = new SqlConnection(connectionString))
            using (var command = new SqlCommand(sql, c))
            { await c.OpenAsync(); return await command.ExecuteScalarAsync(); }
        }
        private static async Task SetupAsync(string script, string master)
        {
            // USE must remain effective across GO batches, just as it is in SSMS/sqlcmd.
            using (var c = new SqlConnection(master))
            {
                await c.OpenAsync();
                foreach (string batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                    if (!String.IsNullOrWhiteSpace(batch))
                        using (var command = new SqlCommand(batch, c) { CommandTimeout = 30 }) await command.ExecuteNonQueryAsync();
            }
        }
        private static Request Register(string username, string name)
        {
            return new Request { Command = Commands.Register, Username = username, Password = Password,
                Email = username + "@example.com", FullName = name, Birthday = "2004-01-02" };
        }
        private static async Task EchoAsync(TcpUserClient client, string prefix)
        {
            for (int i = 0; i < 20; i++) {
                string value = prefix + i + "\nTiếng Việt";
                var r = await client.SendAsync(new Request { Command = Commands.Echo, Echo = value });
                if (r.Code != Codes.Ok || r.Echo != value) throw new IOException("Echo mismatch");
            }
        }
        private static async Task WaitAsync(Func<bool> predicate)
        {
            for (int i = 0; i < 100 && !predicate(); i++) await Task.Delay(50);
            if (!predicate()) throw new TimeoutException("Expected connection/UI state was not reached.");
        }
        private static async Task<Response> RawProfileAsync(int port, string token)
        {
            using (var client = new TcpClient()) {
                await client.ConnectAsync("127.0.0.1", port);
                using (var stream = client.GetStream()) {
                    await FrameCodec.WriteAsync(stream, new Request { Command = Commands.Profile, Token = token }, CancellationToken.None);
                    return FrameCodec.Deserialize<Response>(await FrameCodec.ReadAsync(stream, CancellationToken.None));
                }
            }
        }
        private static void Capture(Control control, string filename)
        {
            control.PerformLayout(); control.Refresh();
            using (var bitmap = new Bitmap(control.Width, control.Height)) {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(output, filename));
            }
        }
        private static async Task VerifyAsync(string root)
        {
            string database = "NT106_Bai3_Test_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 4);
            string instance = @"(localdb)\MSSQLLocalDB";
            var builder = new SqlConnectionStringBuilder { DataSource = instance, InitialCatalog = "master", IntegratedSecurity = true, ConnectTimeout = 8 };
            string master = builder.ConnectionString;
            await ExecuteAsync("CREATE DATABASE [" + database + "]", master);
            builder.InitialCatalog = database; connectionString = builder.ConnectionString;
            File.WriteAllText(Path.Combine(output, "server-connection.txt"), connectionString, new UTF8Encoding(false));
            File.WriteAllLines(Path.Combine(output, "environment.txt"), new[] {
                "Time=" + DateTimeOffset.Now.ToString("o"), "OS=" + Environment.OSVersion,
                "CLR=" + Environment.Version, "SQL instance=" + instance, "Database=" + database,
                "Application repository=System.Data.SqlClient DatabaseHelper (real SQL)",
                "Evidence image=DrawToBitmap of LIVE connected MainForms and running server log, not an OS desktop screenshot",
                "Test database is retained. No existing user database was changed or dropped." }, new UTF8Encoding(false));
            window.SetStatus("SQL thật: " + instance + " | CSDL thử riêng: " + database);
            await ExecuteAsync(@"CREATE TABLE dbo.Users(TenDangNhap nvarchar(20) NOT NULL, MatKhauBam nvarchar(64) NOT NULL,
                Salt nvarchar(32) NOT NULL, HoTen nvarchar(50) NOT NULL, Email nvarchar(100), LanDangNhapCuoi datetime2)");
            string salt = PasswordHasher.Salt(); string hash = await Task.Run(() => PasswordHasher.Hash(Password, salt));
            using (var c = new SqlConnection(connectionString))
            using (var command = new SqlCommand("INSERT dbo.Users VALUES(@name,@hash,@salt,N'Tài kế thừa',N'legacy@example.com',NULL)", c)) {
                command.Parameters.Add("@name", SqlDbType.NVarChar, 20).Value = "legacy_tai";
                command.Parameters.Add("@hash", SqlDbType.NVarChar, 64).Value = hash;
                command.Parameters.Add("@salt", SqlDbType.NVarChar, 32).Value = salt;
                await c.OpenAsync(); await command.ExecuteNonQueryAsync();
            }
            string setup = File.ReadAllText(Path.Combine(root, "Database", "Setup.sql"), Encoding.UTF8).Replace("QuanLyNguoiDung", database);
            await SetupAsync(setup, master);
            var repository = new DatabaseHelper(connectionString); await repository.ProbeAsync(CancellationToken.None);
            Check((int)await ScalarAsync("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.NguoiDung')") == 8,
                "S01 Setup.sql creates canonical table and server DAL probe succeeds");
            var legacy = await repository.FindAsync("legacy_tai", CancellationToken.None);
            Check(legacy != null && legacy.PasswordHash == hash && legacy.PasswordSalt == salt && legacy.Info.Birthday == null &&
                (int)await ScalarAsync("SELECT COUNT(*) FROM dbo.Users") == 1, "S02 migration preserves legacy account, hash/salt and Users table");
            await SetupAsync(setup, master);
            Check((int)await ScalarAsync("SELECT COUNT(*) FROM dbo.NguoiDung") == 1, "S03 repeating Setup.sql does not duplicate migrated users");
            Check((int)await ScalarAsync("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.NguoiDung') AND is_unique=1 AND name='UQ_NguoiDung_Username'") == 1,
                "S04 SQL UNIQUE Username constraint exists");
            long clockTicks = DateTime.UtcNow.Ticks;
            using (var stop = new CancellationTokenSource()) {
                var server = new TcpUserServer(new RequestProcessor(repository, clock: () => new DateTime(Interlocked.Read(ref clockTicks), DateTimeKind.Utc)));
                server.Log += line => { logs.Enqueue(line); window.AppendLog(line); };
                Task running = server.RunAsync(0, stop.Token); int port = await server.Ready.Task;
                File.AppendAllText(Path.Combine(output, "environment.txt"), Environment.NewLine + "TCP=127.0.0.1:" + port);
                try {
                    using (var a = new TcpUserClient()) using (var b = new TcpUserClient()) {
                        await Task.WhenAll(a.ConnectAsync("127.0.0.1", port), b.ConnectAsync("127.0.0.1", port));
                        await Task.WhenAll(EchoAsync(a, "A"), EchoAsync(b, "B"));
                        Check(true, "S05 two clients exchange 40 messages with the real SQL-backed server");
                        var race = await Task.WhenAll(a.SendAsync(Register("race_tai", "Tài O'Connor")), b.SendAsync(Register("race_tai", "Tài O'Connor")));
                        Check(race.Count(r => r.Code == Codes.Ok) == 1 && race.Count(r => r.Code == Codes.Exists) == 1 &&
                            (int)await ScalarAsync("SELECT COUNT(*) FROM dbo.NguoiDung WHERE Username=N'race_tai'") == 1,
                            "S06 concurrent duplicate registration inserts exactly one SQL row");
                        Check((await repository.FindAsync("race_tai", CancellationToken.None)).Info.FullName == "Tài O'Connor",
                            "S07 parameterized INSERT preserves Unicode and apostrophe");
                        var registered = await Task.WhenAll(a.SendAsync(Register("tai_client1", "Tài - Client 1")), b.SendAsync(Register("tai_client2", "Tài - Client 2")));
                        Check(registered.All(r => r.Code == Codes.Ok), "S08 two registrations succeed through TCP and SQL");
                        var storedA = await repository.FindAsync("tai_client1", CancellationToken.None);
                        var storedB = await repository.FindAsync("tai_client2", CancellationToken.None);
                        Check(storedA.PasswordHash != storedB.PasswordHash && storedA.PasswordSalt != storedB.PasswordSalt && storedA.PasswordHash != Password,
                            "S09 SQL stores different salts/hashes for the same password");
                        var wrongName = await a.SendAsync(new Request { Command = Commands.Login, Username = "missing_user", Password = Password });
                        var wrongPassword = await a.SendAsync(new Request { Command = Commands.Login, Username = "tai_client1", Password = "WrongPassword!" });
                        Check(wrongName.Code == Codes.AuthFailed && wrongPassword.Code == Codes.AuthFailed, "S10 unknown username and wrong password return AUTH_FAILED over TCP");
                        var invalid = Register("invalid_user", "Invalid"); invalid.Email = "bad-email";
                        Check((await b.SendAsync(invalid)).Code == Codes.Invalid, "S11 server rejects invalid registration over TCP before SQL insert");
                        var oldLogin = await a.SendAsync(new Request { Command = Commands.Login, Username = "legacy_tai", Password = Password });
                        Check(oldLogin.Code == Codes.Ok, "S12 migrated legacy account logs in through TCP against real SQL");
                        Check((await a.SendAsync(new Request { Command = Commands.Logout })).Code == Codes.Ok, "S13 logout succeeds against real SQL-backed server");
                    }
                    using (var loginA = new LoginForm()) using (var loginB = new LoginForm()) {
                        await Task.WhenAll(loginA.Connection.ConnectAsync("127.0.0.1", port), loginB.Connection.ConnectAsync("127.0.0.1", port));
                        var responses = await Task.WhenAll(
                            loginA.Connection.SendAsync(new Request { Command = Commands.Login, Username = "tai_client1", Password = Password }),
                            loginB.Connection.SendAsync(new Request { Command = Commands.Login, Username = "tai_client2", Password = Password }));
                        Check(responses.All(r => r.Code == Codes.Ok && r.Token != null), "S14 two authenticated clients coexist using real SQL accounts");
                        var profiles = await Task.WhenAll(loginA.Connection.SendAsync(new Request { Command = Commands.Profile }), loginB.Connection.SendAsync(new Request { Command = Commands.Profile }));
                        Check(profiles[0].User.Username == "tai_client1" && profiles[1].User.Username == "tai_client2" &&
                            profiles.All(r => r.Code == Codes.Ok && r.User.Birthday == "2004-01-02" && !String.IsNullOrEmpty(r.User.LastLogin)),
                            "S15 profile and updated LastLogin are read from SQL through TCP");
                        Check(!Encoding.UTF8.GetString(FrameCodec.Serialize(profiles[0])).Contains("Password"), "S16 live TCP profile response contains no password/hash/salt fields");
                        Check((await RawProfileAsync(port, responses[0].Token)).Code == Codes.Unauthorized, "S17 token cannot be reused on another TCP connection");
                        using (var mainA = new MainForm(profiles[0].User, loginA)) using (var mainB = new MainForm(profiles[1].User, loginB)) {
                            window.Attach(mainA, mainB); await Task.Delay(500);
                            await WaitAsync(() => server.ClientCount == 2);
                            window.SetStatus("SQL thật: " + database + " | TCP 127.0.0.1:" + port + " | 2 phiên đang đăng nhập");
                            Capture(window, "hai-client-sql-live.png"); Capture(mainA, "client1-sql-live.png"); Capture(mainB, "client2-sql-live.png");
                            File.WriteAllLines(Path.Combine(output, "server-at-capture.log"), logs, new UTF8Encoding(false));
                            Check(File.Exists(Path.Combine(output, "hai-client-sql-live.png")), "S18 captures LIVE MainForms populated by SQL/TCP responses alongside running server log");
                            Interlocked.Exchange(ref clockTicks, DateTime.UtcNow.AddMinutes(31).Ticks);
                            Check((await loginA.Connection.SendAsync(new Request { Command = Commands.Profile })).Code == Codes.Expired,
                                "S19 expiry rejects token over TCP (server clock advanced by test, no 30-minute wait)");
                            Interlocked.Exchange(ref clockTicks, DateTime.UtcNow.Ticks);
                            var logoutButton = mainB.Controls.Find("btnDangXuat", true).OfType<Button>().Single(); logoutButton.PerformClick();
                            await WaitAsync(() => mainB.IsDisposed && !loginB.Connection.Connected);
                            Check(true, "S20 actual MainForm logout button closes the TCP session and returns login");
                            mainA.Close(); await WaitAsync(() => server.ClientCount == 0);
                            Check(true, "S21 closing actual MainForm removes its TCP session");
                        }
                    }
                    using (var a = new TcpUserClient()) using (var b = new TcpUserClient()) {
                        await Task.WhenAll(a.ConnectAsync("127.0.0.1", port), b.ConnectAsync("127.0.0.1", port));
                        await WaitAsync(() => server.ClientCount == 2);
                        stop.Cancel(); await running;
                        await WaitAsync(() => !a.Connected && !b.Connected);
                        Check(server.ClientCount == 0, "S22 graceful server stop disconnects two clients and removes all sessions");
                    }
                    var persisted = await new DatabaseHelper(connectionString).FindAsync("tai_client1", CancellationToken.None);
                    Check(persisted != null && persisted.Info.LastLogin != null, "S23 a new repository connection sees persisted SQL account and LastLogin");
                    File.WriteAllLines(Path.Combine(output, "sql-summary.txt"), new[] { "Synthetic Users rows=1 (preserved)", "Canonical NguoiDung rows=" + await ScalarAsync("SELECT COUNT(*) FROM dbo.NguoiDung"),
                        "Two same-password accounts have independent hash and salt: verified, credential material omitted.", "Test database=" + database }, new UTF8Encoding(false));
                } finally { stop.Cancel(); await running; }
            }
        }
        private sealed class EvidenceWindow : Form
        {
            private readonly Label status = new Label { Dock = DockStyle.Top, Height = 48, Padding = new Padding(12), Font = new Font("Segoe UI", 10), Text = "Đang kiểm tra SQL/TCP thực tế..." };
            private readonly Panel clients = new Panel { Dock = DockStyle.Top, Height = 540 };
            private readonly RichTextBox log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9), BackColor = Color.FromArgb(24, 30, 38), ForeColor = Color.White };
            public EvidenceWindow() {
                Text = "NT106 - Minh chứng SQL/TCP thực tế - Tài"; AutoScaleMode = AutoScaleMode.None;
                ClientSize = new Size(1490, 920); StartPosition = FormStartPosition.CenterScreen;
                Controls.Add(log); Controls.Add(clients); Controls.Add(status);
            }
            public void SetStatus(string text) { status.Text = text; }
            public void AppendLog(string line) {
                if (IsDisposed || !IsHandleCreated) return;
                if (InvokeRequired) { try { BeginInvoke((Action)(() => AppendLog(line))); } catch (InvalidOperationException) { } return; }
                log.AppendText(line + Environment.NewLine); log.SelectionStart = log.TextLength; log.ScrollToCaret();
            }
            public void Attach(MainForm a, MainForm b) {
                int x = 5;
                foreach (var form in new[] { a, b }) {
                    form.TopLevel = false; form.Location = new Point(x, 10); clients.Controls.Add(form); form.Show(); x += 740;
                }
            }
        }
    }
}
