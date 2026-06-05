using ZL.PFLite.Common;

namespace ZL.Iot.Plugin
{
    /// <summary>
    /// 将 IPLCDriver 包装为 IDev 接口，用于设备列表管理。 </summary>
    public class SimpleInteractWrapper : IDev
    {
        private readonly IPLCDriver _driver;

        public SimpleInteractWrapper(IPLCDriver driver, string deviceId)
        {
            _driver = driver ?? throw new System.ArgumentNullException(nameof(driver));
            DeviceId = deviceId;
        }

        public string DeviceId { get; }

        public bool IsClosed
        {
            get => _driver.IsClosed;
            set => _driver.IsClosed = value;
        }

        public IPLCDriver device
        {
            get => _driver;
            set { /* IPLCDriver 引用由构造函数固定 */ }
        }

        public void Start(int ServiceType = 1)
        {
            // 由外部管理生命周期
        }

        public void Connect()
        {
            if (_driver.IsClosed)
                _driver.Connect();
        }

        public void Stop()
        {
            // 停止逻辑（占位）
        }
    }
}
