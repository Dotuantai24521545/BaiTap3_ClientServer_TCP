using System;
using System.Globalization;
using System.IO;
using System.Net.Mail;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UserManagement.Shared
{
    public static class Commands
    {
        public const string Register = "DANGKY", Login = "DANGNHAP", Profile = "LAYTHONGTIN", Logout = "DANGXUAT", Echo = "ECHO";
    }
    public static class Codes
    {
        public const string Ok = "OK", Invalid = "INVALID_REQUEST", Exists = "USER_EXISTS", AuthFailed = "AUTH_FAILED",
            Unauthorized = "NOT_AUTHENTICATED", Expired = "TOKEN_EXPIRED", Unknown = "UNKNOWN_COMMAND",
            Database = "DB_UNAVAILABLE", Internal = "SERVER_ERROR", Already = "ALREADY_AUTHENTICATED";
        public static string Message(string code)
        {
            switch (code)
            {
                case Ok: return "Thao tác thành công.";
                case Invalid: return "Dữ liệu hoặc thông điệp không hợp lệ.";
                case Exists: return "Tên đăng nhập đã được sử dụng.";
                case AuthFailed: return "Tên đăng nhập hoặc mật khẩu không đúng.";
                case Unauthorized: return "Vui lòng đăng nhập lại.";
                case Expired: return "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
                case Unknown: return "Lệnh chưa được hỗ trợ.";
                case Database: return "Server chưa truy cập được dữ liệu. Vui lòng thử lại sau.";
                case Already: return "Kết nối này đã đăng nhập.";
                default: return "Không thể hoàn tất yêu cầu. Vui lòng thử lại.";
            }
        }
    }
    [DataContract]
    public sealed class Request
    {
        [DataMember(Order = 0)] public int Version { get; set; } = 1;
        [DataMember(Order = 1)] public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember(Order = 2)] public string Command { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Username { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Password { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Email { get; set; }
        [DataMember(EmitDefaultValue = false)] public string FullName { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Birthday { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Token { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Echo { get; set; }
    }
    [DataContract]
    public sealed class UserInfo
    {
        [DataMember] public int UserId { get; set; }
        [DataMember] public string Username { get; set; }
        [DataMember] public string Email { get; set; }
        [DataMember] public string FullName { get; set; }
        [DataMember] public string Birthday { get; set; }
        [DataMember] public string LastLogin { get; set; }
        // Không định nghĩa PasswordHash hay PasswordSalt trong DTO gửi về client.
    }
    [DataContract]
    public sealed class Response
    {
        [DataMember(Order = 0)] public int Version { get; set; } = 1;
        [DataMember(Order = 1)] public string RequestId { get; set; }
        [DataMember(Order = 2)] public string Code { get; set; }
        [DataMember(EmitDefaultValue = false)] public UserInfo User { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Token { get; set; }
        [DataMember(EmitDefaultValue = false)] public string ExpiresUtc { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Echo { get; set; }
        public static Response For(Request request, string code) { return new Response { RequestId = request == null ? null : request.RequestId, Code = code }; }
    }
    public static class Validation
    {
        public static bool Username(string value) { return value != null && Regex.IsMatch(value, @"\A[a-zA-Z0-9_]{3,20}\z"); }
        public static bool Password(string value) { return value != null && value.Length >= 8 && value.Length <= 128; }
        public static bool Email(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || value.Contains("\r") || value.Contains("\n")) return false;
            try { return new MailAddress(value).Address == value && value.IndexOf('@') > 0; } catch (FormatException) { return false; }
        }
        public static bool Register(Request request)
        {
            DateTime birthday;
            return Username(request.Username) && Password(request.Password) && Email(request.Email) &&
                !string.IsNullOrWhiteSpace(request.FullName) && request.FullName.Length <= 50 && !request.FullName.Contains("\n") && !request.FullName.Contains("\r") &&
                DateTime.TryParseExact(request.Birthday, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out birthday) &&
                birthday >= new DateTime(1900, 1, 1) && birthday.Date <= DateTime.Today;
        }
    }
    // TCP là luồng byte: 4 byte độ dài big-endian + đúng N byte JSON UTF-8, tối đa 64 KiB.
    public static class FrameCodec
    {
        public const int MaxPayload = 65536;
        public static byte[] Serialize<T>(T value)
        {
            using (var memory = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(memory, value); return memory.ToArray(); }
        }
        public static T Deserialize<T>(byte[] bytes)
        {
            using (var memory = new MemoryStream(bytes)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(memory);
        }
        public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken token)
        {
            byte[] header = new byte[4];
            int first = await stream.ReadAsync(header, 0, 4, token).ConfigureAwait(false);
            if (first == 0) return null; // EOF chỉ hợp lệ trước frame mới.
            await ReadRemainingAsync(stream, header, first, 4, token).ConfigureAwait(false);
            uint length = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
            if (length == 0 || length > MaxPayload) throw new InvalidDataException("Invalid frame length.");
            byte[] payload = new byte[(int)length];
            await ReadRemainingAsync(stream, payload, 0, payload.Length, token).ConfigureAwait(false);
            return payload;
        }
        private static async Task ReadRemainingAsync(Stream stream, byte[] buffer, int offset, int total, CancellationToken token)
        {
            while (offset < total)
            {
                int count = await stream.ReadAsync(buffer, offset, total - offset, token).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("Truncated frame.");
                offset += count;
            }
        }
        public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
        {
            byte[] payload = Serialize(value);
            if (payload.Length == 0 || payload.Length > MaxPayload) throw new InvalidDataException("Invalid frame length.");
            int size = payload.Length;
            byte[] header = { (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size };
            await stream.WriteAsync(header, 0, header.Length, token).ConfigureAwait(false);
            await stream.WriteAsync(payload, 0, payload.Length, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
    }
}
