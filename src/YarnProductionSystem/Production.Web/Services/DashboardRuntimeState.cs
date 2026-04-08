using Production.Core.DTOs;

namespace Production.Web.Services;

public class DashboardRuntimeState
{
    private readonly object _syncRoot = new();
    private readonly List<DashboardMachineConfig> _configs;
    private readonly Dictionary<string, DashboardMachineCard> _machines;
    private decimal _totalWeight;
    private long _totalCount;

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

    public IReadOnlyList<DashboardMachineConfig> Configs => _configs;

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
