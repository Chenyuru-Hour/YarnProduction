using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Production.Core.Entities;
using Production.Core.DTOs;

namespace Production.Core.Interfaces
{
    /// <summary>
    /// 生产数据持久化仓储接口，提供批量写入、分页查询与导出查询等能力。
    /// 实现应处理主表与归档表的合并查询逻辑（若归档在 DB 端）。
    /// </summary>
    public interface IProductionRepository : IDisposable
    {
        /// <summary>
        /// 批量插入记录（应支持事务与性能优化，例如批次写入或 SqlBulkCopy）
        /// </summary>
        Task BulkInsertAsync(IEnumerable<ProductionRecord> records, CancellationToken cancellationToken = default);

        /// <summary>
        /// 分页查询（支持按日期范围、机台、台位筛选），返回 DTO 分页结果
        /// </summary>
        Task<PagedResult<ProductionRecordDto>> QueryAsync(DateTime start, DateTime end, string? machineId, string? stationId, int pageIndex, int pageSize, CancellationToken cancellationToken = default);

        /// <summary>
        /// 为导出获取全部匹配的数据（注意：实现需考虑大数据量下的流式处理或分批查询）
        /// </summary>
        Task<IEnumerable<ProductionRecord>> QueryForExportAsync(DateTime start, DateTime end, string? machineId, string? stationId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 可选：确保下月分区存在（若由应用管理分区）
        /// 使用 SQL Agent 实现分区和归档,后续的实现改为由 DB Job 托管后的空操作或状态检查
        /// </summary>
        Task EnsureNextMonthPartitionAsync(CancellationToken cancellationToken = default);
    }
}