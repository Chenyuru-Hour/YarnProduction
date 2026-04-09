using Microsoft.Extensions.Logging;
using Production.Core.DTOs;
using Production.Core.Interfaces;

namespace Production.Infrastructure.PlcDrivers;

/// <summary>
/// 模拟 PLC 驱动，用于本地联调与无硬件场景验证。
/// </summary>
public sealed class SimulatedPlcDriver : IPlcDriver
{
    private const int MachineCount = 16;
    private readonly ILogger<SimulatedPlcDriver> _logger;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private bool _started;
    private bool _disposed;

    /// <summary>
    /// 当生成到一条模拟数据时触发。
    /// </summary>
    public event Func<ProductionRecordDto, Task>? OnDataReceived;

    /// <summary>
    /// 初始化 <see cref="SimulatedPlcDriver"/> 实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="logger"/> 为 null 时抛出。</exception>
    /// <example>
    /// <code>
    /// var logger = loggerFactory.CreateLogger&lt;SimulatedPlcDriver&gt;();
    /// var driver = new SimulatedPlcDriver(logger);
    /// </code>
    /// </example>
    public SimulatedPlcDriver(ILogger<SimulatedPlcDriver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 启动模拟驱动。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>表示启动过程的异步任务。</returns>
    /// <exception cref="ObjectDisposedException">当实例已释放时抛出。</exception>
    /// <exception cref="OperationCanceledException">当取消令牌被触发时抛出。</exception>
    /// <example>
    /// <code>
    /// await driver.StartAsync(cancellationToken);
    /// </code>
    /// </example>
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

    /// <summary>
    /// 停止模拟驱动。
    /// </summary>
    /// <returns>表示停止过程的异步任务。</returns>
    /// <example>
    /// <code>
    /// await driver.StopAsync();
    /// </code>
    /// </example>
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

    /// <summary>
    /// 读取一批模拟快照数据。
    /// </summary>
    /// <returns>模拟生成的生产记录集合。</returns>
    /// <exception cref="ObjectDisposedException">当实例已释放时抛出。</exception>
    /// <example>
    /// <code>
    /// var records = await driver.ReadSnapshotAsync();
    /// Console.WriteLine(records.Count());
    /// </code>
    /// </example>
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
            var stationId = Random.Shared.Next(1, 7).ToString(); // [1, 6]

            snapshot.Add(new ProductionRecordDto
            {
                MachineId = machineId.ToString(),
                StationId = stationId,
                BobbinWeight = weight,
                Timestamp = now
            });
        }

        await RaiseDataReceivedEventsAsync(snapshot);
        return snapshot;
    }

    /// <summary>
    /// 释放驱动资源并停止后续读写。
    /// </summary>
    /// <returns>无返回值。</returns>
    /// <example>
    /// <code>
    /// driver.Dispose();
    /// </code>
    /// </example>
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

    /// <summary>
    /// Asynchronously raises data received events for each production record in the specified collection.
    /// </summary>
    /// <remarks>If no event handlers are attached, the method completes without raising any events.
    /// Exceptions thrown by event handlers are caught and logged; event processing continues for remaining
    /// records.</remarks>
    /// <param name="records">A collection of production records for which to raise data received events. Cannot be null.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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
