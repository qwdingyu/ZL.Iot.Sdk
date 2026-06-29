using System;
using System.Diagnostics;

namespace ZL.Iot.Controls.Common
{
    /// <summary>
    /// 时间辅助方法集合。
    /// </summary>
    public static class TimeHelpers
    {
        /// <summary>
        /// 计算从起始时间到现在经过的时间。
        /// </summary>
        public static TimeSpan GetElapsedSince(DateTime start) => DateTime.Now - start;

        /// <summary>
        /// 计算从 Stopwatch 启动到现在经过的时间。
        /// </summary>
        public static TimeSpan GetElapsedSince(Stopwatch stopwatch)
        {
            if (stopwatch == null) throw new ArgumentNullException(nameof(stopwatch));
            return stopwatch.Elapsed;
        }
    }
}
