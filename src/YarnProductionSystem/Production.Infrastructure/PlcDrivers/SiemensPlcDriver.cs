using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Production.Core.DTOs;
using Production.Core.Interfaces;
using Production.Infrastructure.PlcDrivers.Models;
using Production.Infrastructure.PlcDrivers.Options;
using S7.Net;
using System.Globalization;

namespace Production.Infrastructure.PlcDrivers
{
    /// <summary>
    /// 西门子 PLC 驱动实现。
    /// </summary>
    /// <example>
    /// <code>
    /// await plcDriver.StartAsync(cancellationToken);
    /// var snapshot = await plcDriver.ReadSnapshotAsync();
    /// await plcDriver.StopAsync();
    /// </code>
    /// </example>
    public class SiemensPlcDriver : IPlcDriver
    {
        private readonly SiemensPlcOptions _options;
        private readonly ILogger<SiemensPlcDriver> _logger;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        private Plc? _plc;
        private bool _started;
        private bool _disposed;

        public event Func<ProductionRecordDto, Task>? OnDataReceived;

        /// <summary>
        /// 初始化 <see cref="SiemensPlcDriver"/>。
        /// </summary>
        /// <param name="options">西门子 PLC 配置选项。</param>
        /// <param name="logger">日志记录器。</param>
        /// <exception cref="ArgumentNullException">依赖为空时抛出。</exception>
        /// <exception cref="ArgumentException">配置非法时抛出。</exception>
        /// <example>
        /// <code>
        /// var driver = new SiemensPlcDriver(options, logger);
        /// </code>
        /// </example>
        public SiemensPlcDriver(IOptions<SiemensPlcOptions> options, ILogger<SiemensPlcDriver> logger)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options.Value ?? throw new ArgumentException("SiemensPlcOptions 未配置。", nameof(options));
            _options.ValidateAndThrow();
        }

        /// <summary>
        /// 启动 PLC 驱动并建立连接。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        /// <example>
        /// <code>
        /// await driver.StartAsync(cancellationToken);
        /// </code>
        /// </example>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            if (_started)
            {
                return;
            }

            await EnsureConnectedAsync(cancellationToken);
            _started = true;
        }

        /// <summary>
        /// 停止驱动并关闭连接。
        /// </summary>
        /// <returns>异步任务。</returns>
        /// <example>
        /// <code>
        /// await driver.StopAsync();
        /// </code>
        /// </example>
        public Task StopAsync()
        {
            if (_plc is null)
            {
                _started = false;
                return Task.CompletedTask;
            }

            try
            {
                if (_plc.IsConnected)
                {
                    _plc.Close();
                }
            }
            finally
            {
                _started = false;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 读取一次 PLC 快照。
        /// </summary>
        /// <returns>生产记录 DTO 列表。</returns>
        /// <example>
        /// <code>
        /// var records = await driver.ReadSnapshotAsync();
        /// </code>
        /// </example>
        public async Task<IEnumerable<ProductionRecordDto>> ReadSnapshotAsync()
        {
            ThrowIfDisposed();

            if (!_started)
            {
                _logger.LogWarning("SiemensPlcDriver 未启动，返回空快照。");
                return Array.Empty<ProductionRecordDto>();
            }

            await EnsureConnectedAsync(CancellationToken.None);
            if (_plc is null || !_plc.IsConnected)
            {
                _logger.LogWarning("PLC 当前不可用，返回空快照。");
                return Array.Empty<ProductionRecordDto>();
            }

            var snapshotTime = DateTime.UtcNow;
            var snapshotRecords = new List<ProductionRecordDto>(_options.ReadPoints.Count);

            foreach (var readPoint in _options.ReadPoints)
            {
                var readAddress = BuildReadAddress(readPoint);
                if (string.IsNullOrWhiteSpace(readAddress))
                {
                    _logger.LogWarning("跳过不支持的读点类型，Machine={MachineId}, Station={StationId}, VarType={VarType}", readPoint.MachineId, readPoint.StationId, readPoint.VarType);
                    continue;
                }

                try
                {
                    var rawValue = _plc.Read(readAddress);
                    var bobbinWeight = ConvertToDecimal(rawValue, readPoint.VarType);

                    var recordDto = new ProductionRecordDto
                    {
                        MachineId = readPoint.MachineId,
                        StationId = readPoint.StationId,
                        BobbinWeight = bobbinWeight,
                        Timestamp = snapshotTime
                    };

                    snapshotRecords.Add(recordDto);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "读取 PLC 点位失败，Address={Address}", readAddress);
                }
            }

            if (snapshotRecords.Count == 0)
            {
                return snapshotRecords;
            }

            await RaiseDataReceivedEventsAsync(snapshotRecords);
            return snapshotRecords;
        }

        /// <summary>
        /// 释放 PLC 驱动资源。
        /// </summary>
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
            _connectionLock.Dispose();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 确保 PLC 连接可用，不可用时执行重连。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>异步任务。</returns>
        /// <example>
        /// <code>
        /// await EnsureConnectedAsync(cancellationToken);
        /// </code>
        /// </example>
        private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (_plc is { IsConnected: true })
            {
                return;
            }

            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                if (_plc is { IsConnected: true })
                {
                    return;
                }

                _plc?.Close();
                _plc = new Plc(ParseCpuType(_options.CpuType), _options.IpAddress, _options.Rack, _options.Slot);
                _plc.ReadTimeout = _options.ConnectTimeoutMs;

                var maxAttempts = _options.RetryCount + 1;
                for (var attemptIndex = 1; attemptIndex <= maxAttempts; attemptIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        _plc.Open();
                        if (_plc.IsConnected)
                        {
                            _logger.LogInformation("PLC 连接成功，Ip={Ip}, Rack={Rack}, Slot={Slot}", _options.IpAddress, _options.Rack, _options.Slot);
                            return;
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.LogWarning(exception, "PLC 连接失败，尝试次数 {Attempt}/{MaxAttempts}", attemptIndex, maxAttempts);
                    }

                    if (attemptIndex == maxAttempts)
                    {
                        break;
                    }

                    await Task.Delay(500, cancellationToken);
                }

                _logger.LogError("PLC 重连失败，已达到最大重试次数。Ip={Ip}", _options.IpAddress);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        /// <summary>
        /// 构建 S7 读地址。
        /// </summary>
        /// <param name="readPoint">读点配置。</param>
        /// <returns>可读地址字符串；不支持类型返回空字符串。</returns>
        /// <example>
        /// <code>
        /// var address = BuildReadAddress(point); // 例如 DB1.DBD0
        /// </code>
        /// </example>
        private static string BuildReadAddress(PlcReadPoint readPoint)
        {
            if (readPoint is null)
            {
                throw new ArgumentNullException(nameof(readPoint));
            }

            var variableType = readPoint.VarType.Trim().ToLowerInvariant();
            if (variableType == "real" || variableType == "float")
            {
                return $"DB{readPoint.Db}.DBD{readPoint.StartByte}";
            }

            if (variableType == "int16" || variableType == "short")
            {
                return $"DB{readPoint.Db}.DBW{readPoint.StartByte}";
            }

            if (variableType == "int32" || variableType == "int")
            {
                return $"DB{readPoint.Db}.DBD{readPoint.StartByte}";
            }

            if (variableType == "byte")
            {
                return $"DB{readPoint.Db}.DBB{readPoint.StartByte}";
            }

            return string.Empty;
        }

        /// <summary>
        /// 将 PLC 原始值转换为 decimal。
        /// </summary>
        /// <param name="rawValue">原始读取值。</param>
        /// <param name="varType">变量类型。</param>
        /// <returns>转换后的 decimal 值。</returns>
        /// <exception cref="FormatException">转换失败时抛出。</exception>
        /// <example>
        /// <code>
        /// var value = ConvertToDecimal(rawValue, "Real");
        /// </code>
        /// </example>
        private static decimal ConvertToDecimal(object? rawValue, string varType)
        {
            if (rawValue is null)
            {
                throw new FormatException("PLC 读取结果为空，无法转换为 decimal。");
            }

            if (rawValue is decimal decimalValue)
            {
                return decimalValue;
            }

            if (rawValue is float floatValue)
            {
                return Convert.ToDecimal(floatValue, CultureInfo.InvariantCulture);
            }

            if (rawValue is double doubleValue)
            {
                return Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture);
            }

            if (rawValue is short shortValue)
            {
                return shortValue;
            }

            if (rawValue is int intValue)
            {
                return intValue;
            }

            if (rawValue is byte byteValue)
            {
                return byteValue;
            }

            if (decimal.TryParse(rawValue.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
            {
                return parsedValue;
            }

            throw new FormatException($"PLC 原始值无法转换为 decimal，VarType={varType}, Raw={rawValue}。");
        }

        /// <summary>
        /// 触发 OnDataReceived 事件。
        /// </summary>
        /// <param name="records">快照记录集合。</param>
        /// <returns>异步任务。</returns>
        /// <example>
        /// <code>
        /// await RaiseDataReceivedEventsAsync(records);
        /// </code>
        /// </example>
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
                    _logger.LogError(exception, "OnDataReceived 事件处理失败，Machine={MachineId}, Station={StationId}", record.MachineId, record.StationId);
                }
            }
        }

        /// <summary>
        /// 解析 CPU 类型。
        /// </summary>
        /// <param name="cpuTypeText">配置文本。</param>
        /// <returns>CPU 类型枚举。</returns>
        /// <exception cref="ArgumentException">解析失败时抛出。</exception>
        /// <example>
        /// <code>
        /// var cpuType = ParseCpuType("S71500");
        /// </code>
        /// </example>
        private static CpuType ParseCpuType(string cpuTypeText)
        {
            if (string.IsNullOrWhiteSpace(cpuTypeText))
            {
                throw new ArgumentException("CpuType 不能为空。", nameof(cpuTypeText));
            }

            if (Enum.TryParse<CpuType>(cpuTypeText, true, out var parsedCpuType))
            {
                return parsedCpuType;
            }

            throw new ArgumentException($"不支持的 CpuType: {cpuTypeText}。", nameof(cpuTypeText));
        }

        /// <summary>
        /// 对象释放后抛出异常。
        /// </summary>
        /// <exception cref="ObjectDisposedException">对象已释放时抛出。</exception>
        /// <example>
        /// <code>
        /// ThrowIfDisposed();
        /// </code>
        /// </example>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SiemensPlcDriver));
            }
        }
    }
}
