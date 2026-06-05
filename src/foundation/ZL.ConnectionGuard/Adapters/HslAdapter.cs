using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ConnectionGuard.Adapters
{
    /// <summary>
    /// HSL 适配器：通过反射兼容 HslCommunication 的 NetworkDeviceBase。
    /// 不依赖 HSL 包，运行时有程序集即可生效。
    /// </summary>
    public sealed class HslAdapter : IChannelAdapter
    {
        private readonly object _device;
        private readonly Func<object?>? _connect;
        private readonly Action? _close;
        private readonly Func<byte[], object?>? _readFromCore;

        public HslAdapter(object device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            var type = _device.GetType();
            _connect = CreateFunc(type, _device, "ConnectServer");
            _close = CreateAction(type, _device, "ConnectClose");
            _readFromCore = CreateFuncWithBytes(type, _device, "ReadFromCoreServer");
        }

        public string ChannelId => $"HSL:{_device.GetType().Name}";

        public bool IsConnected { get; private set; }

        public event Action<byte[]>? OnDataReceived;

        public Task OpenAsync(CancellationToken token)
        {
            return Task.Run(() =>
            {
                if (_connect == null) throw new InvalidOperationException("ConnectServer method not found.");
                var result = _connect();
                IsConnected = IsSuccess(result);
                if (!IsConnected)
                {
                    throw new InvalidOperationException(GetResultMessage(result) ?? "HSL connect failed.");
                }
            }, token);
        }

        public Task CloseAsync()
        {
            return Task.Run(() =>
            {
                _close?.Invoke();
                IsConnected = false;
            });
        }

        public Task SendAsync(byte[] data, CancellationToken token)
        {
            return Task.Run(() =>
            {
                if (_readFromCore == null)
                {
                    throw new InvalidOperationException("ReadFromCoreServer method not found.");
                }

                var result = _readFromCore(data);
                if (!IsSuccess(result))
                {
                    throw new InvalidOperationException(GetResultMessage(result) ?? "HSL send failed.");
                }

                var response = GetContentBytes(result);
                if (response != null && response.Length > 0)
                {
                    OnDataReceived?.Invoke(response);
                }
            }, token);
        }

        public void Dispose()
        {
            CloseAsync();
        }

        private static bool IsSuccess(object? result)
        {
            if (result == null) return false;
            var prop = result.GetType().GetProperty("IsSuccess");
            if (prop?.PropertyType == typeof(bool))
            {
                return (bool)(prop.GetValue(result) ?? false);
            }
            return true;
        }

        private static string? GetResultMessage(object? result)
        {
            if (result == null) return null;
            var prop = result.GetType().GetProperty("Message");
            return prop?.GetValue(result) as string;
        }

        private static byte[]? GetContentBytes(object? result)
        {
            if (result == null) return null;
            var prop = result.GetType().GetProperty("Content");
            return prop?.GetValue(result) as byte[];
        }

        private static Func<object?>? CreateFunc(Type type, object instance, string name)
        {
            var method = type.GetMethod(name, Type.EmptyTypes);
            if (method == null) return null;
            return (Func<object?>)Delegate.CreateDelegate(typeof(Func<object?>), instance, method);
        }

        private static Action? CreateAction(Type type, object instance, string name)
        {
            var method = type.GetMethod(name, Type.EmptyTypes);
            if (method == null) return null;
            return (Action)Delegate.CreateDelegate(typeof(Action), instance, method);
        }

        private static Func<byte[], object?>? CreateFuncWithBytes(Type type, object instance, string name)
        {
            var method = type.GetMethod(name, new[] { typeof(byte[]) });
            if (method == null) return null;
            return (Func<byte[], object?>)Delegate.CreateDelegate(typeof(Func<byte[], object?>), instance, method);
        }
    }
}
