using System.Net.Http;

namespace ZL.ProtocolGateway
{
    /// <summary>
    /// 共享 HttpClient 工厂 — 消除各 Output 插件中重复的 CreatePooledClient/SharedHttpClient 代码。
    /// 使用 SocketsHttpHandler（.NET Core 2.1+）自动管理连接池和 DNS 刷新。
    /// </summary>
    public static class SharedHttpClientFactory
    {
        /// <summary>通用共享 HttpClient（2 分钟连接生命周期）</summary>
        public static readonly HttpClient Default = CreatePooledClient();

        /// <summary>
        /// 创建支持 DNS 刷新的 HttpClient。
        /// PooledConnectionLifetime 确保 DNS 变更在 2 分钟内生效。
        /// </summary>
#if NETCOREAPP2_1_OR_GREATER
        public static HttpClient CreatePooledClient(TimeSpan? connectionLifetime = null)
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = connectionLifetime ?? TimeSpan.FromMinutes(2)
            };
            return new HttpClient(handler);
        }
#else
        public static HttpClient CreatePooledClient(TimeSpan? connectionLifetime = null) => new HttpClient();
#endif
    }
}
