using System.Diagnostics;
using ClosedXML.Excel;
using Production.Core.DTOs;
using Production.Core.Entities;
using Production.Core.Interfaces;

namespace Production.Web.Services;

/// <summary>
/// 数据管理页服务：负责分页查询与 Excel 导出。
/// </summary>
public sealed class DataManageService
{
    private const int MaxExportRows = 100_000;

    private readonly IProductionRepository _productionRepository;
    private readonly ILogger<DataManageService> _logger;

    /// <summary>
    /// 初始化 <see cref="DataManageService"/> 实例。
    /// </summary>
    /// <param name="productionRepository">生产数据仓储，提供查询与导出读取能力。</param>
    /// <param name="logger">日志记录器。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="productionRepository"/> 或 <paramref name="logger"/> 为 null 时抛出。</exception>
    /// <example>
    /// <code>
    /// // 前置条件：已在 DI 中注册 IProductionRepository 与 DataManageService
    /// var service = scope.ServiceProvider.GetRequiredService&lt;DataManageService&gt;();
    /// </code>
    /// </example>
    public DataManageService(IProductionRepository productionRepository, ILogger<DataManageService> logger)
    {
        _productionRepository = productionRepository ?? throw new ArgumentNullException(nameof(productionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 按筛选条件执行分页查询，返回数据管理页所需记录集合。
    /// </summary>
    /// <param name="filter">筛选条件，包含时间范围、机台/工位与分页参数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页查询结果 <see cref="PagedResult{ProductionRecordDto}"/>。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="filter"/> 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当时间范围或分页参数不合法时抛出。</exception>
    /// <example>
    /// <code>
    /// // 前置条件：service 已通过依赖注入获取
    /// var filter = new DataManageQueryFilter
    /// {
    ///     StartTime = DateTime.Today,
    ///     EndTime = DateTime.Today.AddDays(1).AddTicks(-1),
    ///     PageIndex = 1,
    ///     PageSize = 20
    /// };
    /// var result = await service.QueryAsync(filter, cancellationToken);
    /// Console.WriteLine(result.TotalCount);
    /// </code>
    /// </example>
    public async Task<PagedResult<ProductionRecordDto>> QueryAsync(DataManageQueryFilter filter, CancellationToken cancellationToken = default)
    {
        if (filter is null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        ValidateFilter(filter);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var pagedResult = await _productionRepository.QueryAsync(
                filter.StartTime,
                filter.EndTime,
                NormalizeFilterField(filter.MachineId),
                NormalizeFilterField(filter.StationId),
                filter.PageIndex,
                filter.PageSize,
                cancellationToken);

            _logger.LogInformation(
                "DataManage 查询完成，耗时 {ElapsedMs}ms，页码 {PageIndex}，每页 {PageSize}，总数 {TotalCount}。",
                stopwatch.ElapsedMilliseconds,
                filter.PageIndex,
                filter.PageSize,
                pagedResult.TotalCount);

            return pagedResult;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "DataManage 查询失败，Start={StartTime}, End={EndTime}, Machine={MachineId}, Station={StationId}, PageIndex={PageIndex}, PageSize={PageSize}",
                filter.StartTime,
                filter.EndTime,
                filter.MachineId,
                filter.StationId,
                filter.PageIndex,
                filter.PageSize);

            throw;
        }
    }

    /// <summary>
    /// 按筛选条件导出 Excel 文件内容，供前端下载。
    /// </summary>
    /// <param name="filter">筛选条件，包含时间范围与可选机台/工位。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>导出结果，含文件名、内容与行数。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="filter"/> 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当时间范围或分页参数不合法时抛出。</exception>
    /// <exception cref="InvalidOperationException">当导出数据量超过系统限制时抛出。</exception>
    /// <example>
    /// <code>
    /// // 前置条件：service 已通过依赖注入获取
    /// var export = await service.ExportAsync(filter, cancellationToken);
    /// if (export.HasData)
    /// {
    ///     await File.WriteAllBytesAsync(export.FileName, export.Content, cancellationToken);
    /// }
    /// </code>
    /// </example>
    public async Task<DataManageExportResult> ExportAsync(DataManageQueryFilter filter, CancellationToken cancellationToken = default)
    {
        if (filter is null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        ValidateFilter(filter);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var records = (await _productionRepository.QueryForExportAsync(
                    filter.StartTime,
                    filter.EndTime,
                    NormalizeFilterField(filter.MachineId),
                    NormalizeFilterField(filter.StationId),
                    cancellationToken))
                .ToList();

            if (records.Count == 0)
            {
                _logger.LogInformation(
                    "DataManage 导出跳过：未查询到数据，Start={StartTime}, End={EndTime}, Machine={MachineId}, Station={StationId}",
                    filter.StartTime,
                    filter.EndTime,
                    filter.MachineId,
                    filter.StationId);

                return new DataManageExportResult { RowCount = 0 };
            }

            if (records.Count > MaxExportRows)
            {
                throw new InvalidOperationException($"导出数据量超过上限（{MaxExportRows:N0} 行），请缩小筛选范围后重试。");
            }

            var fileName = $"production-report_{filter.StartTime:yyyyMMddHHmmss}_{filter.EndTime:yyyyMMddHHmmss}.xlsx";
            var content = BuildExcelContent(records);

            _logger.LogInformation(
                "DataManage 导出完成，耗时 {ElapsedMs}ms，行数 {RowCount}。",
                stopwatch.ElapsedMilliseconds,
                records.Count);

            return new DataManageExportResult
            {
                Content = content,
                FileName = fileName,
                RowCount = records.Count
            };
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "DataManage 导出失败，Start={StartTime}, End={EndTime}, Machine={MachineId}, Station={StationId}",
                filter.StartTime,
                filter.EndTime,
                filter.MachineId,
                filter.StationId);

            throw;
        }
    }

    private static byte[] BuildExcelContent(IReadOnlyList<ProductionRecord> records)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("生产数据");

        worksheet.Cell(1, 1).Value = "机台";
        worksheet.Cell(1, 2).Value = "台位";
        worksheet.Cell(1, 3).Value = "重量";
        worksheet.Cell(1, 4).Value = "时间";

        for (var index = 0; index < records.Count; index++)
        {
            var rowIndex = index + 2;
            var record = records[index];

            worksheet.Cell(rowIndex, 1).Value = record.MachineId;
            worksheet.Cell(rowIndex, 2).Value = record.StationId;
            worksheet.Cell(rowIndex, 3).Value = record.BobbinWeight;
            worksheet.Cell(rowIndex, 4).Value = record.Timestamp;
        }

        worksheet.Column(3).Style.NumberFormat.Format = "0.000";
        worksheet.Column(4).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
        worksheet.Columns().AdjustToContents();

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return memoryStream.ToArray();
    }

    private static void ValidateFilter(DataManageQueryFilter filter)
    {
        if (filter.StartTime > filter.EndTime)
        {
            throw new ArgumentException("开始时间不能晚于结束时间。", nameof(filter));
        }

        if (filter.PageIndex < 1)
        {
            throw new ArgumentException("页码必须大于或等于 1。", nameof(filter));
        }

        if (filter.PageSize is < 1 or > 500)
        {
            throw new ArgumentException("每页条数必须在 1 到 500 之间。", nameof(filter));
        }
    }

    private static string? NormalizeFilterField(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
