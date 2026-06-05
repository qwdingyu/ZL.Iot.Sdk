using System;

namespace ZL.Watchdog
{
    /// <summary>
    /// Watchdog 配置。
    /// </summary>
    public class WatchdogOptions
    {
        /// <summary>检查间隔（秒），默认 5</summary>
        public int CheckIntervalSeconds { get; set; } = 5;

        /// <summary>滑动窗口时长（秒），默认 60</summary>
        public int WindowSeconds { get; set; } = 60;

        /// <summary>窗口内最大重启次数，默认 3</summary>
        public int MaxRestartsPerWindow { get; set; } = 3;

        /// <summary>验证配置合法性，返回错误列表（空 = 合法）</summary>
        public System.Collections.Generic.List<string> Validate()
        {
            var errors = new System.Collections.Generic.List<string>();
            if (CheckIntervalSeconds < 1)
                errors.Add("CheckIntervalSeconds 必须 >= 1");
            if (MaxRestartsPerWindow < 1)
                errors.Add("MaxRestartsPerWindow 必须 >= 1");
            if (WindowSeconds < CheckIntervalSeconds)
                errors.Add("WindowSeconds 不能小于 CheckIntervalSeconds");
            return errors;
        }
    }
}
