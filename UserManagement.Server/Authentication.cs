using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UserManagement.Shared;

namespace UserManagement.Server
{
    public static class PasswordHasher
    {
        // Giữ nguyên tham số của MatKhau.cs Bài 2.2 để kiểm tra được tài khoản chuyển sang.
        public const int Iterations = 600000;
        public static string Salt()
        {
            byte[] salt = new byte[16]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            return Convert.ToBase64String(salt);
        }
        public static string Hash(string password, string salt)
        {
            using (var derive = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), Iterations, HashAlgorithmName.SHA256))
                return Convert.ToBase64String(derive.GetBytes(32));
        }
        public static bool Equal(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0; for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i]; return difference == 0;
        }
    }
    public sealed class ClientSession
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Username { get; set; }
        public string Token { get; set; }
        public DateTime ExpiresUtc { get; set; }
        public void Clear() { Username = null; Token = null; ExpiresUtc = DateTime.MinValue; }
    }
    public sealed class RequestProcessor
    {
        private readonly IUserRepository repository;
        private readonly Func<DateTime> clock;
        private readonly TimeSpan tokenLifetime;
        private readonly SemaphoreSlim hashSlots = new SemaphoreSlim(2, 2);
        private readonly string dummySalt = Convert.ToBase64String(new byte[16]);
        public RequestProcessor(IUserRepository repository, TimeSpan? tokenLifetime = null, Func<DateTime> clock = null)
        { this.repository = repository; this.tokenLifetime = tokenLifetime ?? TimeSpan.FromMinutes(30); this.clock = clock ?? (() => DateTime.UtcNow); }
        private async Task<string> DeriveAsync(string password, string salt, CancellationToken token)
        {
            await hashSlots.WaitAsync(token).ConfigureAwait(false);
            try { return await Task.Run(() => PasswordHasher.Hash(password, salt), token).ConfigureAwait(false); }
            finally { hashSlots.Release(); }
        }
        public async Task<Response> HandleAsync(ClientSession session, Request request, CancellationToken token)
        {
            Guid requestId;
            if (request == null || request.Version != 1 || !Guid.TryParseExact(request.RequestId, "N", out requestId)) return Response.For(request, Codes.Invalid);
            switch (request.Command)
            {
                case Commands.Echo:
                    if (request.Echo != null && request.Echo.Length > 4096) return Response.For(request, Codes.Invalid);
                    var echo = Response.For(request, Codes.Ok); echo.Echo = request.Echo; return echo;
                case Commands.Register:
                    if (!Validation.Register(request)) return Response.For(request, Codes.Invalid);
                    string salt = PasswordHasher.Salt(); string hash = await DeriveAsync(request.Password, salt, token);
                    bool created = await repository.CreateAsync(request, hash, salt, token);
                    return Response.For(request, created ? Codes.Ok : Codes.Exists);
                case Commands.Login:
                    if (session.Username != null) return Response.For(request, Codes.Already);
                    if (!Validation.Username(request.Username) || !Validation.Password(request.Password)) return Response.For(request, Codes.AuthFailed);
                    var user = await repository.FindAsync(request.Username, token);
                    string candidate = await DeriveAsync(request.Password, user == null ? dummySalt : user.PasswordSalt, token);
                    if (user == null || !PasswordHasher.Equal(candidate, user.PasswordHash)) return Response.For(request, Codes.AuthFailed);
                    await repository.UpdateLastLoginAsync(user.Info.UserId, token);
                    byte[] tokenBytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(tokenBytes);
                    session.Username = user.Info.Username; session.Token = Convert.ToBase64String(tokenBytes); session.ExpiresUtc = clock().Add(tokenLifetime);
                    var login = Response.For(request, Codes.Ok); login.User = user.Info; login.Token = session.Token;
                    login.ExpiresUtc = session.ExpiresUtc.ToString("o", CultureInfo.InvariantCulture); return login;
                case Commands.Profile:
                case Commands.Logout:
                    string error = Authorize(session, request.Token);
                    if (error != null) return Response.For(request, error);
                    if (request.Command == Commands.Logout) { session.Clear(); return Response.For(request, Codes.Ok); }
                    var profile = await repository.FindAsync(session.Username, token);
                    if (profile == null) { session.Clear(); return Response.For(request, Codes.Unauthorized); }
                    var response = Response.For(request, Codes.Ok); response.User = profile.Info; return response;
                default: return Response.For(request, Codes.Unknown);
            }
        }
        private string Authorize(ClientSession session, string token)
        {
            if (session.Username == null || !PasswordHasher.Equal(session.Token, token)) return Codes.Unauthorized;
            if (clock() >= session.ExpiresUtc) { session.Clear(); return Codes.Expired; }
            return null;
        }
    }
}
