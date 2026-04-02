using System;

namespace Production.Core.Entities
{
    /// <summary>
    /// 归档日志实体，用于记录归档作业执行情况。
    /// </summary>
    public class ArchiveLog
    {
        /// <summary>
        /// 自增主键
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 归档执行时间
        /// </summary>
        public DateTime ArchiveDate { get; set; }

        /// <summary>
        /// 本次归档的记录数
        /// </summary>
        public int RecordsArchived { get; set; }

        /// <summary>
        /// 执行状态，例如 "Success" 或 "Failed"
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// 错误信息（可选）
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 尝试校验 ArchiveLog 基本有效性
        /// </summary>
        /// <param name="error">失败原因</param>
        /// <returns>校验通过返回 true，否则返回 false</returns>
        public bool TryValidate(out string error)
        {
            error = string.Empty;

            if (ArchiveDate == default)
            {
                error = "ArchiveDate 不能为空或默认值。";
                return false;
            }

            if (RecordsArchived < 0)
            {
                error = "RecordsArchived 不能为负数。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(Status))
            {
                error = "Status 不能为空。";
                return false;
            }

            return true;
        }

        /*
         示例：
         var log = new ArchiveLog {
           ArchiveDate = DateTime.UtcNow,
           RecordsArchived = 1234,
           Status = "Success"
         };
         if (!log.TryValidate(out var err)) { ... }
        */
    }
}