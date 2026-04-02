using System;
using System.Threading;
using System.Threading.Tasks;
using Production.Core.DTOs;
using Production.Core.Entities;

namespace Production.Core.Interfaces
{
    /// <summary>
    /// 实时缓存操作抽象（Redis 封装），包括最新重量、当日累计以及 Pub/Sub 功能。
    /// 实现应保证原子性与高性能。
    /// </summary>
    public interface IRealTimeCache : IDisposable
    {
        /// <summary>
        /// 设置最新重量（覆盖），key 格式建议： latest:{MachineId}:{StationId}
        /// </summary>
        Task SetLatestAsync(string machineId, string stationId, decimal weight);

        /// <summary>
        /// 获取最新重量，若不存在返回 null。
        /// </summary>
        Task<decimal?> GetLatestAsync(string machineId, string stationId);

        /// <summary>
        /// 原子地增加当日累计重量与累计次数，并返回更新后的累计结果。
        /// key 格式建议：
        /// - 重量：daily_w:{yyyy-MM-dd}:{MachineId}:{StationId}
        /// - 次数：daily_c:{yyyy-MM-dd}:{MachineId}:{StationId}
        /// </summary>
        Task<DailyProductionAggregate> IncrementDailyAggregateAsync(
            string dateString,
            string machineId,
            string stationId,
            decimal weightIncrement,
            long countIncrement = 1);

        /// <summary>
        /// 获取当日累计重量与次数。若键不存在，返回 null。
        /// </summary>
        Task<DailyProductionAggregate?> GetDailyAggregateAsync(string dateString, string machineId, string stationId);

        /// <summary>
        /// 发布实时消息到频道（例如 "realtime:data"），实现方应将 DTO 序列化为 JSON。
        /// </summary>
        Task PublishRealtimeAsync(string channel, ProductionRecordDto dto);

        /// <summary>
        /// 订阅实时频道，收到消息后调用回调。实现应支持取消。
        /// 注意：某些实现会在内部启动独立长连接线程，故实现方需妥善处理生命周期。
        /// </summary>
        Task SubscribeRealtimeAsync(string channel, Func<ProductionRecordDto, Task> onMessage, CancellationToken cancellationToken = default);
    }
}