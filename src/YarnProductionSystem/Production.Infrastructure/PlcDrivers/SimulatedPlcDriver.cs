using Microsoft.Extensions.Logging;
using Production.Core.DTOs;
using Production.Core.Interfaces;

namespace Production.Infrastructure.PlcDrivers;

/// <summary>
/// 模拟 PLC 驱动：用于无真实 PLC 的联调与流程验证。
/// </summary>
public sealed class SimulatedPlcDriver : IPlcDriver
{
    private const int MachineCount = 16;
    private readonly ILogger<SimulatedPlcDriver> _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private bool _started;
    private bool _disposed;

    public event Func<ProductionRecordDto, Task>? OnDataReceived;

    public SimulatedPlcDriver(ILogger<SimulatedPlcDriver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_started)
            {
                _logger.LogDebug("SimulatedPlcDriver 已启动，忽略重复启动。");
                return;
            }

            _started = true;
            _logger.LogInformation("SimulatedPlcDriver 启动成功，设备数={MachineCount}。", MachineCount);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task StopAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _stateLock.WaitAsync();
        try
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _logger.LogInformation("SimulatedPlcDriver 已停止。");
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<IEnumerable<ProductionRecordDto>> ReadSnapshotAsync()
    {
        ThrowIfDisposed();

        if (!_started)
        {
            _logger.LogWarning("SimulatedPlcDriver 未启动，返回空快照。");
            return Array.Empty<ProductionRecordDto>();
        }

        var now = DateTime.UtcNow;
        var snapshot = new List<ProductionRecordDto>(MachineCount);

        for (var machineId = 1; machineId <= MachineCount; machineId++)
        {
            var weight = Math.Round((decimal)(Random.Shared.NextDouble() * 9.9d + 0.1d), 3);

            snapshot.Add(new ProductionRecordDto
            {
                MachineId = machineId.ToString(),
                StationId = "1",
                BobbinWeight = weight,
                Timestamp = now
            });
        }

        await RaiseDataReceivedEventsAsync(snapshot);
        return snapshot;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAsync().GetAwaiter().GetResult();
        _stateLock.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    private async Task RaiseDataReceivedEventsAsync(IEnumerable<ProductionRecordDto> records)
    {
        if (OnDataReceived is null)
        {
            return;
        }

        foreach (var record in records)
        {
            try
            {
                await OnDataReceived.Invoke(record);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "模拟数据事件处理失败，Machine={MachineId}, Station={StationId}",
                    record.MachineId,
                    record.StationId);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SimulatedPlcDriver));
        }
    }
}
