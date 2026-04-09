namespace Production.Infrastructure.PlcDrivers.Models
{
    /// <summary>
    /// PLC 读取点映射定义。
    /// 每个读取点对应机台/工位与 PLC 地址信息。
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
        /// <summary>
        /// 机台编号。
        /// </summary>
        public string MachineId { get; set; } = string.Empty;

        /// <summary>
        /// 工位编号。
        /// </summary>
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// 数据块编号（DB）。
        /// </summary>
        public int Db { get; set; }

        /// <summary>
        /// 起始字节偏移。
        /// </summary>
        public int StartByte { get; set; }

        /// <summary>
        /// 变量类型（如 Real、Int16、Int32、Byte）。
        /// </summary>
        public string VarType { get; set; } = "Real";

        /// <summary>
        /// 读取长度。
        /// </summary>
        public int Length { get; set; } = 1;

        /// <summary>
        /// 校验当前读取点配置并在非法时抛出异常。
        /// </summary>
        /// <returns>无返回值。</returns>
        /// <exception cref="ArgumentException">当字段值为空或越界时抛出。</exception>
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
