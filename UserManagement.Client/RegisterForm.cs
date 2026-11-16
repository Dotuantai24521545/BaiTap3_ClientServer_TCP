using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Windows.Forms;
using UserManagement.Shared;

namespace QuanLiNguoiDung
{
    public partial class RegisterForm : Form
    {
        private readonly TcpUserClient connection = new TcpUserClient();
        private readonly string host;
        private readonly int port;
        private readonly DateTimePicker dateBirthday = new DateTimePicker { Name = "dateBirthday", Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", MinDate = new DateTime(1900, 1, 1), MaxDate = DateTime.Today, Value = DateTime.Today.AddYears(-18) };
        private bool busy;
        private readonly IUserDialogs dialogs;
        public RegisterForm() : this("127.0.0.1", 9003) { }
        public RegisterForm(string host, int port, IUserDialogs dialogs = null)
        {
            this.dialogs = dialogs ?? new MessageBoxDialogs();
            this.host = host; this.port = port;
            InitializeComponent(); AutoScaleMode = AutoScaleMode.None;
            dateBirthday.Location = new Point(437, 347); dateBirthday.Size = new Size(220, 40); dateBirthday.Font = new Font("Segoe UI", 14);
            Controls.Add(dateBirthday); Controls.Add(new Label { Name = "lblBirthday", Text = "Ngày sinh", Font = new Font("Segoe UI", 14), Location = new Point(55, 354), AutoSize = true });
            lblLoiChung.Location = new Point(55, 403); lblLoiChung.Size = new Size(660, 75); lblLoiChung.Font = new Font("Segoe UI", 10);
            txtTenDangNhap.MaxLength = 20; txtHoTen.MaxLength = 50; txtEmail.MaxLength = 100; txtMatKhau.MaxLength = txtXacNhanMatKhau.MaxLength = 128;
            FormClosing += (s, e) => connection.Dispose();
        }
        private async void BtnDangKy_Click(object sender, EventArgs e)
        {
            if (busy) return;
            var request = new Request { Command = Commands.Register, Username = txtTenDangNhap.Text.Trim(), Password = txtMatKhau.Text,
                FullName = txtHoTen.Text.Trim(), Email = txtEmail.Text.Trim(), Birthday = dateBirthday.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            if (txtMatKhau.Text != txtXacNhanMatKhau.Text) { lblLoiChung.Text = "Mật khẩu xác nhận không khớp."; return; }
            if (!Validation.Register(request)) { lblLoiChung.Text = "Điền đủ các trường. Tên 3–20 chữ/số/gạch dưới, mật khẩu 8–128 ký tự, email hợp lệ và ngày sinh không ở tương lai."; return; }
            busy = true; btnDangKy.Enabled = false; lblLoiChung.Text = "Đang gửi đến server...";
            try
            {
                await connection.ConnectAsync(host, port);
                var response = await connection.SendAsync(request);
                if (IsDisposed) return;
                lblLoiChung.Text = Codes.Message(response.Code);
                if (response.Code == Codes.Ok) { dialogs.Show(this, "Đăng ký thành công. Bạn có thể đăng nhập.", "Thông báo"); Close(); }
            }
            catch (SocketException) { if (!IsDisposed) lblLoiChung.Text = "Không kết nối được server. Kiểm tra IP và cổng ở form Đăng nhập."; }
            catch (IOException) { if (!IsDisposed) lblLoiChung.Text = "Kết nối bị ngắt hoặc chờ quá lâu. Kiểm tra trước khi đăng ký lại."; }
            finally { connection.Disconnect(); if (!IsDisposed) { busy = false; btnDangKy.Enabled = true; txtMatKhau.Clear(); txtXacNhanMatKhau.Clear(); } }
        }
        private void BtnHuy_Click(object sender, EventArgs e) { Close(); }
        private void lblLoiChung_Click(object sender, EventArgs e) { }
    }
}
