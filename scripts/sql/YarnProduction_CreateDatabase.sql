-- name=scripts/sql/YarnProduction_CreateDatabase.sql
-- 描述：创建 YarnProduction 数据库、分区函数/方案、主表、归档表、归档日志表，
--       并创建两个供 SQL Agent 调度的存储过程：
--         dbo.sp_CreateNextMonthPartition  -- 每月一次，确保下月分区存在
--         dbo.sp_ArchiveOldData           -- 每日一次，归档超过指定月份的数据（默认 12 个月）
--
-- 注意：
--  - 在生产环境下，请为 CREATE DATABASE 指定合适的文件路径和大小。
--  - 请在运行前修改示例用户密码与权限。
--  - SQL Agent 作业创建块为示例，可能需要在目标服务器以 sysadmin 权限运行或通过管理界面创建。
--  - 若希望使用分区切换方式做归档，请改用 ALTER PARTITION SWITCH 的实现（效率更高），本脚本采用 INSERT+DELETE 的通用实现并支持分批删除以控制事务日志。

/******************************************************************/
-- 1. 创建数据库（如不存在）
/******************************************************************/
IF DB_ID(N'YarnProduction') IS NULL
BEGIN
    PRINT 'Creating database YarnProduction...';

    CREATE DATABASE YarnProduction
    -- 示例文件路径与初始大小，请根据实际环境调整
    ON PRIMARY
    (
        NAME = N'YarnProduction_Data',
        FILENAME = N'E:\WorkProject\国际复合\03_开发\YarnProduction\scripts\sql\YarnProduction_Data.mdf',
        SIZE = 512MB,
        MAXSIZE = UNLIMITED,
        FILEGROWTH = 64MB
    )
    LOG ON
    (
        NAME = N'YarnProduction_Log',
        FILENAME = N'E:\WorkProject\国际复合\03_开发\YarnProduction\scripts\sql\YarnProduction_Log.ldf',
        SIZE = 128MB,
        MAXSIZE = 2048GB,
        FILEGROWTH = 64MB
    );

    -- 建议：生产环境可将恢复模式设为 FULL，并配置定期备份
    ALTER DATABASE YarnProduction SET RECOVERY FULL;
END
ELSE
BEGIN
    PRINT 'Database YarnProduction already exists.';
END

GO

USE YarnProduction;
GO

/******************************************************************/
-- 2. 创建用户/登录 示例（可选：请根据安全策略调整）
-- 创建 SQL 登录 appuser 并为数据库创建对应用户，授予最小必要权限
/******************************************************************/
-- 注意：仅在需要并且数据库服务器允许创建 SQL 登录的情况下执行
-- 请务必修改密码 'ChangeThisPassword!' 为安全密码
IF NOT EXISTS(SELECT 1 FROM sys.server_principals WHERE name = N'appuser')
BEGIN
    PRINT 'Creating server login appuser (change password in production)...';
    CREATE LOGIN appuser WITH PASSWORD = N'123456';
END

IF NOT EXISTS(SELECT 1 FROM sys.database_principals WHERE name = N'appuser')
BEGIN
    PRINT 'Creating database user appuser and granting datareader/datawriter...';
    CREATE USER appuser FOR LOGIN appuser;
	ALTER ROLE db_datareader ADD MEMBER appuser;
	ALTER ROLE db_datawriter ADD MEMBER appuser;
    -- 给执行存储过程的权限（如果需要）
    -- GRANT EXECUTE TO appuser;
END

GO

/******************************************************************/
-- 3. 创建分区函数与分区方案（按月分区）
--    这里使用动态 SQL 生成从 当前月 前 12 个月 到 当前月 后 12 个月 的边界，
--    以便初始建表时有合理的分区集合。后续按月扩展由 sp_CreateNextMonthPartition 完成。
/******************************************************************/
IF NOT EXISTS (SELECT 1 FROM sys.partition_functions WHERE name = N'PF_Production_ByMonth')
BEGIN
    PRINT 'Creating partition function PF_Production_ByMonth and scheme PS_Production_ByMonth...';

    DECLARE @StartMonth date = DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);
    DECLARE @i INT = -12; -- 往前 12 个月
    DECLARE @values NVARCHAR(MAX) = N'';
    DECLARE @d date;

    WHILE @i <= 12
    BEGIN
        SET @d = DATEADD(month, @i, @StartMonth);
        IF LEN(@values) > 0
            SET @values = @values + N', ';
        SET @values = @values + N'''' + CONVERT(NVARCHAR(10), @d, 120) + N''''; -- 'YYYY-MM-DD'
        SET @i = @i + 1;
    END

    DECLARE @sql NVARCHAR(MAX) = N'CREATE PARTITION FUNCTION PF_Production_ByMonth (datetime2(0)) AS RANGE RIGHT FOR VALUES (' + @values + N');';
    EXEC sp_executesql @sql;

    -- 创建分区方案（全部映射到 PRIMARY，必要���可为每个分区创建单独文件组）
    CREATE PARTITION SCHEME PS_Production_ByMonth AS PARTITION PF_Production_ByMonth ALL TO ([PRIMARY]);
END
ELSE
BEGIN
    PRINT 'Partition function PF_Production_ByMonth already exists.';
END

GO

/******************************************************************/
-- 4. 创建主表 ProductionRecords（使用分区方案）
--    表结构参考 SRS.md：Id, MachineId, StationId, BobbinWeight, Timestamp
--    采用 Id bigint IDENTITY，聚簇索引建议包含分区列 Timestamp（以便分区裁剪）
/******************************************************************/
IF OBJECT_ID(N'dbo.ProductionRecords', N'U') IS NULL
BEGIN
    PRINT 'Creating table ProductionRecords...';

    CREATE TABLE dbo.ProductionRecords
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        MachineId NVARCHAR(50) NOT NULL,
        StationId NVARCHAR(50) NOT NULL,
        BobbinWeight DECIMAL(18,3) NOT NULL,
        Timestamp DATETIME2(0) NOT NULL,
        CONSTRAINT PK_ProductionRecords PRIMARY KEY NONCLUSTERED (Id) -- 临时使用非聚簇主键
    );

    -- 建立聚簇索引以支持按 Timestamp 分区（聚簇索引必须包含分区列）
    -- 使用分区方案 PS_Production_ByMonth
    CREATE CLUSTERED INDEX CIX_ProductionRecords_Timestamp_Id
    ON dbo.ProductionRecords (Timestamp, Id)
    ON PS_Production_ByMonth (Timestamp);

    -- 建议的辅助索引：便于按机台/台位/时间查询
    CREATE NONCLUSTERED INDEX IX_Production_Machine_Station_Timestamp
    ON dbo.ProductionRecords (MachineId, StationId, Timestamp)
    INCLUDE (BobbinWeight, Id);
END
ELSE
BEGIN
    PRINT 'Table ProductionRecords already exists.';
END

GO

/******************************************************************/
-- 5. 创建归档表 ProductionRecords_Archive（结构相同但不分区）
/******************************************************************/
IF OBJECT_ID(N'dbo.ProductionRecords_Archive', N'U') IS NULL
BEGIN
    PRINT 'Creating table ProductionRecords_Archive...';

    CREATE TABLE dbo.ProductionRecords_Archive
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        MachineId NVARCHAR(50) NOT NULL,
        StationId NVARCHAR(50) NOT NULL,
        BobbinWeight DECIMAL(18,3) NOT NULL,
        Timestamp DATETIME2(0) NOT NULL
    );

    CREATE NONCLUSTERED INDEX IX_Archive_Machine_Station_Timestamp
    ON dbo.ProductionRecords_Archive (MachineId, StationId, Timestamp)
    INCLUDE (BobbinWeight);
END
ELSE
BEGIN
    PRINT 'Table ProductionRecords_Archive already exists.';
END

GO

/******************************************************************/
-- 6. 创建归档日志表 ArchiveLog
/******************************************************************/
IF OBJECT_ID(N'dbo.ArchiveLog', N'U') IS NULL
BEGIN
    PRINT 'Creating table ArchiveLog...';

    CREATE TABLE dbo.ArchiveLog
    (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        ArchiveDate DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
        RecordsArchived INT NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        ErrorMessage NVARCHAR(MAX) NULL
    );
END
ELSE
BEGIN
    PRINT 'Table ArchiveLog already exists.';
END

GO

/******************************************************************/
-- 7. 存储过程：sp_CreateNextMonthPartition
--    说明：计算下一个待创建的“月分区”边界（下个月的第一天），若已存在则不做任何操作；
--          若不存在，则执行 ALTER PARTITION FUNCTION ... SPLIT RANGE (...) 创建分区。
--    注：需要对 sys.partition_functions 与 sys.partition_range_values 做权限允许访问（db_owner 或相应权限）。
/******************************************************************/
IF OBJECT_ID(N'dbo.sp_CreateNextMonthPartition', N'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_CreateNextMonthPartition;
GO

CREATE PROCEDURE dbo.sp_CreateNextMonthPartition
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @NextMonth date = DATEFROMPARTS(YEAR(DATEADD(month, 1, GETDATE())), MONTH(DATEADD(month, 1, GETDATE())), 1);
    DECLARE @exists INT;

    SELECT @exists = COUNT(1)
    FROM sys.partition_range_values prv
    INNER JOIN sys.partition_functions pf ON prv.function_id = pf.function_id
    WHERE pf.name = N'PF_Production_ByMonth' AND CONVERT(date, prv.value) = @NextMonth;

    IF @exists > 0
    BEGIN
        PRINT 'Partition boundary for ' + CONVERT(nvarchar(10), @NextMonth, 120) + ' already exists. No action.';
        RETURN;
    END

    DECLARE @sql NVARCHAR(MAX) = N'ALTER PARTITION FUNCTION PF_Production_ByMonth() SPLIT RANGE (''' + CONVERT(nvarchar(10), @NextMonth, 120) + ''');';

    BEGIN TRY
        EXEC sp_executesql @sql;
        PRINT 'Created partition boundary for ' + CONVERT(nvarchar(10), @NextMonth, 120);
    END TRY
    BEGIN CATCH
        DECLARE @err NVARCHAR(MAX) = ERROR_MESSAGE();
        RAISERROR('Failed to create partition boundary: %s', 16, 1, @err);
    END CATCH
END
GO

/******************************************************************/
-- 8. 存储过程：sp_ArchiveOldData
--    说明：将主表中早于指定月份（默认为 12 个月）的数据移动到归档表，并记录归档日志。
--    为避免一次性大事务对日志与性能造成冲击，采用批量迁移+批量删除（分页）的方式。
--    参数：
--      @Months INT DEFAULT 12  -- 表示归档阈值：Timestamp < start_of_current_month - @Months
--      @BatchSize INT DEFAULT 10000 -- 每次迁移/删除的条数
/******************************************************************/
IF OBJECT_ID(N'dbo.sp_ArchiveOldData', N'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ArchiveOldData;
GO

CREATE PROCEDURE dbo.sp_ArchiveOldData
    @Months INT = 12,
    @BatchSize INT = 10000
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @CutoffDate DATETIME2(0) = DATEADD(month, -@Months, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1));
    DECLARE @TotalArchived BIGINT = 0;
    DECLARE @Rows INT = 0;
    DECLARE @StartTime DATETIME2(0) = SYSUTCDATETIME();

    BEGIN TRY
        -- 使用临时表记录待迁移 Ids 分页（避免在同一事务中大范围锁表）
        WHILE 1=1
        BEGIN
            BEGIN TRANSACTION;

            -- 插入一批老数据到归档表
            INSERT INTO dbo.ProductionRecords_Archive (MachineId, StationId, BobbinWeight, Timestamp)
            SELECT TOP (@BatchSize) MachineId, StationId, BobbinWeight, Timestamp
            FROM dbo.ProductionRecords WITH (ROWLOCK, READPAST)
            WHERE Timestamp < @CutoffDate
            ORDER BY Timestamp, Id;

            SET @Rows = @@ROWCOUNT;

            IF @Rows = 0
            BEGIN
                COMMIT TRANSACTION;
                BREAK; -- 退出循环
            END

            -- 删除已迁移的数据（按同样条件）
            ;WITH CTE_ToDelete AS
            (
                SELECT TOP (@BatchSize) Id
                FROM dbo.ProductionRecords WITH (ROWLOCK, READPAST)
                WHERE Timestamp < @CutoffDate
                ORDER BY Timestamp, Id
            )
            DELETE R
            FROM dbo.ProductionRecords R
            INNER JOIN CTE_ToDelete D ON R.Id = D.Id;

            SET @TotalArchived = @TotalArchived + @Rows;

            COMMIT TRANSACTION;
        END

        -- 插入归档日志
        INSERT INTO dbo.ArchiveLog (ArchiveDate, RecordsArchived, Status, ErrorMessage)
        VALUES (SYSUTCDATETIME(), CAST(@TotalArchived AS INT), 'Success', NULL);

        DECLARE @EndTime DATETIME2(0) = SYSUTCDATETIME();
        PRINT 'Archive completed. Total rows archived: ' + CAST(@TotalArchived AS NVARCHAR(32)) 
              + '. Duration: ' + CONVERT(NVARCHAR(32), DATEDIFF(SECOND, @StartTime, @EndTime)) + ' seconds.';
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;

        DECLARE @ErrMsg NVARCHAR(MAX) = ERROR_MESSAGE();
        PRINT 'Archive failed: ' + @ErrMsg;

        INSERT INTO dbo.ArchiveLog (ArchiveDate, RecordsArchived, Status, ErrorMessage)
        VALUES (SYSUTCDATETIME(), 0, 'Failed', @ErrMsg);

        -- 将错误抛出出去以便 SQL Agent 捕获
        THROW;
    END CATCH
END
GO

/******************************************************************/
-- 9. 示例：创建 SQL Agent 作业（示例脚本）
--    说明：下面是使用 msdb.dbo.sp_add_job 等创建作业的示例；仅在具备相应权限时运行。
--    - 每月 1 日 01:00 调用 sp_CreateNextMonthPartition
--    - 每日 02:00 调用 sp_ArchiveOldData
--    注意：建议通过 SQL Server Agent 管理界面检查/创建作业，以便验证步骤与运行帐户
/******************************************************************/
-- 以下块为示例，运行前请确保在 msdb 数据库并且有足够权限：

USE msdb;
GO

-- 9.1 作业：CreateNextMonthPartition
IF NOT EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'YarnProduction_CreateNextMonthPartition')
BEGIN
    DECLARE @job_id UNIQUEIDENTIFIER;
    EXEC msdb.dbo.sp_add_job @job_name=N'YarnProduction_CreateNextMonthPartition', @enabled=1, @description=N'Ensure next month partition exists for YarnProduction', @owner_login_name = N'sa', @job_id = @job_id OUTPUT;

    EXEC msdb.dbo.sp_add_jobstep @job_id=@job_id, @step_name=N'Create Partition', 
        @subsystem=N'TSQL', 
        @command=N'EXEC YarnProduction.dbo.sp_CreateNextMonthPartition;', 
        @database_name=N'YarnProduction';

    -- 每月 1 日 01:00 执行
    EXEC msdb.dbo.sp_add_schedule @schedule_name=N'YarnProduction_MonthlyPartitionSchedule', @enabled=1, 
        @freq_type=16, @freq_interval=1, @active_start_time=010000; -- freq_type=16 monthly, freq_interval=1 means every month
    EXEC msdb.dbo.sp_attach_schedule @job_id=@job_id, @schedule_name=N'YarnProduction_MonthlyPartitionSchedule';

    EXEC msdb.dbo.sp_add_jobserver @job_id=@job_id;
END

-- 9.2 作业：ArchiveOldData
IF NOT EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'YarnProduction_ArchiveOldData')
BEGIN
    DECLARE @job2_id UNIQUEIDENTIFIER;
    EXEC msdb.dbo.sp_add_job @job_name=N'YarnProduction_ArchiveOldData', @enabled=1, @description=N'Archive old production records older than configured months', @owner_login_name = N'sa', @job_id = @job2_id OUTPUT;

    EXEC msdb.dbo.sp_add_jobstep @job_id=@job2_id, @step_name=N'Archive Old Data', 
        @subsystem=N'TSQL', 
        @command=N'EXEC YarnProduction.dbo.sp_ArchiveOldData @Months = 12, @BatchSize = 10000;', 
        @database_name=N'YarnProduction';

    -- 每日 02:00 执行
    EXEC msdb.dbo.sp_add_schedule @schedule_name=N'YarnProduction_DailyArchiveSchedule', @enabled=1, 
        @freq_type=4, @freq_interval=1, @active_start_time=020000; -- freq_type=4 daily
    EXEC msdb.dbo.sp_attach_schedule @job_id=@job2_id, @schedule_name=N'YarnProduction_DailyArchiveSchedule';

    EXEC msdb.dbo.sp_add_jobserver @job_id=@job2_id;
END
GO


-- 脚本结束