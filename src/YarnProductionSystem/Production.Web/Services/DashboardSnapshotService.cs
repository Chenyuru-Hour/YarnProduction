using Production.Core.Interfaces;

namespace Production.Web.Services;

/// <summary>
/// 提供看板首屏快照加载与当前快照读取能力。
/// </summary>
public class DashboardSnapshotService
{
    private readonly IRealTimeCache _realTimeCache;
    private readonly DashboardRuntimeState _runtimeState;

    /// <summary>
    /// 初始化 <see cref="DashboardSnapshotService"/> 实例。
    /// </summary>
    /// <param name="realTimeCache">实时缓存服务，用于读取最新值与日累计。</param>
    /// <param name="runtimeState">看板运行时内存状态容器。</param>
    /// <exception cref="ArgumentNullException">当任一依赖为 null 时抛出。</exception>
    /// <example>
    /// <code>
    /// // 前置条件：已完成依赖注入注册
    /// var service = scope.ServiceProvider.GetRequiredService&lt;DashboardSnapshotService&gt;();
    /// </code>
    /// </example>
    public DashboardSnapshotService(IRealTimeCache realTimeCache, DashboardRuntimeState runtimeState)
    {
        _realTimeCache = realTimeCache ?? throw new ArgumentNullException(nameof(realTimeCache));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
    }

    /// <summary>
    /// 从 Redis 加载每台机台的当前值与日累计，并刷新内存快照。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可直接用于页面渲染的看板快照对象。</returns>
    /// <exception cref="OperationCanceledException">【中文】当调用方取消操作时抛出。</exception>
    /// <example>
    /// <code>
    /// var snapshot = await service.LoadCurrentSnapshotAsync(cancellationToken);
    /// Console.WriteLine(snapshot.TotalWeight);
    /// </code>
    /// </example>
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

    /// <summary>
    /// 读取当前内存中的看板快照。
    /// </summary>
    /// <returns>当前快照。</returns>
    /// <example>
    /// <code>
    /// var snapshot = service.GetCurrentSnapshot();
    /// Console.WriteLine(snapshot.TotalCount);
    /// </code>
    /// </example>
    public DashboardSnapshot GetCurrentSnapshot()
    {
        return _runtimeState.GetSnapshot();
    }
}
