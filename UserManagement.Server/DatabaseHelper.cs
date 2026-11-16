using System;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using UserManagement.Shared;

namespace UserManagement.Server
{
    public sealed class StoredUser
    {
        public UserInfo Info { get; set; }
        public string PasswordHash { get; set; }
        public string PasswordSalt { get; set; }
    }
    public interface IUserRepository
    {
        Task<StoredUser> FindAsync(string username, CancellationToken token);
        Task<bool> CreateAsync(Request request, string hash, string salt, CancellationToken token);
        Task UpdateLastLoginAsync(int userId, CancellationToken token);
    }
    // Kế thừa cách tổ chức DAL ở Bài 2.2. Chỉ server tham chiếu System.Data.SqlClient.
    public sealed class DatabaseHelper : IUserRepository
    {
        private readonly string connectionString;
        public DatabaseHelper(string connectionString) { this.connectionString = connectionString; }
        private SqlCommand Command(string sql, SqlConnection connection) { return new SqlCommand(sql, connection) { CommandTimeout = 5 }; }
        public async Task ProbeAsync(CancellationToken token)
        {
            using (var connection = new SqlConnection(connectionString))
            using (var command = Command("SELECT TOP (0) UserId,Username,PasswordHash,PasswordSalt,Email,FullName,Birthday,LastLogin FROM dbo.NguoiDung", connection))
            { await connection.OpenAsync(token); using (await command.ExecuteReaderAsync(token)) { } }
        }
        public async Task<StoredUser> FindAsync(string username, CancellationToken token)
        {
            const string sql = "SELECT UserId,Username,PasswordHash,PasswordSalt,Email,FullName,Birthday,LastLogin FROM dbo.NguoiDung WHERE Username=@username";
            using (var connection = new SqlConnection(connectionString))
            using (var command = Command(sql, connection))
            {
                command.Parameters.Add("@username", SqlDbType.NVarChar, 20).Value = username;
                await connection.OpenAsync(token);
                using (var reader = await command.ExecuteReaderAsync(token))
                {
                    if (!await reader.ReadAsync(token)) return null;
                    return new StoredUser {
                        PasswordHash = reader.GetString(2), PasswordSalt = reader.GetString(3),
                        Info = new UserInfo { UserId = reader.GetInt32(0), Username = reader.GetString(1),
                            Email = reader.IsDBNull(4) ? "" : reader.GetString(4), FullName = reader.GetString(5),
                            Birthday = reader.IsDBNull(6) ? null : reader.GetDateTime(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                            LastLogin = reader.IsDBNull(7) ? null : reader.GetDateTime(7).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) }
                    };
                }
            }
        }
        public async Task<bool> CreateAsync(Request request, string hash, string salt, CancellationToken token)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(token);
                using (var check = Command("SELECT COUNT(1) FROM dbo.NguoiDung WHERE Username=@username", connection))
                {
                    check.Parameters.Add("@username", SqlDbType.NVarChar, 20).Value = request.Username;
                    if ((int)await check.ExecuteScalarAsync(token) > 0) return false;
                }
                using (var command = Command("INSERT INTO dbo.NguoiDung(Username,PasswordHash,PasswordSalt,Email,FullName,Birthday) VALUES(@username,@hash,@salt,@email,@fullName,@birthday)", connection))
                {
                    command.Parameters.Add("@username", SqlDbType.NVarChar, 20).Value = request.Username;
                    command.Parameters.Add("@hash", SqlDbType.NVarChar, 64).Value = hash;
                    command.Parameters.Add("@salt", SqlDbType.NVarChar, 32).Value = salt;
                    command.Parameters.Add("@email", SqlDbType.NVarChar, 100).Value = request.Email;
                    command.Parameters.Add("@fullName", SqlDbType.NVarChar, 50).Value = request.FullName;
                    command.Parameters.Add("@birthday", SqlDbType.Date).Value = DateTime.ParseExact(request.Birthday, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    try { await command.ExecuteNonQueryAsync(token); return true; }
                    catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627) { return false; }
                    // UNIQUE là lớp bảo vệ cuối khi nhiều client cùng đăng ký một username.
                }
            }
        }
        public async Task UpdateLastLoginAsync(int userId, CancellationToken token)
        {
            using (var connection = new SqlConnection(connectionString))
            using (var command = Command("UPDATE dbo.NguoiDung SET LastLogin=SYSDATETIME() WHERE UserId=@id", connection))
            { command.Parameters.Add("@id", SqlDbType.Int).Value = userId; await connection.OpenAsync(token); await command.ExecuteNonQueryAsync(token); }
        }
    }
}
