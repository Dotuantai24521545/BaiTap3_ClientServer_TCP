using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Windows.Forms;
using UserManagement.Shared;

namespace QuanLiNguoiDung
{
    public partial class MainForm : Form
    {
        private readonly LoginForm login;
        private bool returnLogin;
        private bool busy;
        private readonly IUserDialogs dialogs;
        private readonly Label lblBirthday = new Label { Name = "lblBirthday", Location = new Point(16, 272), Size = new Size(690, 40), Font = new Font("Segoe UI", 14) };
        private readonly Label lblStatus = new Label { Name = "lblStatus", Location = new Point(16, 322), Size = new Size(690, 35), Font = new Font("Segoe UI", 10) };
        private readonly Button btnRefresh = new Button { Name = "btnRefresh", Text = "Lấy thông tin", Location = new Point(24, 375), Size = new Size(210, 60) };
        public MainForm() { InitializeComponent(); AutoScaleMode = AutoScaleMode.None; Controls.Add(lblBirthday); Controls.Add(lblStatus); Controls.Add(btnRefresh); }
        public MainForm(UserInfo user, LoginForm login, IUserDialogs dialogs = null) : this()
        {
            this.dialogs = dialogs ?? new MessageBoxDialogs();
            this.login = login; ShowUser(user); btnRefresh.Click += async (s, e) => await RefreshProfileAsync();
            login.Connection.Disconnected += OnDisconnected;
            FormClosing += (s, e) => {
                login.Connection.Disconnected -= OnDisconnected;
                login.Connection.Disconnect();
                if (returnLogin) login.ReturnToLogin(); else if (!login.IsDisposed) login.Close();
            };
        }
        private void ShowUser(UserInfo user)
        {
            lblXinChao.Text = "Xin chào, " + user.FullName; lblTenDangNhap.Text = "Tên đăng nhập: " + user.Username;
            lblEmail.Text = "Email: " + user.Email; lblBirthday.Text = "Ngày sinh: " + (user.Birthday ?? "Chưa cập nhật từ Bài 2.2");
            lblLanDangNhapCuoi.Text = "Lần đăng nhập gần nhất: " + (user.LastLogin ?? "Lần đầu");
            lblLanDangNhapCuoi.Size = new Size(690, 55); lblLanDangNhapCuoi.Font = new Font("Segoe UI", 11);
            lblStatus.Text = "Đã kết nối server qua TCP";
        }
        private async System.Threading.Tasks.Task RefreshProfileAsync()
        {
            if (busy || login == null) return;
            SetBusy(true);
            try
            {
                var response = await login.Connection.SendAsync(new Request { Command = Commands.Profile });
                if (IsDisposed) return;
                if (response.Code == Codes.Ok) ShowUser(response.User);
                else if (response.Code == Codes.Expired || response.Code == Codes.Unauthorized) { dialogs.Show(this, Codes.Message(response.Code), "Thông báo"); returnLogin = true; Close(); }
                else lblStatus.Text = Codes.Message(response.Code);
            }
            catch (SocketException) { if (!IsDisposed) { returnLogin = true; Close(); } }
            catch (IOException) { if (!IsDisposed) { returnLogin = true; Close(); } }
            finally { if (!IsDisposed) SetBusy(false); }
        }
        private async void btnDangXuat_Click(object sender, EventArgs e)
        {
            if (busy || login == null) return;
            SetBusy(true); returnLogin = true;
            try { await login.Connection.SendAsync(new Request { Command = Commands.Logout }); }
            catch (SocketException) { }
            catch (IOException) { }
            finally { if (!IsDisposed) Close(); }
        }
        private void OnDisconnected()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed && !returnLogin) { dialogs.Show(this, "Kết nối server đã ngắt. Vui lòng đăng nhập lại.", "Thông báo"); returnLogin = true; Close(); } })); }
            catch (InvalidOperationException) { }
        }
        private void SetBusy(bool value) { busy = value; btnRefresh.Enabled = btnDangXuat.Enabled = !value; }
        private void MainForm_FormClosed(object sender, FormClosedEventArgs e) { }
    }
}
