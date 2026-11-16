using System;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UserManagement.Server
{
    internal static class Program
    {
        private static int Main()
        {
            try { return RunAsync().GetAwaiter().GetResult(); }
            catch (SqlException) { Console.WriteLine("Không truy cập được SQL Server hoặc chưa tạo bảng. Xem README và Database/Setup.sql."); return 1; }
            catch (IOException) { Console.WriteLine("Không thể ghi log hoặc truy cập tài nguyên server."); return 1; }
            catch (ConfigurationErrorsException) { Console.WriteLine("Cấu hình server không hợp lệ."); return 1; }
        }
        private static async Task<int> RunAsync()
        {
            int port;
            if (!int.TryParse(ConfigurationManager.AppSettings["TcpPort"], out port) || port < 1024 || port > 49151)
            { Console.WriteLine("TcpPort phải từ 1024 đến 49151."); return 1; }
            var settings = ConfigurationManager.ConnectionStrings["UserDatabase"];
            if (settings == null) { Console.WriteLine("Thiếu connection string UserDatabase."); return 1; }
            var database = new DatabaseHelper(settings.ConnectionString);
            using (var stop = new CancellationTokenSource())
            {
                await database.ProbeAsync(stop.Token);
                Directory.CreateDirectory("Logs");
                using (var log = new StreamWriter(Path.Combine("Logs", "server-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log"), true) { AutoFlush = true })
                {
                    object logGate = new object();
                    var server = new TcpUserServer(new RequestProcessor(database));
                    server.Log += text => { lock (logGate) { Console.WriteLine(text); log.WriteLine(text); } };
                    Console.CancelKeyPress += (s, e) => { e.Cancel = true; stop.Cancel(); };
                    Console.WriteLine("TCP user server. Nhấn Ctrl+C để dừng và chờ dọn kết nối.");
                    await server.RunAsync(port, stop.Token);
                }
            }
            return 0;
        }
    }
}
