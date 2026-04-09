using Production.Core.DTOs;

namespace Production.Web.Services;

/// <summary>
/// 看板运行时状态：维护机台卡片内存态并提供快照/增量更新能力。
/// </summary>
public class DashboardRuntimeState
{
    private readonly object _syncRoot = new();
    private readonly List<DashboardMachineConfig> _configs;
    private readonly Dictionary<string, DashboardMachineCard> _machines;
    private decimal _totalWeight;
    private long _totalCount;

    /// <summary>
    /// 初始化 <see cref="DashboardRuntimeState"/> 实例。
    /// </summary>
    /// <param name="configuration">配置源，读取 <c>Dashboard:Machines</c> 机台映射。</param>
    /// <returns>无返回值。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="configuration"/> 为 null 时，配置访问会触发异常。</exception>
    /// <example>
    /// <code>
    /// // 前置条件：appsettings.json 已提供 Dashboard:Machines 或使用默认 1..16 机台
    /// var state = new DashboardRuntimeState(configuration);
    /// Console.WriteLine(state.Configs.Count);
    /// </code>
    /// </example>
    public DashboardRuntimeState(IConfiguration configuration)
    {
        _configs = configuration.GetSection("Dashboard:Machines").Get<List<DashboardMachineConfig>>() ?? new();
        if (_configs.Count == 0)
        {
            _configs = Enumerable.Range(1, 16)
                .Select(index => new DashboardMachineConfig
                {
                    MachineId = index.ToString(),
                    StationId = "1"
                })
                .ToList();
        }

        _machines = _configs.ToDictionary(BuildKey, config => new DashboardMachineCard
        {
            MachineId = config.MachineId,
            StationId = config.StationId,
            LatestWeight = 0m,
            DailyTotalWeight = 0m,
            DailyTotalCount = 0
        });
    }

    /// <summary>
    /// 获取按展示顺序排列的机台配置集合。
    /// </summary>
    public IReadOnlyList<DashboardMachineConfig> Configs => _configs;

    /// <summary>
    /// 获取当前内存状态的只读快照副本。
    /// </summary>
    /// <returns>包含机台卡片与总计信息的快照。</returns>
    /// <example>
    /// <code>
    /// var snapshot = state.GetSnapshot();
    /// Console.WriteLine(snapshot.TotalWeight);
    /// </code>
    /// </example>
    public DashboardSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            var machineCards = _configs
                .Select(config => Clone(_machines[BuildKey(config)]))
                .ToList();

            return new DashboardSnapshot
            {
                Machines = machineCards,
                TotalWeight = _totalWeight,
                TotalCount = _totalCount
            };
        }
    }

    /// <summary>
    /// 使用完整卡片集合重建运行时状态，并返回重建后的快照。
    /// </summary>
    /// <param name="cards">待应用的机台卡片集合。</param>
    /// <returns>应用完成后的最新快照。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="cards"/> 为 null 时，枚举过程会触发异常。</exception>
    /// <example>
    /// <code>
    /// var snapshot = state.SetSnapshot(new[]
    /// {
    ///     new DashboardMachineCard { MachineId = "1", StationId = "1", DailyTotalWeight = 10m, DailyTotalCount = 1 }
    /// });
    /// Console.WriteLine(snapshot.TotalCount);
    /// </code>
    /// </example>
    public DashboardSnapshot SetSnapshot(IEnumerable<DashboardMachineCard> cards)
    {
        lock (_syncRoot)
        {
            _totalWeight = 0m;
            _totalCount = 0;

            foreach (var card in cards)
            {
                var key = BuildKey(card.MachineId, card.StationId);
                _machines[key] = Clone(card);
                _totalWeight += card.DailyTotalWeight;
                _totalCount += card.DailyTotalCount;
            }

            return GetSnapshot();
        }
    }

    /// <summary>
    /// 将单条生产记录增量应用到内存状态，并生成前端广播消息。
    /// </summary>
    /// <param name="dto">实时生产记录 DTO。</param>
    /// <returns>用于 SignalR 推送的看板增量消息。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="dto"/> 为 null 时访问属性会触发异常。</exception>
    /// <example>
    /// <code>
    /// var update = state.ApplyIncrement(new ProductionRecordDto
    /// {
    ///     MachineId = "1",
    ///     StationId = "1",
    ///     BobbinWeight = 1.25m,
    ///     Timestamp = DateTime.UtcNow
    /// });
    /// Console.WriteLine(update.TotalWeight);
    /// </code>
    /// </example>
    public DashboardUpdateMessage ApplyIncrement(ProductionRecordDto dto)
    {
        lock (_syncRoot)
        {
            var key = BuildKey(dto.MachineId, dto.StationId);
            if (!_machines.TryGetValue(key, out var machineCard))
            {
                machineCard = new DashboardMachineCard
                {
                    MachineId = dto.MachineId,
                    StationId = dto.StationId
                };
                _machines[key] = machineCard;
            }

            machineCard.LatestWeight = dto.BobbinWeight;
            machineCard.DailyTotalWeight += dto.BobbinWeight;
            machineCard.DailyTotalCount += 1;
            machineCard.LastUpdatedAt = dto.Timestamp;

            _totalWeight += dto.BobbinWeight;
            _totalCount += 1;

            return new DashboardUpdateMessage
            {
                EventName = "dashboard:update",
                Machine = Clone(machineCard),
                TotalWeight = _totalWeight,
                TotalCount = _totalCount
            };
        }
    }

    private static DashboardMachineCard Clone(DashboardMachineCard source)
    {
        return new DashboardMachineCard
        {
            MachineId = source.MachineId,
            StationId = source.StationId,
            LatestWeight = source.LatestWeight,
            DailyTotalWeight = source.DailyTotalWeight,
            DailyTotalCount = source.DailyTotalCount,
            LastUpdatedAt = source.LastUpdatedAt
        };
    }

    private static string BuildKey(DashboardMachineConfig config) => BuildKey(config.MachineId, config.StationId);

    private static string BuildKey(string machineId, string stationId) => $"{machineId}:{stationId}";
}
