using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;
using UserManagement.Shared;

namespace QuanLiNguoiDung
{
    public partial class LoginForm : Form
    {
        public TcpUserClient Connection { get; } = new TcpUserClient();
        private readonly TextBox txtServerIp = new TextBox { Name = "txtServerIp", Text = "127.0.0.1", Width = 175 };
        private readonly NumericUpDown numServerPort = new NumericUpDown { Name = "numServerPort", Minimum = 1024, Maximum = 49151, Value = 9003, Width = 100 };
        private readonly Label lblConnection = new Label { Name = "lblConnection", Text = "Chưa kết nối", AutoSize = true, ForeColor = Color.DimGray };
        private bool busy;
        private readonly IUserDialogs dialogs;
        public LoginForm() : this(null) { }
        public LoginForm(IUserDialogs dialogs)
        {
            this.dialogs = dialogs ?? new MessageBoxDialogs();
            InitializeComponent(); Text = "Bài 3 - Đăng nhập";
            if (!Controls.Contains(pnlHeader)) Controls.Add(pnlHeader);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(765, 585);
            var settings = new FlowLayoutPanel { Name = "pnlConnection", Location = new Point(25, 475), Size = new Size(710, 90), Padding = new Padding(8) };
            settings.Controls.Add(new Label { Text = "IP server", AutoSize = true }); settings.Controls.Add(txtServerIp);
            settings.Controls.Add(new Label { Text = "Cổng", AutoSize = true }); settings.Controls.Add(numServerPort); settings.Controls.Add(lblConnection);
            Controls.Add(settings);
            lblLoiChung.Location = new Point(25, 290); lblLoiChung.Size = new Size(700, 40); lblLoiChung.Font = new Font("Segoe UI", 10);
            lblLoiMatKhau.Height = 28;
            txtTenDangNhap.MaxLength = 20; txtMatKhau.MaxLength = 128;
            Connection.Disconnected += () => {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)(() => { if (!IsDisposed) lblConnection.Text = "Đã ngắt kết nối"; })); } catch (InvalidOperationException) { }
            };
            FormClosing += (s, e) => Connection.Dispose();
        }
        private async void btnDangNhap_Click(object sender, EventArgs e)
        {
            if (busy) return;
            lblLoiTen.Text = lblLoiMatKhau.Text = lblLoiChung.Text = "";
            if (!Validation.Username(txtTenDangNhap.Text.Trim())) { lblLoiTen.Text = "Tên gồm 3–20 chữ, số hoặc dấu gạch dưới."; return; }
            if (!Validation.Password(txtMatKhau.Text)) { lblLoiMatKhau.Text = "Mật khẩu cần từ 8 đến 128 ký tự."; return; }
            IPAddress address;
            if (!IPAddress.TryParse(txtServerIp.Text.Trim(), out address)) { lblLoiChung.Text = "Địa chỉ IP server không hợp lệ."; return; }
            SetBusy(true); lblConnection.Text = "Đang kết nối...";
            try
            {
                await Connection.ConnectAsync(address.ToString(), (int)numServerPort.Value);
                if (IsDisposed) return;
                lblConnection.Text = "Đang chờ server...";
                var response = await Connection.SendAsync(new Request { Command = Commands.Login, Username = txtTenDangNhap.Text.Trim(), Password = txtMatKhau.Text });
                if (IsDisposed) return;
                if (response.Code != Codes.Ok) { lblLoiChung.Text = Codes.Message(response.Code); Connection.Disconnect(); }
                else { lblConnection.Text = "Đã kết nối"; new MainForm(response.User, this, dialogs).Show(); Hide(); }
            }
            catch (SocketException) { if (!IsDisposed) lblLoiChung.Text = "Không kết nối được server. Kiểm tra IP và cổng."; }
            catch (IOException) { if (!IsDisposed) lblLoiChung.Text = "Kết nối bị ngắt hoặc chờ quá lâu. Vui lòng thử lại."; }
            finally { if (!IsDisposed) { txtMatKhau.Clear(); SetBusy(false); } }
        }
        private void SetBusy(bool value)
        {
            busy = value; btnDangNhap.Enabled = lnkDangKy.Enabled = txtServerIp.Enabled = numServerPort.Enabled = !value;
            btnDangNhap.Text = value ? "Đang xử lý..." : "Đăng nhập";
        }
        private void btnThoat_Click(object sender, EventArgs e) { Close(); }
        private void lnkDangKy_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            using (var register = new RegisterForm(txtServerIp.Text.Trim(), (int)numServerPort.Value, dialogs)) register.ShowDialog(this);
        }
        public void ReturnToLogin() { Connection.Disconnect(); if (!IsDisposed) { Show(); Activate(); txtMatKhau.Clear(); } }
        private void lblTen_Click(object sender, EventArgs e) { }
        private void lblMK_Click(object sender, EventArgs e) { }
        private void lblLoiChung_Click(object sender, EventArgs e) { }
        private void LoginForm_Load(object sender, EventArgs e) { }
    }
}
