using Microsoft.AspNetCore.SignalR;
using Production.Core.DTOs;
using Production.Core.Interfaces;
using Production.Web.Hubs;

namespace Production.Web.Services;

public class RedisToSignalRForwarder : BackgroundService
{
    private readonly IRealTimeCache _realTimeCache;
    private readonly DashboardSnapshotService _snapshotService;
    private readonly DashboardRuntimeState _runtimeState;
    private readonly IHubContext<ProductionHub> _hubContext;
    private readonly ILogger<RedisToSignalRForwarder> _logger;
    private readonly string _realtimeChannel;

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _snapshotService.LoadCurrentSnapshotAsync(stoppingToken);
        await _hubContext.Clients.All.SendAsync("dashboard:health", "ready", stoppingToken);

        _logger.LogInformation("Redis 转发服务已启动，订阅频道 {Channel}", _realtimeChannel);

        try
        {
            await _realTimeCache.SubscribeRealtimeAsync(_realtimeChannel, dto => ForwardAsync(dto, stoppingToken), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Redis 转发服务停止。");
        }
    }

    private async Task ForwardAsync(ProductionRecordDto dto, CancellationToken stoppingToken)
    {
        var update = _runtimeState.ApplyIncrement(dto);
        await _hubContext.Clients.All.SendAsync("dashboard:update", update, stoppingToken);
    }
}
