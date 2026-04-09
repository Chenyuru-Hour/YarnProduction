using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Production.Core.DTOs;
using Production.Core.Entities;
using Production.Core.Interfaces;
using StackExchange.Redis;

namespace Production.Infrastructure.Redis;

/// <summary>
/// 基于 Redis 的实时缓存实现。
/// 提供最新重量、当日累计（重量+次数）以及 Pub/Sub 功能。
/// </summary>
/// <example>
/// <code>
/// var cache = new RealTimeCache(connectionMultiplexer, logger);
/// await cache.SetLatestAsync("1", "2", 12.345m);
/// var latest = await cache.GetLatestAsync("1", "2");
/// </code>
/// </example>
public class RealTimeCache : IRealTimeCache
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly IDatabase _database;
    private readonly ISubscriber _subscriber;
    private readonly ILogger<RealTimeCache>? _logger;
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="RealTimeCache"/>。
    /// </summary>
    /// <param name="connectionMultiplexer">Redis 连接复用器。</param>
    /// <param name="logger">日志记录器。</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="connectionMultiplexer"/> 为空时抛出。</exception>
    /// <example>
    /// <code>
    /// var cache = new RealTimeCache(connectionMultiplexer, logger);
    /// </code>
    /// </example>
    public RealTimeCache(IConnectionMultiplexer connectionMultiplexer, ILogger<RealTimeCache>? logger = null)
    {
        if (connectionMultiplexer is null)
        {
            throw new ArgumentNullException(nameof(connectionMultiplexer));
        }

        _database = connectionMultiplexer.GetDatabase();
        _subscriber = connectionMultiplexer.GetSubscriber();
        _logger = logger;
    }

    /// <summary>
    /// 设置最新重量（覆盖写入）。
    /// </summary>
    /// <param name="machineId">机台编号。</param>
    /// <param name="stationId">台位编号。</param>
    /// <param name="weight">重量值（非负）。</param>
    /// <returns>异步任务。</returns>
    /// <exception cref="ArgumentException">参数非法时抛出。</exception>
    /// <example>
    /// <code>
    /// await cache.SetLatestAsync("1", "2", 9.876m);
    /// </code>
    /// </example>
    public async Task SetLatestAsync(string machineId, string stationId, decimal weight)
    {
        ThrowIfDisposed();

        ValidateMachineAndStation(machineId, stationId);
        if (weight < 0)
        {
            throw new ArgumentException("weight 不能为负数。", nameof(weight));
        }

        var latestKey = BuildLatestKey(machineId, stationId);
        var latestValue = weight.ToString(CultureInfo.InvariantCulture);

        await _database.StringSetAsync(latestKey, latestValue);
    }

    /// <summary>
    /// 获取最新重量，不存在时返回 null。
    /// </summary>
    /// <param name="machineId">机台编号。</param>
    /// <param name="stationId">台位编号。</param>
    /// <returns>最新重量或 null。</returns>
    /// <exception cref="ArgumentException">参数非法时抛出。</exception>
    /// <example>
    /// <code>
    /// var latest = await cache.GetLatestAsync("1", "2");
    /// </code>
    /// </example>
    public async Task<decimal?> GetLatestAsync(string machineId, string stationId)
    {
        ThrowIfDisposed();

        ValidateMachineAndStation(machineId, stationId);

        var latestKey = BuildLatestKey(machineId, stationId);
        var redisValue = await _database.StringGetAsync(latestKey);
        if (!redisValue.HasValue)
        {
            return null;
        }

        if (decimal.TryParse(redisValue.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latestWeight))
        {
            return latestWeight;
        }

        _logger?.LogError("解析最新重量失败，Key={Key}, Value={Value}", latestKey, redisValue.ToString());
        throw new FormatException($"Redis 值无法解析为 decimal，Key={latestKey}。");
    }

    /// <summary>
    /// 原子增加当日累计重量与累计次数，并返回更新后的累计结果。
    /// </summary>
    /// <param name="dateString">日期字符串（yyyy-MM-dd）。</param>
    /// <param name="machineId">机台编号。</param>
    /// <param name="stationId">台位编号。</param>
    /// <param name="weightIncrement">重量增量（非负）。</param>
    /// <param name="countIncrement">次数增量（非负）。</param>
    /// <returns>更新后的累计结果。</returns>
    /// <exception cref="ArgumentException">参数非法时抛出。</exception>
    /// <example>
    /// <code>
    /// var aggregate = await cache.IncrementDailyAggregateAsync("2026-04-02", "1", "2", 3.210m, 1);
    /// </code>
    /// </example>
    public async Task<DailyProductionAggregate> IncrementDailyAggregateAsync(
        string dateString,
        string machineId,
        string stationId,
        decimal weightIncrement,
        long countIncrement = 1)
    {
        ThrowIfDisposed();

        ValidateDateMachineAndStation(dateString, machineId, stationId);
        if (weightIncrement < 0)
        {
            throw new ArgumentException("weightIncrement 不能为负数。", nameof(weightIncrement));
        }

        if (countIncrement < 0)
        {
            throw new ArgumentException("countIncrement 不能为负数。", nameof(countIncrement));
        }

        var weightKey = BuildDailyWeightKey(dateString, machineId, stationId);
        var countKey = BuildDailyCountKey(dateString, machineId, stationId);

        const string luaScript = @"
local newWeight = redis.call('INCRBYFLOAT', KEYS[1], ARGV[1])
local newCount = redis.call('INCRBY', KEYS[2], ARGV[2])
return { newWeight, newCount }";

        var scriptResult = await _database.ScriptEvaluateAsync(
            luaScript,
            new RedisKey[] { weightKey, countKey },
            new RedisValue[]
            {
                weightIncrement.ToString(CultureInfo.InvariantCulture),
                countIncrement
            });

        var resultItems = (RedisResult[]?)scriptResult;
        if (resultItems is null || resultItems.Length < 2)
        {
            throw new InvalidOperationException("Redis 原子累加返回项不足。");
        }

        var totalWeight = ParseDecimal(resultItems[0].ToString(), weightKey);
        var totalCount = ParseLong(resultItems[1].ToString(), countKey);

        return new DailyProductionAggregate
        {
            DateString = dateString,
            MachineId = machineId,
            StationId = stationId,
            TotalWeight = totalWeight,
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// 获取当日累计重量与次数。
    /// 两个键都不存在时返回 null；存在单键时缺失值按 0 处理。
    /// </summary>
    /// <param name="dateString">日期字符串（yyyy-MM-dd）。</param>
    /// <param name="machineId">机台编号。</param>
    /// <param name="stationId">台位编号。</param>
    /// <returns>累计结果或 null。</returns>
    /// <exception cref="ArgumentException">参数非法时抛出。</exception>
    /// <example>
    /// <code>
    /// var aggregate = await cache.GetDailyAggregateAsync("2026-04-02", "1", "2");
    /// </code>
    /// </example>
    public async Task<DailyProductionAggregate?> GetDailyAggregateAsync(string dateString, string machineId, string stationId)
    {
        ThrowIfDisposed();

        ValidateDateMachineAndStation(dateString, machineId, stationId);

        var weightKey = BuildDailyWeightKey(dateString, machineId, stationId);
        var countKey = BuildDailyCountKey(dateString, machineId, stationId);

        var values = await _database.StringGetAsync(new RedisKey[] { weightKey, countKey });
        var weightValue = values[0];
        var countValue = values[1];

        if (!weightValue.HasValue && !countValue.HasValue)
        {
            return null;
        }

        var totalWeight = weightValue.HasValue ? ParseDecimal(weightValue.ToString(), weightKey) : 0m;
        var totalCount = countValue.HasValue ? ParseLong(countValue.ToString(), countKey) : 0L;

        return new DailyProductionAggregate
        {
            DateString = dateString,
            MachineId = machineId,
            StationId = stationId,
            TotalWeight = totalWeight,
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// 发布实时消息到 Redis 频道。
    /// </summary>
    /// <param name="channel">频道名称。</param>
    /// <param name="dto">生产记录消息体。</param>
    /// <returns>异步任务。</returns>
    /// <exception cref="ArgumentException">参数非法时抛出。</exception>
    /// <exception cref="ArgumentNullException">当 <paramref name="dto"/> 为空时抛出。</exception>
    /// <example>
    /// <code>
    /// await cache.PublishRealtimeAsync("realtime:data", dto);
    /// </code>
    /// </example>
    public async Task PublishRealtimeAsync(string channel, ProductionRecordDto dto)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException("channel 不能为空。", nameof(channel));
        }

        if (dto is null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        var messageJson = JsonSerializer.Serialize(dto);
        await _subscriber.PublishAsync(RedisChannel.Literal(channel), messageJson);
    }

    /// <summary>
    /// 订阅实时频道并在接收消息时回调。
    /// </summary>
    /// <param name="channel">频道名称。</param>
    /// <param name="onMessage">消息回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    /// <exception cref="ArgumentException">参数非法时抛出。</exception>
    /// <exception cref="ArgumentNullException">当 <paramref name="onMessage"/> 为空时抛出。</exception>
    /// <example>
    /// <code>
    /// await cache.SubscribeRealtimeAsync("realtime:data", async dto =>
    /// {
    ///     Console.WriteLine($"{dto.MachineId}-{dto.StationId}: {dto.BobbinWeight}");
    /// });
    /// </code>
    /// </example>
    public async Task SubscribeRealtimeAsync(
        string channel,
        Func<ProductionRecordDto, Task> onMessage,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(channel))
        {
            throw new ArgumentException("channel 不能为空。", nameof(channel));
        }

        if (onMessage is null)
        {
            throw new ArgumentNullException(nameof(onMessage));
        }

        await _subscriber.SubscribeAsync(RedisChannel.Literal(channel), (receivedChannel, redisValue) =>
        {
            _ = HandleRealtimeMessageAsync(redisValue, onMessage);
        });

        if (!cancellationToken.CanBeCanceled)
        {
            return;
        }

        var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            _ = _subscriber.UnsubscribeAsync(RedisChannel.Literal(channel));
            completionSource.TrySetCanceled(cancellationToken);
        });

        await completionSource.Task;
    }

    /// <summary>
    /// 释放当前对象占用的资源。
    /// </summary>
    /// <example>
    /// <code>
    /// cache.Dispose();
    /// </code>
    /// </example>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 处理订阅到的实时消息。
    /// </summary>
    /// <param name="redisValue">Redis 消息体。</param>
    /// <param name="onMessage">业务回调。</param>
    /// <returns>异步任务。</returns>
    /// <example>
    /// <code>
    /// await HandleRealtimeMessageAsync(value, callback);
    /// </code>
    /// </example>
    private async Task HandleRealtimeMessageAsync(RedisValue redisValue, Func<ProductionRecordDto, Task> onMessage)
    {
        if (!redisValue.HasValue)
        {
            return;
        }

        var payload = redisValue.ToString();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<ProductionRecordDto>(payload);
            if (dto is null)
            {
                _logger?.LogWarning("实时消息反序列化结果为空，Payload={Payload}", payload);
                return;
            }

            await onMessage(dto);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "处理实时消息失败，Payload={Payload}", payload);
        }
    }

    /// <summary>
    /// 构建最新重量键。
    /// </summary>
    /// <param name="machineId">取纱机编号。</param>
    /// <param name="stationId">台位编号。</param>
    /// <returns>Redis 键名。</returns>
    /// <example>
    /// <code>
    /// var key = BuildLatestKey("1", "2"); // latest:1:2
    /// </code>
    /// </example>
    private static string BuildLatestKey(string machineId, string stationId) => $"latest:{machineId}:{stationId}";

    /// <summary>
    /// 构建当日累计重量键。
    /// </summary>
    /// <param name="dateString">日期字符串。</param>
    /// <param name="machineId">取纱机编号。</param>
    /// <param name="stationId">台位编号。</param>
    /// <returns>Redis 键名。</returns>
    /// <example>
    /// <code>
    /// var key = BuildDailyWeightKey("2026-04-02", "1", "2");
    /// </code>
    /// </example>
    private static string BuildDailyWeightKey(string dateString, string machineId, string stationId) =>
        $"daily_w:{dateString}:{machineId}:{stationId}";

    /// <summary>
    /// 构建当日累计次数键。
    /// </summary>
    /// <param name="dateString">日期字符串。</param>
    /// <param name="machineId">取纱机编号。</param>
    /// <param name="stationId">台位编号。</param>
    /// <returns>Redis 键名。</returns>
    /// <example>
    /// <code>
    /// var key = BuildDailyCountKey("2026-04-02", "1", "2");
    /// </code>
    /// </example>
    private static string BuildDailyCountKey(string dateString, string machineId, string stationId) =>
        $"daily_c:{dateString}:{machineId}:{stationId}";

    /// <summary>
    /// 校验取纱机编号与台位参数。
    /// </summary>
    /// <param name="machineId">取纱机编号。</param>
    /// <param name="stationId">台位编号。</param>
    /// <exception cref="ArgumentException">参数为空白时抛出。</exception>
    /// <example>
    /// <code>
    /// ValidateMachineAndStation("1", "2");
    /// </code>
    /// </example>
    private static void ValidateMachineAndStation(string machineId, string stationId)
    {
        if (string.IsNullOrWhiteSpace(machineId))
        {
            throw new ArgumentException("machineId 不能为空。", nameof(machineId));
        }

        if (string.IsNullOrWhiteSpace(stationId))
        {
            throw new ArgumentException("stationId 不能为空。", nameof(stationId));
        }
    }

    /// <summary>
    /// 校验日期、机台与台位参数。
    /// </summary>
    /// <param name="dateString">日期字符串。</param>
    /// <param name="machineId">取纱机编号。</param>
    /// <param name="stationId">台位编号。</param>
    /// <exception cref="ArgumentException">参数不合法时抛出。</exception>
    /// <example>
    /// <code>
    /// ValidateDateMachineAndStation("2026-04-02", "1", "2");
    /// </code>
    /// </example>
    private static void ValidateDateMachineAndStation(string dateString, string machineId, string stationId)
    {
        if (string.IsNullOrWhiteSpace(dateString))
        {
            throw new ArgumentException("dateString 不能为空。", nameof(dateString));
        }

        if (!DateTime.TryParseExact(dateString, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException("dateString 格式必须为 yyyy-MM-dd。", nameof(dateString));
        }

        ValidateMachineAndStation(machineId, stationId);
    }

    /// <summary>
    /// 解析 decimal 文本值。
    /// </summary>
    /// <param name="rawValue">原始文本。</param>
    /// <param name="key">Redis 键名。</param>
    /// <returns>解析后的 decimal。</returns>
    /// <exception cref="FormatException">解析失败时抛出。</exception>
    /// <example>
    /// <code>
    /// var value = ParseDecimal("12.34", "daily_w:2026-04-02:1:2");
    /// </code>
    /// </example>
    private static decimal ParseDecimal(string rawValue, string key)
    {
        if (decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        throw new FormatException($"Redis 值无法解析为 decimal，Key={key}。");
    }

    /// <summary>
    /// 解析 long 文本值。
    /// </summary>
    /// <param name="rawValue">原始文本。</param>
    /// <param name="key">Redis 键名。</param>
    /// <returns>解析后的 long。</returns>
    /// <exception cref="FormatException">解析失败时抛出。</exception>
    /// <example>
    /// <code>
    /// var value = ParseLong("12", "daily_c:2026-04-02:1:2");
    /// </code>
    /// </example>
    private static long ParseLong(string rawValue, string key)
    {
        if (long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue))
        {
            return parsedValue;
        }

        throw new FormatException($"Redis 值无法解析为 long，Key={key}。");
    }

    /// <summary>
    /// 在对象已释放时抛出异常。
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
            throw new ObjectDisposedException(nameof(RealTimeCache));
        }
    }
}
