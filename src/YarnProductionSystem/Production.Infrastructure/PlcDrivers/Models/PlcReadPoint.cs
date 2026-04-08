namespace Production.Infrastructure.PlcDrivers.Models
{
    /// <summary>
    /// PLC 读点映射定义。
    /// 每个读点对应一个机台与台位的重量读取地址。
    /// </summary>
    /// <example>
    /// <code>
    /// var readPoint = new PlcReadPoint
    /// {
    ///     MachineId = "1",
    ///     StationId = "2",
    ///     Db = 1,
    ///     StartByte = 0,
    ///     VarType = "Real",
    ///     Length = 1
    /// };
    /// readPoint.ValidateAndThrow();
    /// </code>
    /// </example>
    public class PlcReadPoint
    {
        public string MachineId { get; set; } = string.Empty;

        public string StationId { get; set; } = string.Empty;

        public int Db { get; set; }

        public int StartByte { get; set; }

        public string VarType { get; set; } = "Real";

        public int Length { get; set; } = 1;

        /// <summary>
        /// 校验当前读点配置并在非法时抛出异常。
        /// </summary>
        /// <exception cref="ArgumentException">当字段值无效时抛出。</exception>
        /// <example>
        /// <code>
        /// var point = new PlcReadPoint { MachineId = "1", StationId = "1", Db = 1, StartByte = 0, VarType = "Real", Length = 1 };
        /// point.ValidateAndThrow();
        /// </code>
        /// </example>
        public void ValidateAndThrow()
        {
            if (string.IsNullOrWhiteSpace(MachineId))
            {
                throw new ArgumentException("PlcReadPoint.MachineId 不能为空。", nameof(MachineId));
            }

            if (string.IsNullOrWhiteSpace(StationId))
            {
                throw new ArgumentException("PlcReadPoint.StationId 不能为空。", nameof(StationId));
            }

            if (Db <= 0)
            {
                throw new ArgumentException("PlcReadPoint.Db 必须大于 0。", nameof(Db));
            }

            if (StartByte < 0)
            {
                throw new ArgumentException("PlcReadPoint.StartByte 不能为负数。", nameof(StartByte));
            }

            if (string.IsNullOrWhiteSpace(VarType))
            {
                throw new ArgumentException("PlcReadPoint.VarType 不能为空。", nameof(VarType));
            }

            if (Length <= 0)
            {
                throw new ArgumentException("PlcReadPoint.Length 必须大于 0。", nameof(Length));
            }
        }
    }
}
