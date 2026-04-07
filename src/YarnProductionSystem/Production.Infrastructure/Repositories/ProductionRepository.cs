using Production.Core.DTOs;
using Microsoft.EntityFrameworkCore;
using Production.Core.Entities;
using Production.Core.Interfaces;
using Production.Infrastructure.Data;

namespace Production.Infrastructure.Repositories
{
    /// <summary>
    /// 基于 EF Core 的生产记录仓储实现。
    /// </summary>
    /// <example>
    /// var repository = new ProductionRepository(dbContext);
    /// await repository.BulkInsertAsync(records, cancellationToken);
    /// </example>
    public class ProductionRepository : IProductionRepository
    {
        private readonly AppDbContext _dbContext;
        private bool _disposed;

        /// <summary>
        /// 初始化 <see cref="ProductionRepository"/>。
        /// </summary>
        /// <param name="dbContext">应用数据库上下文。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="dbContext"/> 为空时抛出。</exception>
        /// <example>
        /// var repository = new ProductionRepository(dbContext);
        /// </example>
        public ProductionRepository(AppDbContext dbContext)
        {
            if (dbContext is null)
            {
                throw new ArgumentNullException(nameof(dbContext));
            }

            _dbContext = dbContext;
        }

        /// <summary>
        /// 批量插入生产记录。
        /// </summary>
        /// <param name="records">待插入记录集合。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="records"/> 为空时抛出。</exception>
        /// <exception cref="ArgumentException">当记录内容校验失败时抛出。</exception>
        /// <example>
        /// await repository.BulkInsertAsync(records, cancellationToken);
        /// </example>
        public async Task BulkInsertAsync(IEnumerable<ProductionRecord> records, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (records is null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var productionRecords = records as IList<ProductionRecord> ?? records.ToList();
            if (productionRecords.Count == 0)
            {
                return;
            }

            foreach (var productionRecord in productionRecords)
            {
                if (productionRecord is null)
                {
                    throw new ArgumentException("records 中包含空记录。", nameof(records));
                }

                if (productionRecord.TryValidate(out var validateError))
                {
                    continue;
                }

                throw new ArgumentException($"生产记录校验失败：{validateError}", nameof(records));
            }

            await _dbContext.ProductionRecords.AddRangeAsync(productionRecords, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// 分页查询生产记录。
        /// </summary>
        /// <param name="start">开始时间（含）。</param>
        /// <param name="end">结束时间（含）。</param>
        /// <param name="machineId">机台编号（可选）。</param>
        /// <param name="stationId">台位编号（可选）。</param>
        /// <param name="pageIndex">页码，从 1 开始。</param>
        /// <param name="pageSize">每页数量，建议 1~500。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>分页结果。</returns>
        /// <exception cref="ArgumentException">参数不合法时抛出。</exception>
        /// <example>
        /// var page = await repository.QueryAsync(start, end, "1", "2", 1, 100, cancellationToken);
        /// </example>
        public async Task<PagedResult<ProductionRecordDto>> QueryAsync(
            DateTime start,
            DateTime end,
            string? machineId,
            string? stationId,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            ValidateQueryArguments(start, end, pageIndex, pageSize);
            cancellationToken.ThrowIfCancellationRequested();

            var filteredQuery = BuildFilteredQuery(start, end, machineId, stationId).AsNoTracking();
            var totalCount = await filteredQuery.LongCountAsync(cancellationToken);

            if (totalCount == 0)
            {
                return PagedResult<ProductionRecordDto>.Create(Array.Empty<ProductionRecordDto>(), pageIndex, pageSize, 0);
            }

            var skipCount = (pageIndex - 1) * pageSize;
            var pagedItems = await filteredQuery
                .OrderByDescending(record => record.Timestamp)
                .Skip(skipCount)
                .Take(pageSize)
                .Select(record => new ProductionRecordDto
                {
                    Id = record.Id,
                    MachineId = record.MachineId,
                    StationId = record.StationId,
                    BobbinWeight = record.BobbinWeight,
                    Timestamp = record.Timestamp
                })
                .ToListAsync(cancellationToken);

            return PagedResult<ProductionRecordDto>.Create(pagedItems, pageIndex, pageSize, totalCount);
        }

        /// <summary>
        /// 查询导出所需的全部生产记录。
        /// </summary>
        /// <param name="start">开始时间（含）。</param>
        /// <param name="end">结束时间（含）。</param>
        /// <param name="machineId">机台编号（可选）。</param>
        /// <param name="stationId">台位编号（可选）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>用于导出的实体列表。</returns>
        /// <exception cref="ArgumentException">时间参数不合法时抛出。</exception>
        /// <example>
        /// var exportRows = await repository.QueryForExportAsync(start, end, null, null, cancellationToken);
        /// </example>
        public async Task<IEnumerable<ProductionRecord>> QueryForExportAsync(
            DateTime start,
            DateTime end,
            string? machineId,
            string? stationId,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            ValidateTimeRange(start, end);
            cancellationToken.ThrowIfCancellationRequested();

            var exportQuery = BuildFilteredQuery(start, end, machineId, stationId).AsNoTracking();
            return await exportQuery
                .OrderBy(record => record.Timestamp)
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// 确保下月分区存在。
        /// 当前版本由 SQL Agent 作业托管分区维护，此方法保留为空操作。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>已完成任务。</returns>
        /// <example>
        /// await repository.EnsureNextMonthPartitionAsync(cancellationToken);
        /// </example>
        public Task EnsureNextMonthPartitionAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }

        /// <summary>
        /// 释放仓储占用资源。
        /// </summary>
        /// <example>
        /// repository.Dispose();
        /// </example>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _dbContext.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 构建统一筛选查询。
        /// </summary>
        /// <param name="start">开始时间（含）。</param>
        /// <param name="end">结束时间（含）。</param>
        /// <param name="machineId">机台编号（可选）。</param>
        /// <param name="stationId">台位编号（可选）。</param>
        /// <returns>筛选后的查询对象。</returns>
        /// <example>
        /// var query = BuildFilteredQuery(start, end, "1", "2");
        /// </example>
        private IQueryable<ProductionRecord> BuildFilteredQuery(DateTime start, DateTime end, string? machineId, string? stationId)
        {
            ValidateTimeRange(start, end);

            var productionQuery = _dbContext.ProductionRecords
                .Where(record => record.Timestamp >= start && record.Timestamp <= end);

            if (string.IsNullOrWhiteSpace(machineId))
            {
                if (string.IsNullOrWhiteSpace(stationId))
                {
                    return productionQuery;
                }

                return productionQuery.Where(record => record.StationId == stationId);
            }

            if (string.IsNullOrWhiteSpace(stationId))
            {
                return productionQuery.Where(record => record.MachineId == machineId);
            }

            return productionQuery.Where(record => record.MachineId == machineId && record.StationId == stationId);
        }

        /// <summary>
        /// 校验分页查询参数。
        /// </summary>
        /// <param name="start">开始时间。</param>
        /// <param name="end">结束时间。</param>
        /// <param name="pageIndex">页码。</param>
        /// <param name="pageSize">分页大小。</param>
        /// <exception cref="ArgumentException">参数非法时抛出。</exception>
        /// <example>
        /// ValidateQueryArguments(start, end, 1, 100);
        /// </example>
        private static void ValidateQueryArguments(DateTime start, DateTime end, int pageIndex, int pageSize)
        {
            ValidateTimeRange(start, end);

            if (pageIndex < 1)
            {
                throw new ArgumentException("pageIndex 必须大于或等于 1。", nameof(pageIndex));
            }

            if (pageSize < 1 || pageSize > 500)
            {
                throw new ArgumentException("pageSize 必须在 1 到 500 之间。", nameof(pageSize));
            }
        }

        /// <summary>
        /// 校验时间范围参数。
        /// </summary>
        /// <param name="start">开始时间。</param>
        /// <param name="end">结束时间。</param>
        /// <exception cref="ArgumentException">时间范围非法时抛出。</exception>
        /// <example>
        /// ValidateTimeRange(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);
        /// </example>
        private static void ValidateTimeRange(DateTime start, DateTime end)
        {
            if (start > end)
            {
                throw new ArgumentException("start 不能大于 end。");
            }
        }

        /// <summary>
        /// 在对象已释放时抛出异常。
        /// </summary>
        /// <exception cref="ObjectDisposedException">对象已释放时抛出。</exception>
        /// <example>
        /// ThrowIfDisposed();
        /// </example>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ProductionRepository));
            }
        }
    }
}
