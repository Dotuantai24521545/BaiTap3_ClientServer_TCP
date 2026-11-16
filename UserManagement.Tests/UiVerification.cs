using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuanLiNguoiDung;
using UserManagement.Server;
using UserManagement.Shared;

namespace UserManagement.Tests
{
    internal static class UiVerification
    {
        private const string Password = "LabUi2026!";
        private static readonly List<string> results = new List<string>();
        private static readonly ConcurrentQueue<string> logs = new ConcurrentQueue<string>();
        private static readonly List<string> notices = new List<string>();
        private static string output;
        private static int exitCode = 1;
        private static Form runner;
        private static Label status;
        private static RichTextBox display;
        private static int ticks;
        private static int unhandledUiErrors;
        private sealed class RecordedDialogs : IUserDialogs
        {
            public void Show(IWin32Window owner, string text, string caption)
            { notices.Add(text); display.AppendText("UI notice: " + text + Environment.NewLine); }
        }
        private sealed class DelayedRepository : IUserRepository
        {
            private readonly DatabaseHelper database;
            public int Writes;
            public DelayedRepository(DatabaseHelper database) { this.database = database; }
            public async Task<StoredUser> FindAsync(string name, CancellationToken token)
            { await Task.Delay(650, token); return await database.FindAsync(name, token); }
            public async Task<bool> CreateAsync(Request r, string h, string s, CancellationToken token)
            { Interlocked.Increment(ref Writes); await Task.Delay(650, token); return await database.CreateAsync(r, h, s, token); }
            public Task UpdateLastLoginAsync(int id, CancellationToken token) { return database.UpdateLastLoginAsync(id, token); }
        }
        public static int RunOffline(string destination)
        {
            output = Path.GetFullPath(destination); Directory.CreateDirectory(output);
            var evidence = new List<string>(); int code = 0;
            Action<bool, string> verify = (passed, name) => {
                string line = (passed ? "PASS " : "FAIL ") + name; evidence.Add(line); Console.WriteLine(line);
                if (!passed) throw new InvalidOperationException(name);
            };
            try {
                using (var form = new RegisterForm()) {
                    Show(form); Application.DoEvents(); var button = Control<Button>(form, "btnDangKy");
                    var error = Control<Label>(form, "lblLoiChung");
                    button.PerformClick();
                    verify(button.Enabled && error.Text.Contains("Điền đủ"), "L01 actual Register click rejects empty required fields");
                    Fill(form, "offline_user"); Control<TextBox>(form, "txtHoTen").Clear(); button.PerformClick();
                    verify(button.Enabled && error.Text.Contains("Điền đủ"), "L02 actual Register click rejects empty full name");
                    Fill(form, "offline_user"); Control<TextBox>(form, "txtEmail").Text = "bad-email"; button.PerformClick();
                    verify(button.Enabled && error.Text.Contains("email"), "L03 actual Register click rejects malformed email");
                    Fill(form, "offline_user"); Control<TextBox>(form, "txtXacNhanMatKhau").Text = "MismatchPassword!"; button.PerformClick();
                    verify(button.Enabled && error.Text.Contains("khớp"), "L04 actual Register click rejects mismatched confirmation");
                    Fill(form, "offline_user"); Control<TextBox>(form, "txtTenDangNhap").Text = "bad user'"; button.PerformClick();
                    verify(button.Enabled && error.Text.Contains("Điền đủ"), "L05 actual Register click rejects invalid username characters");
                    Fill(form, "offline_user"); Control<TextBox>(form, "txtMatKhau").Text = "short"; Control<TextBox>(form, "txtXacNhanMatKhau").Text = "short"; button.PerformClick();
                    verify(button.Enabled && error.Text.Contains("mật khẩu"), "L06 actual Register click rejects a short password");
                    verify(Control<TextBox>(form, "txtTenDangNhap").MaxLength == 20 && Control<TextBox>(form, "txtHoTen").MaxLength == 50 &&
                        Control<TextBox>(form, "txtEmail").MaxLength == 100 && Control<TextBox>(form, "txtMatKhau").MaxLength == 128,
                        "L07 registration controls enforce declared input lengths");
                    verify(Control<DateTimePicker>(form, "dateBirthday").MaxDate.Date == DateTime.Today && Control<DateTimePicker>(form, "dateBirthday").MinDate.Year == 1900,
                        "L08 birthday picker restricts range to 1900 through today");
                    Control<TextBox>(form, "txtMatKhau").Clear(); Control<TextBox>(form, "txtXacNhanMatKhau").Clear(); Capture(form, "register-validation-live.png");
                    Control<Button>(form, "btnHuy").PerformClick(); verify(form.IsDisposed, "L09 actual registration Cancel button closes the form");
                }
                using (var form = new LoginForm()) {
                    Show(form); Application.DoEvents(); var button = Control<Button>(form, "btnDangNhap");
                    button.PerformClick(); verify(Control<Label>(form, "lblLoiTen").Text != "" && !form.Connection.Connected, "L10 actual Login click rejects empty username before TCP");
                    LoginFields(form, "offline_user", "", 9003); button.PerformClick();
                    verify(Control<Label>(form, "lblLoiMatKhau").Text != "" && !form.Connection.Connected, "L11 actual Login click rejects empty password before TCP");
                    LoginFields(form, "offline_user", Password, 9003); Control<TextBox>(form, "txtServerIp").Text = "invalid-ip"; button.PerformClick();
                    verify(Control<Label>(form, "lblLoiChung").Text.Contains("IP") && !form.Connection.Connected, "L12 actual Login click rejects invalid server IP before TCP");
                    Control<TextBox>(form, "txtMatKhau").Clear(); Capture(form, "login-invalid-ip-live.png");
                    LoginFields(form, "bad user'", Password, 9003); button.PerformClick();
                    verify(Control<Label>(form, "lblLoiTen").Text != "" && !form.Connection.Connected, "L13 actual Login click rejects invalid username characters");
                    verify(Control<TextBox>(form, "txtTenDangNhap").MaxLength == 20 && Control<TextBox>(form, "txtMatKhau").MaxLength == 128 &&
                        Control<NumericUpDown>(form, "numServerPort").Minimum == 1024 && Control<NumericUpDown>(form, "numServerPort").Maximum == 49151,
                        "L14 actual Login controls enforce lengths and allowed port range");
                    Control<Button>(form, "btnThoat").PerformClick(); verify(form.IsDisposed && !form.Connection.Connected, "L15 actual Login Exit button disposes form and connection");
                }
            } catch (Exception error) { evidence.Add("FAIL " + error); Console.WriteLine(error); code = 1; }
            evidence.Add("Scope=real control text assignments and actual button Click handlers, no SQL/TCP needed. Not human manual typing.");
            evidence.Add("NOT_RUN SQL-dependent UI workflows, packet blackhole, physical Wi-Fi loss and GitHub publication in this local-only run.");
            evidence.Add("ExitCode=" + code);
            File.WriteAllLines(Path.Combine(output, "ui-local-results.txt"), evidence, new UTF8Encoding(false));
            return code;
        }
        public static int Run(string destination, string root)
        {
            output = Path.GetFullPath(destination); Directory.CreateDirectory(output);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => {
                unhandledUiErrors++;
                string failure = "FAIL unhandled UI exception: " + e.Exception;
                results.Add(failure); Console.WriteLine(failure);
                if (display != null && !display.IsDisposed) display.AppendText(failure + Environment.NewLine);
            };
            using (runner = new Form { Text = "NT106 - Kiểm tra thao tác UI và mất mạng mô phỏng", ClientSize = new Size(850, 440), StartPosition = FormStartPosition.CenterScreen })
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                status = new Label { Dock = DockStyle.Top, Height = 52, Padding = new Padding(8), Text = "Đang kiểm tra các sự kiện thật trên form..." };
                display = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10) };
                runner.Controls.Add(display); runner.Controls.Add(status);
                timer.Tick += (s, e) => ticks++;
                runner.Shown += async (s, e) => {
                    timer.Start();
                    try { await VerifyAsync(root); exitCode = results.Any(x => x.StartsWith("FAIL ")) ? 1 : 0; }
                    catch (Exception error) { results.Add("FAIL " + error); Console.WriteLine(error); }
                    finally {
                        timer.Stop();
                        results.Add("NOT_RUN physical Wi-Fi adapter loss and two-machine LAN; blackhole test is a transport simulation.");
                        results.Add("UI automation sets real control text and invokes real Click/FormClosing handlers; it is not a human typing session.");
                        results.Add("Modal notices are recorded through an injected IUserDialogs; normal app still uses MessageBox.");
                        results.Add("ExitCode=" + exitCode);
                        File.WriteAllLines(Path.Combine(output, "ui-test-results.txt"), results, new UTF8Encoding(false));
                        File.WriteAllLines(Path.Combine(output, "server-ui.log"), logs, new UTF8Encoding(false));
                        File.WriteAllLines(Path.Combine(output, "ui-notices.txt"), notices, new UTF8Encoding(false));
                        runner.Close();
                    }
                };
                Application.Run(runner);
            }
            return exitCode;
        }
        private static T Control<T>(System.Windows.Forms.Control parent, string name) where T : System.Windows.Forms.Control
        { return parent.Controls.Find(name, true).OfType<T>().Single(); }
        private static void Check(bool value, string name)
        {
            string line = (value ? "PASS " : "FAIL ") + name; results.Add(line); Console.WriteLine(line);
            display.AppendText(line + Environment.NewLine); status.Text = name;
            if (!value) throw new InvalidOperationException(name);
        }
        private static async Task WaitAsync(Func<bool> predicate, int timeout = 18000)
        {
            var clock = Stopwatch.StartNew();
            while (!predicate() && clock.ElapsedMilliseconds < timeout) await Task.Delay(40);
            if (!predicate()) throw new TimeoutException("Expected UI/network state was not reached.");
        }
        private static void Capture(System.Windows.Forms.Control control, string name)
        {
            control.Refresh();
            using (var bitmap = new Bitmap(control.Width, control.Height)) {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(output, name));
            }
        }
        private static void Show(Form form) { form.StartPosition = FormStartPosition.Manual; form.Location = new Point(30, 30); form.Show(); }
        private static void Fill(RegisterForm form, string name)
        {
            Control<TextBox>(form, "txtTenDangNhap").Text = name;
            Control<TextBox>(form, "txtHoTen").Text = "Tài kiểm tra giao diện";
            Control<TextBox>(form, "txtEmail").Text = name + "@example.com";
            Control<TextBox>(form, "txtMatKhau").Text = Password;
            Control<TextBox>(form, "txtXacNhanMatKhau").Text = Password;
            Control<DateTimePicker>(form, "dateBirthday").Value = new DateTime(2004, 1, 2);
        }
        private static void LoginFields(LoginForm form, string username, string password, int port)
        {
            Control<TextBox>(form, "txtTenDangNhap").Text = username;
            Control<TextBox>(form, "txtMatKhau").Text = password;
            Control<TextBox>(form, "txtServerIp").Text = "127.0.0.1";
            Control<NumericUpDown>(form, "numServerPort").Value = port;
        }
        private static bool LoginLocked(LoginForm form)
        {
            return !Control<Button>(form, "btnDangNhap").Enabled && !Control<LinkLabel>(form, "lnkDangKy").Enabled &&
                !Control<TextBox>(form, "txtServerIp").Enabled && !Control<NumericUpDown>(form, "numServerPort").Enabled;
        }
        private static MainForm MainWindow() { return Application.OpenForms.OfType<MainForm>().SingleOrDefault(x => !x.IsDisposed); }
        private static async Task VerifyAsync(string root)
        {
            string cs = File.ReadAllText(Path.Combine(root, "TaiLieu", "MinhChung_SQL", "server-connection.txt"), Encoding.UTF8).Trim();
            var builder = new SqlConnectionStringBuilder(cs);
            if (!builder.InitialCatalog.StartsWith("NT106_Bai3_Test_", StringComparison.Ordinal)) throw new InvalidOperationException("UI tests require an isolated NT106 test database.");
            var database = new DatabaseHelper(cs); await database.ProbeAsync(CancellationToken.None);
            var repository = new DelayedRepository(database);
            string name = "ui_" + Guid.NewGuid().ToString("N").Substring(0, 9);
            File.WriteAllLines(Path.Combine(output, "environment.txt"), new[] { DateTimeOffset.Now.ToString("o"), "SQL=" + builder.DataSource,
                "Database=" + builder.InitialCatalog, "Synthetic UI account=" + name, "SQL lookups/writes have a test-only 650ms delay to measure UI responsiveness.",
                "No adapter, firewall, route or OS network settings are changed." }, new UTF8Encoding(false));
            var dialogs = new RecordedDialogs();
            using (var stop = new CancellationTokenSource()) {
                var server = new TcpUserServer(new RequestProcessor(repository));
                server.Log += line => logs.Enqueue(line);
                Task running = server.RunAsync(LabTestPorts.AvailableServerPort(), stop.Token); int port = await server.Ready.Task;
                try {
                    using (var form = new RegisterForm("127.0.0.1", port, dialogs)) {
                        Show(form); var button = Control<Button>(form, "btnDangKy"); var error = Control<Label>(form, "lblLoiChung");
                        button.PerformClick();
                        Check(!String.IsNullOrWhiteSpace(error.Text) && server.ClientCount == 0 && repository.Writes == 0,
                            "U01 empty registration is rejected on the actual form before connecting");
                        Fill(form, name); Control<TextBox>(form, "txtXacNhanMatKhau").Text = "DifferentPassword!"; button.PerformClick();
                        Check(error.Text.Contains("khớp") && server.ClientCount == 0, "U02 actual register button rejects mismatched confirmation");
                        Fill(form, name); Control<TextBox>(form, "txtEmail").Text = "invalid-email"; button.PerformClick();
                        Check(error.Text.Contains("email") && server.ClientCount == 0, "U03 actual register button rejects malformed email before TCP");
                        Capture(form, "register-invalid-email.png"); Fill(form, name); int before = ticks; button.PerformClick();
                        Check(!button.Enabled, "U04 register button is disabled during real SQL/TCP request");
                        await WaitAsync(() => form.IsDisposed);
                        Check(ticks > before + 2 && notices.Any(x => x.Contains("Đăng ký thành công")) && await database.FindAsync(name, CancellationToken.None) != null,
                            "U05 actual registration inserts SQL account, reports success and leaves UI responsive");
                    }
                    using (var form = new RegisterForm("127.0.0.1", port, dialogs)) {
                        Show(form); Fill(form, name); var button = Control<Button>(form, "btnDangKy"); button.PerformClick();
                        await WaitAsync(() => button.Enabled);
                        Check(Control<Label>(form, "lblLoiChung").Text == Codes.Message(Codes.Exists) &&
                            Control<TextBox>(form, "txtMatKhau").Text == "" && Control<TextBox>(form, "txtXacNhanMatKhau").Text == "",
                            "U06 duplicate registration shows declared friendly error and clears both password boxes");
                        Capture(form, "register-duplicate.png"); form.Close();
                    }
                    await WaitAsync(() => server.ClientCount == 0);
                    using (var login = new LoginForm(dialogs)) {
                        Show(login); var button = Control<Button>(login, "btnDangNhap");
                        button.PerformClick();
                        Check(Control<Label>(login, "lblLoiTen").Text != "" && !login.Connection.Connected, "U07 empty login is rejected on actual form");
                        LoginFields(login, name, "short", port); button.PerformClick();
                        Check(Control<Label>(login, "lblLoiMatKhau").Text != "" && !login.Connection.Connected, "U08 short password is rejected before connecting");
                        LoginFields(login, name, Password, port); Control<TextBox>(login, "txtServerIp").Text = "invalid-ip"; button.PerformClick();
                        Check(Control<Label>(login, "lblLoiChung").Text != "" && !login.Connection.Connected, "U09 invalid server IP is rejected on actual form");
                        LoginFields(login, name, "WrongPassword!", port); button.PerformClick();
                        Check(LoginLocked(login), "U10 login, registration link and connection settings are locked while waiting");
                        await WaitAsync(() => button.Enabled);
                        string wrongPassword = Control<Label>(login, "lblLoiChung").Text;
                        Check(wrongPassword == Codes.Message(Codes.AuthFailed) && Control<TextBox>(login, "txtMatKhau").Text == "", "U11 wrong password shows friendly AUTH_FAILED and clears password box");
                        Capture(login, "login-auth-failed.png");
                        LoginFields(login, "ui_missing_name", Password, port); button.PerformClick(); await WaitAsync(() => button.Enabled);
                        Check(Control<Label>(login, "lblLoiChung").Text == wrongPassword && Control<TextBox>(login, "txtMatKhau").Text == "", "U12 missing username and wrong password have identical visible error");
                        int before = ticks; LoginFields(login, name, Password, port); button.PerformClick();
                        await WaitAsync(() => MainWindow() != null && button.Enabled);
                        var main = MainWindow();
                        Check(main.Visible && !login.Visible && Control<TextBox>(login, "txtMatKhau").Text == "" && ticks > before + 2,
                            "U13 actual login button opens MainForm, hides login, clears password and keeps UI responsive");
                        string newName = "Tài - Đã cập nhật qua SQL";
                        using (var c = new SqlConnection(cs)) using (var command = new SqlCommand("UPDATE dbo.NguoiDung SET FullName=@fullName WHERE Username=@username", c)) {
                            command.Parameters.AddWithValue("@fullName", newName); command.Parameters.AddWithValue("@username", name);
                            await c.OpenAsync(); await command.ExecuteNonQueryAsync();
                        }
                        Control<Button>(main, "btnRefresh").PerformClick();
                        Check(!Control<Button>(main, "btnRefresh").Enabled && !Control<Button>(main, "btnDangXuat").Enabled, "U14 MainForm locks profile/logout buttons during refresh");
                        await WaitAsync(() => Control<Button>(main, "btnRefresh").Enabled);
                        Check(Control<Label>(main, "lblXinChao").Text.Contains(newName), "U15 actual refresh button displays changed SQL data received through TCP");
                        Capture(main, "main-ui-refreshed.png"); Control<Button>(main, "btnDangXuat").PerformClick();
                        await WaitAsync(() => main.IsDisposed && login.Visible && !login.Connection.Connected);
                        Check(Control<TextBox>(login, "txtMatKhau").Text == "", "U16 actual logout button returns login with no retained password");
                        LoginFields(login, name, Password, port); button.PerformClick(); await WaitAsync(() => MainWindow() != null && button.Enabled);
                        MainWindow().Close(); await WaitAsync(() => login.IsDisposed && server.ClientCount == 0);
                        Check(true, "U17 closing actual MainForm also closes login/socket and removes session");
                    }
                    int errorsBeforeCancel = unhandledUiErrors;
                    bool allCanceledFormsDisposed = true;
                    for (int attempt = 0; attempt < 8; attempt++) {
                        using (var form = new RegisterForm("127.0.0.1", port, dialogs)) {
                            Show(form); Fill(form, "cancel_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                            Control<Button>(form, "btnDangKy").PerformClick(); form.Close();
                            allCanceledFormsDisposed &= form.IsDisposed;
                            await Task.Delay(60);
                        }
                    }
                    await Task.Delay(1300); await WaitAsync(() => server.ClientCount == 0);
                    Check(allCanceledFormsDisposed && unhandledUiErrors == errorsBeforeCancel,
                        "U18 eight RegisterForm cancellations during pending connection leave no session or unhandled UI error");
                    using (var proxy = new BlackholeProxy(port))
                    using (var login = new LoginForm(dialogs)) {
                        Show(login); LoginFields(login, name, Password, proxy.Port);
                        Control<Button>(login, "btnDangNhap").PerformClick(); await WaitAsync(() => MainWindow() != null && Control<Button>(login, "btnDangNhap").Enabled);
                        var main = MainWindow(); await WaitAsync(() => server.ClientCount == 1);
                        var lossClock = Stopwatch.StartNew(); proxy.DropTraffic = true; int before = ticks;
                        Control<Button>(main, "btnRefresh").PerformClick();
                        await WaitAsync(() => main.IsDisposed && login.Visible && !login.Connection.Connected, 22000);
                        Check(proxy.DroppedBytes > 0 && lossClock.ElapsedMilliseconds < 22000 && ticks > before + 20,
                            "U19 bidirectional packet blackhole with no FIN/RST triggers client deadline and returns login while UI stays responsive");
                        Capture(login, "login-after-blackhole.png");
                        status.Text = "Client đã phát hiện mất kết nối; đang chờ server dọn theo deadline đọc 45 giây...";
                        await WaitAsync(() => server.ClientCount == 0, 52000);
                        Check(lossClock.ElapsedMilliseconds < 55000 && logs.Any(x => x.Contains("REMOVE") && x.Contains("clients=0")),
                            "U20 server removes blackholed peer using its actual default 45-second read timeout");
                        File.WriteAllLines(Path.Combine(output, "blackhole-timing.txt"), new[] { "Packets were consumed and discarded in both directions; sockets intentionally kept open.",
                            "Dropped bytes=" + proxy.DroppedBytes, "Server cleanup elapsed milliseconds=" + lossClock.ElapsedMilliseconds,
                            "No Wi-Fi adapter was disabled; this is a reproducible transport-failure simulation." }, new UTF8Encoding(false));
                        login.Close();
                    }
                    Check(!notices.Any(x => x.Contains("Exception") || x.Contains("StackTrace")), "U21 all recorded UI notices are friendly and contain no exception class names");
                    Capture(runner, "ui-summary.png");
                } finally { stop.Cancel(); await running; }
            }
        }
        // A test-only transparent proxy. In drop mode it reads/discards data without forwarding FIN/RST.
        private sealed class BlackholeProxy : IDisposable
        {
            private readonly TcpListener listener;
            private TcpClient front, back;
            private volatile bool drop;
            private long dropped;
            public int Port { get; private set; }
            public bool DropTraffic { set { drop = value; } }
            public long DroppedBytes { get { return Interlocked.Read(ref dropped); } }
            public BlackholeProxy(int port) { listener = LabTestPorts.Listen(IPAddress.Loopback); Port = ((IPEndPoint)listener.LocalEndpoint).Port; var task = RelayAsync(port); }
            private async Task RelayAsync(int port)
            {
                try {
                    front = await listener.AcceptTcpClientAsync(); back = new TcpClient(); await back.ConnectAsync("127.0.0.1", port);
                    await Task.WhenAll(PumpAsync(front.GetStream(), back.GetStream()), PumpAsync(back.GetStream(), front.GetStream()));
                } catch (IOException) { } catch (SocketException) { } catch (ObjectDisposedException) { }
                finally { if (front != null) front.Close(); if (back != null) back.Close(); }
            }
            private async Task PumpAsync(NetworkStream from, NetworkStream to)
            {
                byte[] buffer = new byte[8192]; int n;
                while ((n = await from.ReadAsync(buffer, 0, buffer.Length)) != 0) {
                    if (drop) Interlocked.Add(ref dropped, n);
                    else await to.WriteAsync(buffer, 0, n);
                }
            }
            public void Dispose() { listener.Stop(); if (front != null) front.Close(); if (back != null) back.Close(); }
        }
    }
}
