using Production.Infrastructure.PlcDrivers.Models;

namespace Production.Infrastructure.PlcDrivers.Options
{
    /// <summary>
    /// 西门子 PLC 驱动配置。
    /// 对应配置节：PlcDriver:Parameters
    /// </summary>
    /// <example>
    /// <code>
    /// var options = new SiemensPlcOptions
    /// {
    ///     IpAddress = "192.168.1.100",
    ///     Rack = 0,
    ///     Slot = 1,
    ///     CpuType = "S71500",
    ///     Port = 102,
    ///     ConnectTimeoutMs = 3000,
    ///     RetryCount = 3,
    ///     ReadPoints = new List&lt;PlcReadPoint&gt; { new() { MachineId = "1", StationId = "1", Db = 1, StartByte = 0, VarType = "Real", Length = 1 } }
    /// };
    /// options.ValidateAndThrow();
    /// </code>
    /// </example>
    public class SiemensPlcOptions
    {
        public string IpAddress { get; set; } = string.Empty;

        public short Rack { get; set; }

        public short Slot { get; set; }

        public string CpuType { get; set; } = "S71500";

        public int Port { get; set; } = 102;

        public List<PlcReadPoint> ReadPoints { get; set; } = new();

        public int ConnectTimeoutMs { get; set; } = 3000;

        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 校验配置并在非法时抛出明确异常。
        /// </summary>
        /// <exception cref="ArgumentException">参数无效时抛出。</exception>
        /// <example>
        /// <code>
        /// options.ValidateAndThrow();
        /// </code>
        /// </example>
        public void ValidateAndThrow()
        {
            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                throw new ArgumentException("SiemensPlcOptions.IpAddress 不能为空。", nameof(IpAddress));
            }

            if (Rack < 0)
            {
                throw new ArgumentException("SiemensPlcOptions.Rack 不能为负数。", nameof(Rack));
            }

            if (Slot < 0)
            {
                throw new ArgumentException("SiemensPlcOptions.Slot 不能为负数。", nameof(Slot));
            }

            if (string.IsNullOrWhiteSpace(CpuType))
            {
                throw new ArgumentException("SiemensPlcOptions.CpuType 不能为空。", nameof(CpuType));
            }

            if (Port is <= 0 or > 65535)
            {
                throw new ArgumentException("SiemensPlcOptions.Port 必须在 1~65535 之间。", nameof(Port));
            }

            if (ConnectTimeoutMs <= 0)
            {
                throw new ArgumentException("SiemensPlcOptions.ConnectTimeoutMs 必须大于 0。", nameof(ConnectTimeoutMs));
            }

            if (RetryCount < 0)
            {
                throw new ArgumentException("SiemensPlcOptions.RetryCount 不能为负数。", nameof(RetryCount));
            }

            if (ReadPoints.Count == 0)
            {
                throw new ArgumentException("SiemensPlcOptions.ReadPoints 至少需要一个读点。", nameof(ReadPoints));
            }

            foreach (var readPoint in ReadPoints)
            {
                if (readPoint is null)
                {
                    throw new ArgumentException("SiemensPlcOptions.ReadPoints 中包含空读点。", nameof(ReadPoints));
                }

                readPoint.ValidateAndThrow();
            }
        }
    }
}
