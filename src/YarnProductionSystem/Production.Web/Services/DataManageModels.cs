using Production.Core.DTOs;

namespace Production.Web.Services;

/// <summary>
/// 数据管理页面查询条件。
/// </summary>
public sealed class DataManageQueryFilter
{
    /// <summary>
    /// 查询开始时间（含）。
    /// </summary>
    public DateTime StartTime { get; init; }

    /// <summary>
    /// 查询结束时间（含）。
    /// </summary>
    public DateTime EndTime { get; init; }

    /// <summary>
    /// 机台编号（可选）。
    /// </summary>
    public string? MachineId { get; init; }

    /// <summary>
    /// 台位编号（可选）。
    /// </summary>
    public string? StationId { get; init; }

    /// <summary>
    /// 页码，从 1 开始。
    /// </summary>
    public int PageIndex { get; init; } = 1;

    /// <summary>
    /// 每页条数。
    /// </summary>
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Excel 导出结果。
/// </summary>
public sealed class DataManageExportResult
{
    /// <summary>
    /// 导出文件内容。
    /// </summary>
    public byte[] Content { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// 导出文件名。
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// 文件 MIME 类型。
    /// </summary>
    public string ContentType { get; init; } = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// 导出总行数。
    /// </summary>
    public int RowCount { get; init; }

    /// <summary>
    /// 是否存在可导出数据。
    /// </summary>
    public bool HasData => Content.Length > 0 && RowCount > 0;
}
