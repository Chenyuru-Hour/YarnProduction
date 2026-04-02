using System;

namespace Production.Core.Entities
{
    /// <summary>
    /// 生产记录实体，映射到数据库表 ProductionRecords。
    /// 包含简单的校验方法（TryValidate），用于在写入数据库前进行输入检查。
    /// </summary>
    public class ProductionRecord
    {
        /// <summary>
        /// 自增主键
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 取纱机编号（1~16 等），不得为空
        /// </summary>
        public string MachineId { get; set; } = string.Empty;

        /// <summary>
        /// 台位标识，不得为空
        /// </summary>
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// 纱卷重量（kg），必须为非负小数
        /// </summary>
        public decimal BobbinWeight { get; set; }

        /// <summary>
        /// 采集时间（UTC 推荐），不得超前太多或为空
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 尝试校验当前实体的基本有效性。若校验失败返回 false 并在 out 参数中提供错误信息。
        /// 使用早退（early return）风格实现简单且高可读。
        /// </summary>
        /// <param name="error">校验失败时的错误描述，成功时为空字符串</param>
        /// <returns>校验通过返回 true，否则返回 false</returns>
        public bool TryValidate(out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(MachineId))
            {
                error = "MachineId 不能为空。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(StationId))
            {
                error = "StationId 不能为空。";
                return false;
            }

            if (BobbinWeight < 0m)
            {
                error = "BobbinWeight 必须为非负数。";
                return false;
            }

            // 不允许采集时间过早或过未来（允许误差，例如 ±7 天）
            var now = DateTime.UtcNow;
            if (Timestamp == default)
            {
                error = "Timestamp 不能为空或默认值���";
                return false;
            }

            if (Timestamp < now.AddYears(-5) || Timestamp > now.AddMinutes(5))
            {
                error = "Timestamp 超出合理范围（超过过去 5 年或未来超过 5 分钟）。";
                return false;
            }

            return true;
        }

        /*
         示例用法：
         var record = new ProductionRecord {
            MachineId = "1",
            StationId = "A",
            BobbinWeight = 2.345m,
            Timestamp = DateTime.UtcNow
         };
         if (!record.TryValidate(out var err)) {
            // 处理校验错误
         }
        */
    }
}