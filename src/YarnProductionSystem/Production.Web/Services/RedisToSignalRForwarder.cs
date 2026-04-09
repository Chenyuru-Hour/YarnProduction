using Microsoft.AspNetCore.SignalR;
using Production.Core.DTOs;
using Production.Core.Interfaces;
using Production.Web.Hubs;

namespace Production.Web.Services;

/// <summary>
/// Redis 到 SignalR 的转发后台服务。
/// 用于把实时通道消息转发给前端看板客户端。
/// </summary>
public class RedisToSignalRForwarder : BackgroundService
{
    private readonly IRealTimeCache _realTimeCache;
    private readonly DashboardSnapshotService _snapshotService;
    private readonly DashboardRuntimeState _runtimeState;
    private readonly IHubContext<ProductionHub> _hubContext;
    private readonly ILogger<RedisToSignalRForwarder> _logger;
    private readonly string _realtimeChannel;

    /// <summary>
    /// 初始化 <see cref="RedisToSignalRForwarder"/> 实例。
    /// </summary>
    /// <param name="realTimeCache">实时缓存与订阅服务。</param>
    /// <param name="snapshotService">看板快照加载服务。</param>
    /// <param name="runtimeState">看板运行时状态容器。</param>
    /// <param name="hubContext">SignalR Hub 上下文。</param>
    /// <param name="configuration">应用配置，读取 Redis 实时通道。</param>
    /// <param name="logger">日志记录器。</param>
    /// <exception cref="ArgumentNullException">当任一必需依赖为 null 时抛出。</exception>
    /// <example>
    /// <code>
    /// // 前置条件：在 Program.cs 中已注册 HostedService
    /// services.AddHostedService&lt;RedisToSignalRForwarder&gt;();
    /// </code>
    /// </example>
    public RedisToSignalRForwarder(
        IRealTimeCache realTimeCache,
        DashboardSnapshotService snapshotService,
        DashboardRuntimeState runtimeState,
        IHubContext<ProductionHub> hubContext,
        IConfiguration configuration,
        ILogger<RedisToSignalRForwarder> logger)
    {
        _realTimeCache = realTimeCache ?? throw new ArgumentNullException(nameof(realTimeCache));
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var configuredChannel = configuration["RedisChannel:RealtimeData"];
        _realtimeChannel = string.IsNullOrWhiteSpace(configuredChannel) ? "realtime:data" : configuredChannel;
    }

    /// <summary>
    /// 启动转发主循环：先加载初始快照，再订阅 Redis 实时消息并广播到 SignalR。
    /// </summary>
    /// <param name="stoppingToken">宿主提供的停止令牌。</param>
    /// <returns>表示后台任务执行过程的异步任务。</returns>
    /// <exception cref="OperationCanceledException">当宿主触发停止时抛出并由框架处理。</exception>
    /// <example>
    /// <code>
    /// // 该方法由宿主自动调用，一般无需手动调用。
    /// // 如需集成测试，可通过启动 Host 触发 ExecuteAsync。
    /// await host.StartAsync(cancellationToken);
    /// </code>
    /// </example>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 实时链路第 3 段（首屏）：先从 Redis 读取当前快照并写入运行态。
        await _snapshotService.LoadCurrentSnapshotAsync(stoppingToken);
        // 向前端广播可用状态，提示 SignalR 转发链路已就绪。
        await _hubContext.Clients.All.SendAsync("dashboard:health", "ready", stoppingToken);

        _logger.LogInformation("Redis 转发器已启动，订阅频道 {Channel}", _realtimeChannel);

        try
        {
            // 订阅 Worker 发布的 Redis 实时频道，收到消息后转发到 SignalR。
            await _realTimeCache.SubscribeRealtimeAsync(_realtimeChannel, dto => ForwardAsync(dto, stoppingToken), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Redis 转发器已停止。");
        }
    }

    private async Task ForwardAsync(ProductionRecordDto dto, CancellationToken stoppingToken)
    {
        // 将增量 DTO 合并进内存态并计算最新总计。
        var update = _runtimeState.ApplyIncrement(dto);
        // 实时链路第 4 段：通过 SignalR 推送到所有看板客户端。
        await _hubContext.Clients.All.SendAsync("dashboard:update", update, stoppingToken);
    }
}
