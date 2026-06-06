using System;
using System.Text;
using System.Threading.Tasks;
using ZL.ProtocolGateway;
using ZL.ProtocolGateway.Plugins;

namespace DemoTcpForwarding
{
    /// <summary>
    /// 演示：TCP 端口转发 (8080 -> 8081)
    /// 验证 ProtocolGateway 核心链路：Input -> Pipeline -> Output
    /// </summary>
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== ProtocolGateway Demo: TCP Forwarding ===");
            Console.WriteLine("Scenario: Listen on :8080 -> Forward to :8081");
            Console.WriteLine("Test Command: nc -v 127.0.0.1 8080 (Type and Enter)");
            Console.WriteLine("=================================================\n");

            try
            {
                // 1. 配置输出插件 (目标地址)
                var outputConfig = new TcpOutputConfig
                {
                    Name = "TargetServer",
                    ServerIp = "127.0.0.1",
                    Port = 8081,
                    Suffix = "\n" // 转发时自动追加换行
                };
                var tcpOutput = new TcpOutputPlugin(outputConfig);

                // 2. 配置流水线 (Pipeline)
                var pipeline = new MessagePipeline();
                
                // 注册输出
                pipeline.RegisterOutput(tcpOutput);

                // 添加路由规则：所有来自 TCP Input 的消息都转发给 TargetServer
                pipeline.AddRouter(new RouteRule
                {
                    Name = "ForwardAllTcp",
                    Condition = msg => msg.Metadata.ContainsKey("Protocol") && msg.Metadata["Protocol"] == "Tcp",
                    OutputNames = { "TargetServer" },
                    ContinueMatching = false
                });

                // 3. 配置输入插件 (监听地址)
                var inputConfig = new TcpInputConfig
                {
                    Name = "LocalListener",
                    LocalIp = "0.0.0.0",
                    Port = 8080,
                    Delimiter = Encoding.ASCII.GetBytes("\n") // 按行分包
                };
                var tcpInput = new TcpInputPlugin(inputConfig);

                // 4. 组装网关服务
                var gateway = new GatewayService(pipeline);
                gateway.AddInput(tcpInput);

                // 5. 启动服务
                await gateway.StartAsync();

                Console.WriteLine("\n[Press Enter to Exit]");
                Console.ReadLine();

                await gateway.StopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] {ex.Message}");
            }
        }
    }
}
