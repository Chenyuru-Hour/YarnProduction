using System;
using System.Collections.Generic;

namespace Production.Core.DTOs
{
    /// <summary>
    /// 分页结果封装类型，通用于查询接口返回。
    /// </summary>
    /// <typeparam name="T">单条记录类型</typeparam>
    public class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
        public int PageIndex { get; init; }
        public int PageSize { get; init; }
        public long TotalCount { get; init; }

        /// <summary>
        /// 方便构造的方法
        /// </summary>
        public static PagedResult<T> Create(IReadOnlyList<T> items, int pageIndex, int pageSize, long totalCount)
        {
            return new PagedResult<T>
            {
                Items = items,
                PageIndex = pageIndex,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        /*
         示例：
         var page = PagedResult<ProductionRecordDto>.Create(items, 1, 50, totalCount);
        */
    }
}