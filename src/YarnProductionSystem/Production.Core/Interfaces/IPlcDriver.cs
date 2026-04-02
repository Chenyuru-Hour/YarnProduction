using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Production.Core.DTOs;

namespace Production.Core.Interfaces
{
    /// <summary>
    /// PLC 驱动抽象接口，供 Worker 或其他采集组件注入使用。
    /// 注意：具体实现应保证线程安全并处理网络异常重试。
    /// </summary>
    public interface IPlcDriver : IDisposable
    {
        /// <summary>
        /// 启动驱动并开始连接设备（若需要），方法应返回可被取消的任务。
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止驱动并释放资源。
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 读取一次快照数据（可用于批量读取当前所有台位的值）
        /// 返回 ProductionRecordDto 列表。
        /// </summary>
        Task<IEnumerable<ProductionRecordDto>> ReadSnapshotAsync();

        /// <summary>
        /// 当驱动实时获取到单条生产记录时触发该事件（用于推送、异步处理等）
        /// </summary>
        event Func<ProductionRecordDto, Task>? OnDataReceived;
    }
}