using Production.Core.DTOs;
using Production.Core.DTOs;
using Production.Core.Entities;
using Production.Core.Interfaces;

namespace Production.Worker
{
    /// <summary>
    /// 生产采集后台服务：采集 PLC 数据并完成入库、缓存与实时发布。
    /// </summary>
    /// <example>
    /// 启动后每个采集周期执行：ReadSnapshot -> BulkInsert -> Redis 更新 -> Pub/Sub 发布。
    /// </example>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IPlcDriver _plcDriver;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IRealTimeCache _realTimeCache;
        private readonly string _realtimeChannel;
        private readonly int _collectIntervalMs;

        /// <summary>
        /// 初始化 <see cref="Worker"/>。
        /// </summary>
        /// <param name="logger">日志记录器。</param>
        /// <param name="plcDriver">PLC 驱动。</param>
        /// <param name="productionRepository">生产记录仓储。</param>
        /// <param name="realTimeCache">实时缓存服务。</param>
        /// <param name="configuration">应用配置。</param>
        /// <exception cref="ArgumentNullException">依赖为空时抛出。</exception>
        /// <example>
        /// 由依赖注入创建：services.AddHostedService&lt;Worker&gt;();
        /// </example>
        public Worker(
            ILogger<Worker> logger,
            IPlcDriver plcDriver,
            IServiceScopeFactory scopeFactory,
            IRealTimeCache realTimeCache,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _plcDriver = plcDriver ?? throw new ArgumentNullException(nameof(plcDriver));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _realTimeCache = realTimeCache ?? throw new ArgumentNullException(nameof(realTimeCache));

            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            var configuredChannel = configuration["RedisChannel:RealtimeData"];
            _realtimeChannel = string.IsNullOrWhiteSpace(configuredChannel) ? "realtime:data" : configuredChannel;

            var configuredInterval = configuration.GetValue<int?>("CollectIntervalMs") ?? 1000;
            _collectIntervalMs = configuredInterval <= 0 ? 1000 : configuredInterval;
        }

        /// <summary>
        /// 执行后台采集主循环。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        /// <example>
        /// Worker 每轮执行完整链路：采集 -> 校验 -> 入库 -> 缓存 -> 发布。
        /// </example>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _plcDriver.StartAsync(stoppingToken);
            _logger.LogInformation("Worker 已启动，采集周期 {IntervalMs}ms，频道 {Channel}", _collectIntervalMs, _realtimeChannel);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    await ProcessBatchAsync(stoppingToken);
                    await Task.Delay(_collectIntervalMs, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker 收到停止信号，准备退出。");
            }
            finally
            {
                await _plcDriver.StopAsync();
            }
        }

        /// <summary>
        /// 处理单个采集批次（批次级异常隔离）。
        /// </summary>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>异步任务。</returns>
        /// <example>
        /// await ProcessBatchAsync(stoppingToken);
        /// </example>
        private async Task ProcessBatchAsync(CancellationToken stoppingToken)
        {
            try
            {
                var snapshot = await _plcDriver.ReadSnapshotAsync();
                var snapshotRecords = snapshot?.ToList() ?? new List<ProductionRecordDto>();
                if (snapshotRecords.Count == 0)
                {
                    _logger.LogDebug("本轮快照为空，跳过处理。");
                    return;
                }

                var validDtos = new List<ProductionRecordDto>(snapshotRecords.Count);
                var entitiesToInsert = new List<ProductionRecord>(snapshotRecords.Count);
                var invalidRecordCount = 0;

                foreach (var snapshotRecord in snapshotRecords)
                {
                    if (!TryValidateDto(snapshotRecord, out var validationError))
                    {
                        invalidRecordCount++;
                        _logger.LogWarning("快照记录无效，已跳过。Reason={Reason}", validationError);
                        continue;
                    }

                    var productionEntity = ConvertToEntity(snapshotRecord);
                    if (!productionEntity.TryValidate(out var entityValidationError))
                    {
                        invalidRecordCount++;
                        _logger.LogWarning("实体记录无效，已跳过。Reason={Reason}", entityValidationError);
                        continue;
                    }

                    validDtos.Add(snapshotRecord);
                    entitiesToInsert.Add(productionEntity);
                }

                if (entitiesToInsert.Count == 0)
                {
                    _logger.LogWarning("本轮无有效记录。总数={Total}, 无效={Invalid}", snapshotRecords.Count, invalidRecordCount);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var productionRepository = scope.ServiceProvider.GetRequiredService<IProductionRepository>();
                await productionRepository.BulkInsertAsync(entitiesToInsert, stoppingToken);

                var cacheSuccessCount = 0;
                var cacheFailureCount = 0;
                foreach (var validDto in validDtos)
                {
                    var cacheUpdated = await TryProcessRealtimeCacheAsync(validDto, stoppingToken);
                    if (cacheUpdated)
                    {
                        cacheSuccessCount++;
                        continue;
                    }

                    cacheFailureCount++;
                }

                _logger.LogInformation(
                    "批次完成：快照总数={Total}, 入库成功={Saved}, 无效={Invalid}, 缓存成功={CacheSuccess}, 缓存失败={CacheFailed}",
                    snapshotRecords.Count,
                    entitiesToInsert.Count,
                    invalidRecordCount,
                    cacheSuccessCount,
                    cacheFailureCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "批次处理失败，已隔离并继续下一批。");
            }
        }

        /// <summary>
        /// 处理单条记录的缓存更新与发布（单条级异常隔离）。
        /// </summary>
        /// <param name="recordDto">记录 DTO。</param>
        /// <param name="stoppingToken">停止令牌。</param>
        /// <returns>成功返回 true，失败返回 false。</returns>
        /// <example>
        /// var ok = await TryProcessRealtimeCacheAsync(dto, stoppingToken);
        /// </example>
        private async Task<bool> TryProcessRealtimeCacheAsync(ProductionRecordDto recordDto, CancellationToken stoppingToken)
        {
            try
            {
                stoppingToken.ThrowIfCancellationRequested();

                var dateString = recordDto.Timestamp.ToString("yyyy-MM-dd");
                await _realTimeCache.SetLatestAsync(recordDto.MachineId, recordDto.StationId, recordDto.BobbinWeight);
                await _realTimeCache.IncrementDailyAggregateAsync(dateString, recordDto.MachineId, recordDto.StationId, recordDto.BobbinWeight, 1);
                await _realTimeCache.PublishRealtimeAsync(_realtimeChannel, recordDto);

                return true;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "实时链路处理失败，Machine={MachineId}, Station={StationId}, Weight={Weight}",
                    recordDto.MachineId,
                    recordDto.StationId,
                    recordDto.BobbinWeight);
                return false;
            }
        }

        /// <summary>
        /// 校验快照 DTO。
        /// </summary>
        /// <param name="recordDto">待校验 DTO。</param>
        /// <param name="error">错误信息。</param>
        /// <returns>合法返回 true，否则 false。</returns>
        /// <example>
        /// if (!TryValidateDto(dto, out var error)) { ... }
        /// </example>
        private static bool TryValidateDto(ProductionRecordDto? recordDto, out string error)
        {
            error = string.Empty;

            if (recordDto is null)
            {
                error = "DTO 为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(recordDto.MachineId))
            {
                error = "MachineId 为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(recordDto.StationId))
            {
                error = "StationId 为空。";
                return false;
            }

            if (recordDto.BobbinWeight < 0)
            {
                error = "BobbinWeight 不能为负数。";
                return false;
            }

            if (recordDto.Timestamp == default)
            {
                error = "Timestamp 不能为空。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// DTO 转实体。
        /// </summary>
        /// <param name="recordDto">来源 DTO。</param>
        /// <returns>实体对象。</returns>
        /// <exception cref="ArgumentNullException">参数为空时抛出。</exception>
        /// <example>
        /// var entity = ConvertToEntity(dto);
        /// </example>
        private static ProductionRecord ConvertToEntity(ProductionRecordDto recordDto)
        {
            if (recordDto is null)
            {
                throw new ArgumentNullException(nameof(recordDto));
            }

            return new ProductionRecord
            {
                MachineId = recordDto.MachineId,
                StationId = recordDto.StationId,
                BobbinWeight = recordDto.BobbinWeight,
                Timestamp = recordDto.Timestamp
            };
        }
    }
}
