using System.Collections.Generic;

namespace ZL.Iot.Controls.Core.Interfaces
{
    /// <summary>
    /// 协议服务接口
    /// 每个设备协议只需实现 Connect/Read/Write/Disconnect 四个方法
    /// </summary>
    public interface IProtocolService
    {
        /// <summary>是否已连接</summary>
        bool IsConnected { get; }

        /// <summary>连接设备</summary>
        /// <param name="ip">IP 地址</param>
        /// <param name="port">端口号</param>
        /// <param name="param">额外参数（如站号、机架号、槽号等）</param>
        /// <returns>成功返回 null 或 "OK"，失败返回错误信息</returns>
        string Connect(string ip, int port, object? param);

        /// <summary>断开连接</summary>
        /// <returns>成功返回 "OK"</returns>
        string Disconnect();

        /// <summary>读取指定地址的值</summary>
        /// <param name="address">PLC 地址，如 "DB1.DBD0"</param>
        /// <param name="dataType">数据类型，如 "Real"、"Int"、"Bool"</param>
        /// <returns>读取到的值，失败返回 null</returns>
        object? Read(string address, string dataType);

        /// <summary>写入值到指定地址</summary>
        /// <param name="address">PLC 地址</param>
        /// <param name="dataType">数据类型</param>
        /// <param name="value">要写入的值</param>
        /// <returns>成功返回 "OK"，失败返回错误信息</returns>
        string Write(string address, string dataType, object value);
    }
}