using System;
using Production.Core.Entities;

namespace Production.Core.DTOs
{
    /// <summary>
    /// 用于在 API/SignalR/HUB 中交换的生产记录 DTO，避免直接返回 EF 实体。
    /// 包含从实体转换的工厂方法与简单校验。
    /// </summary>
    public class ProductionRecordDto
    {
        public long? Id { get; set; }
        public string MachineId { get; set; } = string.Empty;
        public string StationId { get; set; } = string.Empty;
        public decimal BobbinWeight { get; set; }
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 从实体创建 DTO，若实体为空返回 null。
        /// 使用早返回以简化调用点的逻辑。
        /// </summary>
        /// <param name="entity">来源实体</param>
        /// <returns>转换后的 DTO 或 null</returns>
        public static ProductionRecordDto? FromEntity(ProductionRecord? entity)
        {
            if (entity == null) return null;

            return new ProductionRecordDto
            {
                Id = entity.Id,
                MachineId = entity.MachineId,
                StationId = entity.StationId,
                BobbinWeight = entity.BobbinWeight,
                Timestamp = entity.Timestamp
            };
        }

        /// <summary>
        /// 将 DTO 转换为实体（用于写入数据库前）
        /// </summary>
        /// <returns>ProductionRecord 实体</returns>
        public ProductionRecord ToEntity()
        {
            return new ProductionRecord
            {
                Id = Id ?? 0,
                MachineId = MachineId,
                StationId = StationId,
                BobbinWeight = BobbinWeight,
                Timestamp = Timestamp
            };
        }

        /*
         示例用法：
         var dto = new ProductionRecordDto {
            MachineId = "1",
            StationId = "A",
            BobbinWeight = 1.234m,
            Timestamp = DateTime.UtcNow
         };
         var entity = dto.ToEntity();
        */
    }
}