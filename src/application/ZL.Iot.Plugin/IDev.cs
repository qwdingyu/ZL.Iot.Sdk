using ZL.PFLite.Common;

namespace ZL.Iot.Plugin
{
    public interface IDev
    {
        //string ServerName { get; set; }
        bool IsClosed { get; set; }
        IPLCDriver device { get; set; }
        void Start(int ServiceType = 1);
        void Connect();
        void Stop();
    }
}
