using System;
using System.Collections.Generic;

namespace Production.Web.Services;

/// <summary>
/// 看板机台配置模型。
/// </summary>
public class DashboardMachineConfig
{
    /// <summary>
    /// 机台编号。
    /// </summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// 工位编号。
    /// </summary>
    public string StationId { get; set; } = string.Empty;
}

/// <summary>
/// 单机台卡片实时数据模型。
/// </summary>
public class DashboardMachineCard
{
    /// <summary>
    /// 机台编号。
    /// </summary>
    public string MachineId { get; set; } = string.Empty;

    /// <summary>
    /// 工位编号。
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// 最新卷重。
    /// </summary>
    public decimal LatestWeight { get; set; }

    /// <summary>
    /// 当日累计重量。
    /// </summary>
    public decimal DailyTotalWeight { get; set; }

    /// <summary>
    /// 当日累计数量。
    /// </summary>
    public long DailyTotalCount { get; set; }

    /// <summary>
    /// 最后更新时间。
    /// </summary>
    public DateTime? LastUpdatedAt { get; set; }
}

/// <summary>
/// 看板完整快照模型。
/// </summary>
public class DashboardSnapshot
{
    /// <summary>
    /// 当前展示的机台卡片集合。
    /// </summary>
    public IReadOnlyList<DashboardMachineCard> Machines { get; init; } = Array.Empty<DashboardMachineCard>();

    /// <summary>
    /// 全部机台当日总重量。
    /// </summary>
    public decimal TotalWeight { get; init; }

    /// <summary>
    /// 全部机台当日总数量。
    /// </summary>
    public long TotalCount { get; init; }
}

/// <summary>
/// 看板增量广播消息模型。
/// </summary>
public class DashboardUpdateMessage
{
    /// <summary>
    /// 前端订阅事件名。
    /// </summary>
    public string EventName { get; set; } = "dashboard:update";

    /// <summary>
    /// 本次更新的机台卡片。
    /// </summary>
    public DashboardMachineCard Machine { get; set; } = new();

    /// <summary>
    /// 更新后的总重量。
    /// </summary>
    public decimal TotalWeight { get; set; }

    /// <summary>
    /// 更新后的总数量。
    /// </summary>
    public long TotalCount { get; set; }
}
