using Production.Core.Interfaces;

namespace Production.Web.Services;

public class DashboardSnapshotService
{
    private readonly IRealTimeCache _realTimeCache;
    private readonly DashboardRuntimeState _runtimeState;

    public DashboardSnapshotService(IRealTimeCache realTimeCache, DashboardRuntimeState runtimeState)
    {
        _realTimeCache = realTimeCache ?? throw new ArgumentNullException(nameof(realTimeCache));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
    }

    public async Task<DashboardSnapshot> LoadCurrentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var dateString = DateTime.Now.ToString("yyyy-MM-dd");
        var cards = new List<DashboardMachineCard>();

        foreach (var machineConfig in _runtimeState.Configs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var latestWeight = await _realTimeCache.GetLatestAsync(machineConfig.MachineId, machineConfig.StationId) ?? 0m;
            var aggregate = await _realTimeCache.GetDailyAggregateAsync(dateString, machineConfig.MachineId, machineConfig.StationId);

            cards.Add(new DashboardMachineCard
            {
                MachineId = machineConfig.MachineId,
                StationId = machineConfig.StationId,
                LatestWeight = latestWeight,
                DailyTotalWeight = aggregate?.TotalWeight ?? 0m,
                DailyTotalCount = aggregate?.TotalCount ?? 0,
                LastUpdatedAt = null
            });
        }

        return _runtimeState.SetSnapshot(cards);
    }

    public DashboardSnapshot GetCurrentSnapshot()
    {
        return _runtimeState.GetSnapshot();
    }
}
