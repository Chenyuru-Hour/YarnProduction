using System;
using System.Collections.Generic;

namespace Production.Web.Services;

public class DashboardMachineConfig
{
    public string MachineId { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
}

public class DashboardMachineCard
{
    public string MachineId { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public decimal LatestWeight { get; set; }
    public decimal DailyTotalWeight { get; set; }
    public long DailyTotalCount { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
}

public class DashboardSnapshot
{
    public IReadOnlyList<DashboardMachineCard> Machines { get; init; } = Array.Empty<DashboardMachineCard>();
    public decimal TotalWeight { get; init; }
    public long TotalCount { get; init; }
}

public class DashboardUpdateMessage
{
    public string EventName { get; set; } = "dashboard:update";
    public DashboardMachineCard Machine { get; set; } = new();
    public decimal TotalWeight { get; set; }
    public long TotalCount { get; set; }
}
