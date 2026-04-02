using System;
using Microsoft.EntityFrameworkCore;
using Production.Core.Entities;

namespace Production.Infrastructure.Data
{
    /// <summary>
    /// 应用数据库上下文，负责 EF Core 实体映射与模型配置。
    /// </summary>
    /// <example>
    /// 配置示例：
    /// services.AddDbContext&lt;AppDbContext&gt;(options =>
    ///     options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
    /// </example>
    public class AppDbContext : DbContext
    {
        private const int IdentifierMaxLength = 50;
        private const int StatusMaxLength = 20;

        /// <summary>
        /// 初始化 <see cref="AppDbContext"/>。
        /// </summary>
        /// <param name="options">DbContext 配置选项。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 为 null 时抛出。</exception>
        /// <example>
        /// var dbContext = new AppDbContext(options);
        /// </example>
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
        }

        /// <summary>
        /// 生产记录主表（ProductionRecords）。
        /// </summary>
        public DbSet<ProductionRecord> ProductionRecords => Set<ProductionRecord>();

        /// <summary>
        /// 归档日志表（ArchiveLog）。
        /// </summary>
        public DbSet<ArchiveLog> ArchiveLogs => Set<ArchiveLog>();

        /// <summary>
        /// 配置实体模型。
        /// </summary>
        /// <param name="modelBuilder">模型构建器。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="modelBuilder"/> 为 null 时抛出。</exception>
        /// <example>
        /// protected override void OnModelCreating(ModelBuilder modelBuilder)
        /// {
        ///     base.OnModelCreating(modelBuilder);
        /// }
        /// </example>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder is null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            base.OnModelCreating(modelBuilder);

            ConfigureProductionRecordEntity(modelBuilder);
            ConfigureArchiveLogEntity(modelBuilder);
        }

        /// <summary>
        /// 配置 <see cref="ProductionRecord"/> 的数据库映射。
        /// </summary>
        /// <param name="modelBuilder">模型构建器。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="modelBuilder"/> 为 null 时抛出。</exception>
        /// <example>
        /// // 生成后的表：ProductionRecords
        /// // 常用索引：Timestamp、MachineId+StationId+Timestamp
        /// </example>
        private static void ConfigureProductionRecordEntity(ModelBuilder modelBuilder)
        {
            if (modelBuilder is null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            var productionRecordBuilder = modelBuilder.Entity<ProductionRecord>();

            productionRecordBuilder.ToTable("ProductionRecords");

            productionRecordBuilder.HasKey(record => record.Id);

            productionRecordBuilder.Property(record => record.Id)
                .ValueGeneratedOnAdd();

            productionRecordBuilder.Property(record => record.MachineId)
                .IsRequired()
                .HasMaxLength(IdentifierMaxLength);

            productionRecordBuilder.Property(record => record.StationId)
                .IsRequired()
                .HasMaxLength(IdentifierMaxLength);

            productionRecordBuilder.Property(record => record.BobbinWeight)
                .IsRequired()
                .HasPrecision(18, 3);

            productionRecordBuilder.Property(record => record.Timestamp)
                .IsRequired();

            productionRecordBuilder.HasIndex(record => record.Timestamp)
                .HasDatabaseName("IX_ProductionRecords_Timestamp");

            productionRecordBuilder.HasIndex(record => new { record.MachineId, record.StationId, record.Timestamp })
                .HasDatabaseName("IX_ProductionRecords_Machine_Station_Timestamp");
        }

        /// <summary>
        /// 配置 <see cref="ArchiveLog"/> 的数据库映射。
        /// </summary>
        /// <param name="modelBuilder">模型构建器。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="modelBuilder"/> 为 null 时抛出。</exception>
        /// <example>
        /// // 生成后的表：ArchiveLog
        /// // 可用于记录 SQL Agent 归档任务执行结果
        /// </example>
        private static void ConfigureArchiveLogEntity(ModelBuilder modelBuilder)
        {
            if (modelBuilder is null)
            {
                throw new ArgumentNullException(nameof(modelBuilder));
            }

            var archiveLogBuilder = modelBuilder.Entity<ArchiveLog>();

            archiveLogBuilder.ToTable("ArchiveLog");

            archiveLogBuilder.HasKey(log => log.Id);

            archiveLogBuilder.Property(log => log.Id)
                .ValueGeneratedOnAdd();

            archiveLogBuilder.Property(log => log.ArchiveDate)
                .IsRequired();

            archiveLogBuilder.Property(log => log.RecordsArchived)
                .IsRequired();

            archiveLogBuilder.Property(log => log.Status)
                .IsRequired()
                .HasMaxLength(StatusMaxLength);

            archiveLogBuilder.Property(log => log.ErrorMessage)
                .HasColumnType("nvarchar(max)");

            archiveLogBuilder.HasIndex(log => log.ArchiveDate)
                .HasDatabaseName("IX_ArchiveLog_ArchiveDate");
        }
    }
}
