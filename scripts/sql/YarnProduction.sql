-- ====================================================
-- 数据库：取纱机生产数据监控系统
-- 版本：1.0
-- 创建时间：2026-04-01
-- 说明：包含主表（分区表）、归档表、日志表、用户表，
--       以及分区扩展和归档存储过程。
-- ====================================================

-- 1. 创建数据库（如果已存在则删除并重建，请谨慎执行）
USE master;
GO

IF EXISTS (SELECT name FROM sys.databases WHERE name = N'YarnProduction')
    DROP DATABASE YarnProduction;
GO

CREATE DATABASE YarnProduction;
GO

USE YarnProduction;
GO

-- 2. 分区函数（按月分区，每月1日为边界）
CREATE PARTITION FUNCTION pf_Monthly (datetime2(0))
AS RANGE RIGHT FOR VALUES (
    '2026-01-01', '2026-02-01', '2026-03-01', '2026-04-01',
    '2026-05-01', '2026-06-01', '2026-07-01', '2026-08-01',
    '2026-09-01', '2026-10-01', '2026-11-01', '2026-12-01',
    '2027-01-01'
);
GO

-- 3. 分区方案（所有分区放在 PRIMARY 文件组）
CREATE PARTITION SCHEME ps_Monthly
AS PARTITION pf_Monthly ALL TO ([PRIMARY]);
GO

-- 4. 主表 ProductionRecords（分区表）
CREATE TABLE dbo.ProductionRecords (
    Id bigint NOT NULL IDENTITY(1,1),
    MachineId tinyint NOT NULL,
    StationId smallint NOT NULL,
    BobbinWeight decimal(18,3) NOT NULL,
    Timestamp datetime2(0) NOT NULL,
    CONSTRAINT PK_ProductionRecords PRIMARY KEY CLUSTERED (Id, Timestamp) ON ps_Monthly(Timestamp)
);
GO

-- 5. 归档表 ProductionRecords_Archive（非分区）
CREATE TABLE dbo.ProductionRecords_Archive (
    Id bigint NOT NULL,
    MachineId tinyint NOT NULL,
    StationId smallint NOT NULL,
    BobbinWeight decimal(18,3) NOT NULL,
    Timestamp datetime2(0) NOT NULL,
    CONSTRAINT PK_ProductionRecords_Archive PRIMARY KEY CLUSTERED (Id, Timestamp)
);
GO

-- 6. 归档日志表 ArchiveLog
CREATE TABLE dbo.ArchiveLog (
    Id int NOT NULL IDENTITY(1,1),
    ArchiveDate datetime2(0) NOT NULL,
    RecordsArchived int NOT NULL,
    Status nvarchar(20) NOT NULL,
    ErrorMessage nvarchar(max) NULL,
    DurationMs int NULL,
    CONSTRAINT PK_ArchiveLog PRIMARY KEY CLUSTERED (Id)
);
GO

-- 7. 用户表 Users
CREATE TABLE dbo.Users (
    Id int NOT NULL IDENTITY(1,1),
    UserName nvarchar(50) NOT NULL,
    Password nvarchar(256) NOT NULL,
    Role nvarchar(50) NOT NULL,
    CreatedAt datetime2(0) NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Users_UserName UNIQUE (UserName)
);
GO

-- 8. 创建索引（主表）
CREATE NONCLUSTERED INDEX IX_ProductionRecords_Timestamp ON dbo.ProductionRecords(Timestamp);
CREATE NONCLUSTERED INDEX IX_ProductionRecords_MachineStation ON dbo.ProductionRecords(MachineId, StationId) INCLUDE (BobbinWeight, Timestamp);
GO

-- 归档表索引
CREATE NONCLUSTERED INDEX IX_Archive_Timestamp ON dbo.ProductionRecords_Archive(Timestamp);
CREATE NONCLUSTERED INDEX IX_Archive_MachineStation ON dbo.ProductionRecords_Archive(MachineId, StationId) INCLUDE (BobbinWeight, Timestamp);
GO

-- 日志表索引
CREATE NONCLUSTERED INDEX IX_ArchiveLog_Date ON dbo.ArchiveLog(ArchiveDate);
GO

-- 用户表索引
CREATE NONCLUSTERED INDEX IX_Users_Role ON dbo.Users(Role);
GO

-- 9. 存储过程：动态添加分区边界（每月执行）
CREATE PROCEDURE dbo.CreateNextMonthPartition
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NextMonthFirstDay DATE = DATEADD(MONTH, 1, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1));
    DECLARE @Boundary DATETIME2(0) = @NextMonthFirstDay;

    IF NOT EXISTS (SELECT 1 FROM sys.partition_range_values WHERE value = @Boundary)
    BEGIN
        ALTER PARTITION SCHEME ps_Monthly NEXT USED [PRIMARY];
        ALTER PARTITION FUNCTION pf_Monthly() SPLIT RANGE (@Boundary);
    END
END
GO

-- 10. 存储过程：归档超过12个月的数据（每天执行）
CREATE PROCEDURE dbo.ArchiveOldData
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @StartTime DATETIME2 = GETDATE();
    DECLARE @CutoffDate DATE = DATEADD(MONTH, -12, @StartTime);
    DECLARE @RowCount INT = 0;
    DECLARE @Status NVARCHAR(20) = 'Success';
    DECLARE @ErrorMessage NVARCHAR(MAX) = NULL;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 将主表中超过12个月的数据插入归档表
        INSERT INTO dbo.ProductionRecords_Archive (Id, MachineId, StationId, BobbinWeight, Timestamp)
        SELECT Id, MachineId, StationId, BobbinWeight, Timestamp
        FROM dbo.ProductionRecords WITH (NOLOCK)
        WHERE Timestamp < @CutoffDate;

        SET @RowCount = @@ROWCOUNT;

        -- 从主表删除已归档的数据
        DELETE FROM dbo.ProductionRecords
        WHERE Timestamp < @CutoffDate;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;
        SET @Status = 'Failed';
        SET @ErrorMessage = ERROR_MESSAGE();
    END CATCH

    -- 记录归档日志
    INSERT INTO dbo.ArchiveLog (ArchiveDate, RecordsArchived, Status, ErrorMessage, DurationMs)
    VALUES (GETDATE(), @RowCount, @Status, @ErrorMessage, DATEDIFF(ms, @StartTime, GETDATE()));
END
GO

-- 11. 可选：插入默认管理员账户（密码哈希需在应用层生成，此处为占位）
INSERT INTO dbo.Users (UserName, Password, Role)
VALUES ('admin', '123456', 'Admin');
GO

-- ====================================================
-- 脚本结束
-- ====================================================