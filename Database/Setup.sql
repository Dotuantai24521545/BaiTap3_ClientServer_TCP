-- Chạy trong SSMS hoặc sqlcmd bằng tài khoản có quyền tạo CSDL/bảng.
-- Không xóa bảng Users hoặc dữ liệu cũ của Bài 2.2.
IF DB_ID(N'QuanLyNguoiDung') IS NULL CREATE DATABASE QuanLyNguoiDung;
GO
USE QuanLyNguoiDung;
GO
IF OBJECT_ID(N'dbo.NguoiDung', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.NguoiDung
    (
        UserId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_NguoiDung PRIMARY KEY,
        Username NVARCHAR(20) NOT NULL CONSTRAINT UQ_NguoiDung_Username UNIQUE,
        PasswordHash NVARCHAR(64) NOT NULL,
        PasswordSalt NVARCHAR(32) NOT NULL,
        Email NVARCHAR(100) NULL,
        FullName NVARCHAR(50) NOT NULL,
        Birthday DATE NULL,
        LastLogin DATETIME2 NULL
    );
END;
GO
-- Chuyển dữ liệu theo những cột được DatabaseHelper.cs cũ sử dụng.
-- Birthday để NULL vì Bài 2.2 không có trường này. PBKDF2 giữ cùng thuật toán/tham số.
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'TenDangNhap') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'MatKhauBam') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'Salt') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'HoTen') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'Email') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'LanDangNhapCuoi') IS NOT NULL
BEGIN
    EXEC sys.sp_executesql N'
        INSERT INTO dbo.NguoiDung (Username,PasswordHash,PasswordSalt,Email,FullName,Birthday,LastLogin)
        SELECT u.TenDangNhap,u.MatKhauBam,u.Salt,u.Email,u.HoTen,NULL,u.LanDangNhapCuoi
        FROM dbo.Users AS u
        WHERE NOT EXISTS (SELECT 1 FROM dbo.NguoiDung AS n WHERE n.Username=u.TenDangNhap);';
END;
GO
SELECT COUNT(*) AS SoTaiKhoan FROM dbo.NguoiDung;
