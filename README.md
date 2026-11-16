# NT106 - Assignment 3 - TCP user management

Repository: https://github.com/Dotuantai24521545/BaiTap3_ClientServer_TCP
Visibility: public. Submission representative and implementer: Tai.
Anyone can read or clone this public repository without an invitation. Direct write access is restricted to the owner.

## Verified core suites

82 checks passed: 31 local, 7 real TCP, 23 real SQL/TCP and 21 real UI-handler/transport-blackhole checks.
The publication script verifies all three completed result files before publishing. Raw results and images are in TaiLieu/MinhChung_TCP, MinhChung_SQL and MinhChung_UI.
UI checks assign real control values and invoke actual Click/FormClosing handlers. Modal notices are captured through an instance-scoped dialog service; the normal app uses MessageBox.
Packet loss is simulated by dropping bidirectional data while keeping sockets open. No Wi-Fi adapter is disabled. Two-machine LAN and physical Wi-Fi loss have not been tested because only one computer was available.

## Build and run

Open BaiTap3_QuanLyNguoiDung_TCP.sln in Visual Studio with .NET desktop development and the .NET Framework 4.7.2 targeting pack, or run Build.ps1. No NuGet packages are required.
Build.ps1 stages runnable EXEs in ChayThu. Apply Database/Setup.sql to SQL Server, configure UserDatabase in the server App.config, run Server first, then two Clients. Default sample SQL instance: (localdb)\MSSQLLocalDB; database: QuanLyNguoiDung. Default TCP: 127.0.0.1:9003.
Run XacMinh-TrenMay.ps1 for local/TCP checks; Chay-SQL-MinhChung.ps1 for isolated real SQL tests; Chay-UI-MatKetNoi.ps1 for UI and packet-loss simulation. Each SQL test creates a separate NT106_Bai3_Test_* database and preserves existing application data.

## Architecture and protocol

UserManagement.Client contains WinForms and TCP only. UserManagement.Server owns SQL access, password hashing and sessions. UserManagement.Shared defines DTOs, errors, validation and framing. UserManagement.Tests contains verification helpers; it does not replace production SQL.
Messages are UTF-8 JSON with a 4-byte unsigned big-endian payload length, 1..65536 bytes. Commands: DANGKY, DANGNHAP, LAYTHONGTIN, DANGXUAT and ECHO. Full protocol: TaiLieu/GiaoThuc_TCP_HaiTrang.pdf.
Password hashing: PBKDF2-HMAC-SHA256, 600000 iterations, independent 16-byte salts, 32-byte hashes. Tokens are random 32-byte values bound to a connection and expire in 30 minutes. Plain TCP is for this lab; TLS is not implemented.

The three Designer/resource files and DAL organization are based on Assignment 2.2: https://github.com/quockhang797/BaiTap2.2_QuanLyNguoiDung. The migration preserves Users and transfers missing accounts into NguoiDung. Legacy-account tests use synthetic records, not the user's prior personal database.

Source publication excludes compiled EXEs and draft reports. The final submission report is delivered separately.