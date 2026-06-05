using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using SqlSugar;

namespace ZL.DB.Acc.Ex
{
    public static class ReportEx
    {
        /// <summary>
        /// 按月统计
        /// </summary>
        public static List<object> YearMonth<T>(this SqlSugarScope scope) where T : class, new()
        {
            //生成月份
            var queryableLeft = scope.Reportable(ReportableDateType.MonthsInLast1years).ToQueryable<DateTime>();
            //声名表
            var queryableRight = scope.Queryable<T>();
            //由于SqlSugar的动态表达式限制，这里返回空列表，实际使用时请根据具体实体编写
            return new List<object>();
        }

        /// <summary>
        /// 统计某月每天的数量
        /// </summary>
        public static List<object> MonthDay<T>(this SqlSugarScope db, DateTime time) where T : class, new()
        {
            var days = (time.AddMonths(1) - time).Days;
            var dayArray = Enumerable.Range(1, days).Select(it => Convert.ToDateTime(time.ToString("yyyy-MM-" + it))).ToList();
            return new List<object>();
        }
    }
}
