// ============================================================
// 文件：GatewayHealthCheckServiceTests.cs
// 描述：GatewayHealthCheckService 完整测试
// 修改日期：2026-06-05
// ============================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class GatewayHealthCheckServiceTests
    {
        [Fact]
        public void Constructor_WithNullManager_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new GatewayHealthCheckService(null!));
        }

        [Fact]
        public void Check_NoOutputs_ReturnsUnhealthy()
        {
            using var manager = new GatewayManager();
            var healthCheck = new GatewayHealthCheckService(manager);

            var result = healthCheck.Check();

            Assert.Equal(GatewayHealthStatus.Unhealthy, result.Status);
            Assert.Equal(0, result.TotalPlugins);
            Assert.Empty(result.PluginResults);
        }

        [Fact]
        public void Check_WithStoppedOutput_ReturnsDegraded()
        {
            using var manager = new GatewayManager();

            var mockOutput = new HealthCheckTestOutputPlugin("TestOutput");
            manager.RegisterOutput("TestOutput", mockOutput);

            var healthCheck = new GatewayHealthCheckService(manager);
            var result = healthCheck.Check();

            // Output is registered but not running (Stopped → Warning health level → Degraded bucket)
            // DetermineOverallStatus: healthy=0, degraded=1, unhealthy=0 → Degraded (not Unhealthy)
            Assert.Equal(GatewayHealthStatus.Degraded, result.Status);
            Assert.Single(result.PluginResults);
            Assert.Equal(OutputPluginHealthLevel.Warning, result.PluginResults[0].HealthLevel);
        }

        [Fact]
        public async Task Check_WithRunningOutput_ReturnsHealthy()
        {
            using var manager = new GatewayManager();

            var mockOutput = new HealthCheckTestOutputPlugin("RunningOutput");
            manager.RegisterOutput("RunningOutput", mockOutput);
            await manager.StartAsync();
            await manager.StartOutputAsync("RunningOutput");

            try
            {
                var healthCheck = new GatewayHealthCheckService(manager);
                var result = healthCheck.Check();

                Assert.Equal(GatewayHealthStatus.Healthy, result.Status);
                Assert.NotEmpty(result.PluginResults);
                Assert.Single(result.PluginResults);
                Assert.Equal(OutputPluginHealthLevel.Healthy, result.PluginResults[0].HealthLevel);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public async Task Check_MixedOutputs_AggregatesCorrectly()
        {
            using var manager = new GatewayManager();

            var output1 = new HealthCheckTestOutputPlugin("Good");
            var output2 = new HealthCheckTestOutputPlugin("Bad");
            manager.RegisterOutput("Good", output1);
            manager.RegisterOutput("Bad", output2);
            await manager.StartAsync();
            await manager.StartOutputAsync("Good");
            await manager.StartOutputAsync("Bad");

            try
            {
                var healthCheck = new GatewayHealthCheckService(manager);
                var result = healthCheck.Check();

                Assert.Equal(2, result.PluginResults.Count);
                Assert.Equal(2, result.TotalPlugins);
            }
            finally
            {
                await manager.StopAsync();
            }
        }

        [Fact]
        public void Check_Timestamp_IsRecent()
        {
            using var manager = new GatewayManager();
            var healthCheck = new GatewayHealthCheckService(manager);

            var before = DateTime.UtcNow.AddSeconds(-1);
            var result = healthCheck.Check();
            var after = DateTime.UtcNow.AddSeconds(1);

            Assert.True(result.Timestamp >= before, "Timestamp should be >= check start");
            Assert.True(result.Timestamp <= after, "Timestamp should be <= check end");
        }

        [Fact]
        public void CheckResult_IsReady_WhenHealthy()
        {
            using var manager = new GatewayManager();
            var healthCheck = new GatewayHealthCheckService(manager);
            var result = healthCheck.Check();

            // No outputs → Unhealthy → IsReady = false
            Assert.False(result.IsReady);
        }

        [Fact]
        public void CheckResult_IsLive_WhenNoPlugins()
        {
            using var manager = new GatewayManager();
            var healthCheck = new GatewayHealthCheckService(manager);
            var result = healthCheck.Check();

            // No outputs, Unhealthy but TotalPlugins==0 → IsLive = true
            Assert.True(result.IsLive);
        }
    }

    // Simple test output plugin for health check tests
    public class HealthCheckTestOutputPlugin : OutputPluginBase
    {
        public override string Name { get; }
        public override string ProtocolType => "Test";

        public HealthCheckTestOutputPlugin(string name)
        {
            Name = name;
        }

        protected override Task OnStartAsync(CancellationToken ct) => Task.CompletedTask;
        protected override Task OnStopAsync() => Task.CompletedTask;
        protected override Task OnSendAsync(Message message, CancellationToken ct) => Task.CompletedTask;
    }
}
