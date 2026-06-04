using System;

namespace ZL.EdgeService
{
    public class EsEvents
    {
        /// <summary>
        /// 更新PLC连接状态
        /// 第一个参数为设备id，第二个为 ping状态，第三个为conn状态
        /// </summary>
        public static Action<string, bool, bool> OnDeviceConnStatus { get; set; }
    }
}
