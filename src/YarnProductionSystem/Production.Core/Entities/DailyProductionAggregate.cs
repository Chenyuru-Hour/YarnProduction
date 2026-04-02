namespace Production.Core.Entities
{
    /// <summary>
    /// Redis 当日累计统计结果。
    /// 对应键：
    /// - daily_w:{yyyy-MM-dd}:{MachineId}:{StationId}
    /// - daily_c:{yyyy-MM-dd}:{MachineId}:{StationId}
    /// </summary>
    public class DailyProductionAggregate
    {
        public string DateString { get; set; } = string.Empty;

        public string MachineId { get; set; } = string.Empty;

        public string StationId { get; set; } = string.Empty;

        public decimal TotalWeight { get; set; }

        public long TotalCount { get; set; }
    }
}
