using System;
using System.Buffers;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.ConnectionGuard.Adapters
{
    /// <summary>
    /// HTTP 轮询适配器：定时请求 HTTP 接口并将响应作为数据包回调。
    /// 适用于 REST/HTTP 轮询型设备或网关。
    /// </summary>
    public sealed class HttpPollingAdapter : IChannelAdapter
    {
        private readonly HttpClient _client;
        private readonly Uri _pollUri;
        private readonly Uri _sendUri;
        private readonly TimeSpan _interval;
        private readonly string _pollMethod;
        private readonly string _sendMethod;
        private CancellationTokenSource? _pollCts;
        private Task? _pollTask;
        private bool _connected;

        public HttpPollingAdapter(
            string pollUrl,
            TimeSpan interval,
            string? sendUrl = null,
            string pollMethod = "GET",
            string sendMethod = "POST")
        {
            if (string.IsNullOrWhiteSpace(pollUrl)) throw new ArgumentNullException(nameof(pollUrl));
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));

            _client = new HttpClient();
            _pollUri = new Uri(pollUrl, UriKind.Absolute);
            _sendUri = new Uri(sendUrl ?? pollUrl, UriKind.Absolute);
            _interval = interval;
            _pollMethod = pollMethod.ToUpperInvariant();
            _sendMethod = sendMethod.ToUpperInvariant();
        }

        public string ChannelId => $"HTTP:{_pollUri}";
        public bool IsConnected => _connected;

        public event Action<byte[]>? OnDataReceived;

        public Task OpenAsync(CancellationToken token)
        {
            _connected = true;
            _pollCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _pollTask = Task.Run(() => PollLoopAsync(_pollCts.Token), _pollCts.Token);
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            _connected = false;
            try { _pollCts?.Cancel(); } catch { }
            return Task.CompletedTask;
        }

        public async Task SendAsync(byte[] data, CancellationToken token)
        {
            if (!_connected) throw new InvalidOperationException("HTTP adapter not running.");

            using var content = new ByteArrayContent(data);
            HttpMethod method = _sendMethod == "PUT" ? HttpMethod.Put : HttpMethod.Post;
            if (_sendMethod == "GET")
            {
                await _client.GetAsync(_sendUri, token);
                return;
            }

            using var request = new HttpRequestMessage(method, _sendUri)
            {
                Content = content
            };
            await _client.SendAsync(request, token);
        }

        public void Dispose()
        {
            CloseAsync();
            _client.Dispose();
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using HttpResponseMessage response = _pollMethod == "POST"
                        ? await _client.PostAsync(_pollUri, new ByteArrayContent(Array.Empty<byte>()), token)
                        : await _client.GetAsync(_pollUri, token);

                    if (response.IsSuccessStatusCode)
                    {
                        byte[] payload = await ReadAllBytesAsync(response, token);
                        if (payload.Length > 0)
                        {
                            OnDataReceived?.Invoke(payload);
                        }
                    }
                }
                catch
                {
                    // 连接失败由 ConnectionGuard 的重连/健康检查兜底。
                }

                try
                {
                    await Task.Delay(_interval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static async Task<byte[]> ReadAllBytesAsync(HttpResponseMessage response, CancellationToken token)
        {
            var stream = await response.Content.ReadAsStreamAsync(token);
            long? contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > 0 && contentLength.Value <= int.MaxValue)
            {
                int length = (int)contentLength.Value;
                byte[] exact = new byte[length];
                int offset = 0;
                while (offset < length)
                {
                    int read = await stream.ReadAsync(exact, offset, length - offset, token);
                    if (read <= 0) break;
                    offset += read;
                }
                if (offset == length) return exact;
                byte[] trimmed = new byte[offset];
                Buffer.BlockCopy(exact, 0, trimmed, 0, offset);
                return trimmed;
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
            byte[] aggregate = ArrayPool<byte>.Shared.Rent(4096);
            int total = 0;

            try
            {
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (read <= 0) break;
                    int required = total + read;
                    if (required > aggregate.Length)
                    {
                        int newSize = Math.Max(required, aggregate.Length * 2);
                        byte[] bigger = ArrayPool<byte>.Shared.Rent(newSize);
                        Buffer.BlockCopy(aggregate, 0, bigger, 0, total);
                        ArrayPool<byte>.Shared.Return(aggregate);
                        aggregate = bigger;
                    }
                    Buffer.BlockCopy(buffer, 0, aggregate, total, read);
                    total += read;
                }

                if (total <= 0) return Array.Empty<byte>();
                byte[] result = new byte[total];
                Buffer.BlockCopy(aggregate, 0, result, 0, total);
                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                ArrayPool<byte>.Shared.Return(aggregate);
            }
        }
    }
}
