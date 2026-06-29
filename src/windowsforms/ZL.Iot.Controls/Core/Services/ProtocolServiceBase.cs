using System;
using ZL.Iot.Controls.Common;

namespace ZL.Iot.Controls.Core.Services
{
    /// <summary>
    /// 协议服务基类
    /// 提供统一错误处理、日志埋点、重试机制
    /// 所有协议实现应继承此类而不是直接实现 IProtocolService
    /// </summary>
    public abstract class ProtocolServiceBase : ZL.Iot.Controls.Core.Interfaces.IProtocolService
    {
        public abstract bool IsConnected { get; }

        /// <summary>PLC 设备驱动实例（供 UcTagTable 批量采集使用，仿真模式返回 null）</summary>
        public abstract ZL.IotHub.Core.IPlcDevice? PlcDevice { get; }

        public string Connect(string ip, int port, object? param)
        {
            try
            {
                var result = OnConnect(ip, port, param);
                var msg = result ?? "OK";
                LogHelper.Info($"连接 {ip}:{port} = {msg}");
                return msg;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"连接失败 {ip}:{port} - {ex.Message}");
                return ex.Message;
            }
        }

        public string Disconnect()
        {
            try
            {
                OnDisconnect();
                LogHelper.Info("已断开连接");
                return "OK";
            }
            catch (Exception ex)
            {
                LogHelper.Error($"断开连接失败: {ex.Message}");
                return ex.Message;
            }
        }

        public object? Read(string address, string dataType)
        {
            try
            {
                var result = OnRead(address, dataType);
                LogHelper.Debug($"读 {address}({dataType}) = {result}");
                return result;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"读 {address} 失败: {ex.Message}");
                return null;
            }
        }

        public string Write(string address, string dataType, object value)
        {
            try
            {
                OnWrite(address, dataType, value);
                LogHelper.Info($"写 {address}({dataType}) = {value}");
                return "OK";
            }
            catch (Exception ex)
            {
                LogHelper.Error($"写 {address} 失败: {ex.Message}");
                return ex.Message;
            }
        }

        // ===== 子类需实现以下方法 =====
        protected abstract string? OnConnect(string ip, int port, object? param);
        protected abstract void OnDisconnect();
        protected abstract object? OnRead(string address, string dataType);
        protected abstract void OnWrite(string address, string dataType, object value);
    }
}