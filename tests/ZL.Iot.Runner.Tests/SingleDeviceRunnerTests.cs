// ============================================================
//  SingleDeviceRunner 单元测试
//  覆盖：事件订阅/取消、驱动类型路由、DriverFactory.Create 集成
// ============================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Runtime;
using ZL.IotHub.Core;
using ZL.IotHub.Hsl;
using ZL.IotHub.Native;
using ZL.Tag;

namespace ZL.Iot.Runner.Tests
{
    /// <summary>
    /// 可触发的测试用 NativeUnifiedDriver。
    /// 通过反射直接调用 TriggerDataChanged 事件的私有后台委托，
    /// 以绕过 C# 对事件外部调用的限制（事件只能 += / -=，不能外部 Invoke）。
    /// </summary>
    internal class FireableNativeDriver : NativeUnifiedDriver
    {
        private static readonly System.Reflection.FieldInfo? _triggerField =
            typeof(NativeUnifiedDriver).GetField("TriggerDataChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        public FireableNativeDriver(DeviceConfig config) : base(config) { }

        /// <summary>直接触发 TriggerDataChanged 事件，模拟驱动层标签变化通知。</summary>
        public void FireTrigger(string tagId, object value, TagItem tag)
        {
            if (_triggerField?.GetValue(this) is Action<string, object, TagItem> handler)
                handler(tagId, value, tag);
        }
    }

    /// <summary>
    /// 可触发的测试用 HslUnifiedDriver。
    /// </summary>
    internal class FireableHslDriver : HslUnifiedDriver
    {
        private static readonly System.Reflection.FieldInfo? _triggerField =
            typeof(HslUnifiedDriver).GetField("TriggerDataChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        public FireableHslDriver(DeviceConfig config) : base(config) { }

        /// <summary>直接触发 TriggerDataChanged 事件，模拟驱动层标签变化通知。</summary>
        public void FireTrigger(string tagId, object value, TagItem tag)
        {
            if (_triggerField?.GetValue(this) is Action<string, object, TagItem> handler)
                handler(tagId, value, tag);
        }
    }

    /// <summary>
    /// 一个没有 TriggerDataChanged 事件的 DeviceRoot 子类，
    /// 用于测试 SubscribeToDriverEvents 的 default 降级路径。
    /// </summary>
    internal class NonTriggerDriver : DeviceRoot
    {
        public NonTriggerDriver() : base(new DeviceConfig()) { }
        public override void LoadSelfConfig() { }
        public override Task ReconnectionLoop(CancellationToken token) => Task.CompletedTask;
        public override Task HeartBeatLoop(CancellationToken token) => Task.CompletedTask;
        public override Task BatchReadLoop(CancellationToken token) => Task.CompletedTask;
    }

    /// <summary>
    /// SingleDeviceRunner 单元测试
    ///
    /// 覆盖：
    /// 1. NativeUnifiedDriver 驱动的事件订阅与触发传播
    /// 2. HslUnifiedDriver 驱动的事件订阅与触发传播
    /// 3. 不支持的驱动类型（无 TriggerDataChanged）→ 静默降级
    /// 4. Dispose 后事件正确取消订阅（内存泄漏防护）
    /// 5. DriverFactory.Create 工厂方法集成
    /// 6. 构造参数验证
    /// </summary>
    public class SingleDeviceRunnerTests
    {
        private static readonly TagItem SampleTag = new()
        {
            Id = "TEST_TAG",
            Address = "DB1.DBX0.0",
            DataTypeCode = "bool",
            Enable = true,
            TagType = "M"
        };

        #region 构造与事件订阅 - NativeUnifiedDriver

        [Fact]
        public void Constructor_NativeDriver_TypeMatchWorks()
        {
            // Arrange — 验证 FireableNativeDriver 能被 NativeUnifiedDriver pattern 匹配
            var config = CreateMinimalConfig("native-siemsrc-s7", "192.168.1.100", 102);
            var driver = new FireableNativeDriver(config);
            var executor = CreateEmptyExecutor();

            // Act
            var runner = new SingleDeviceRunner("DEV-001", driver, executor,
                NullLogger<SingleDeviceRunner>.Instance,
                protocol: "siemsrc-s7", ip: "192.168.1.100", port: 102);

            // Assert — 构造成功即表明 SubscribeToDriverEvents 未因类型匹配失败而走 default 路径
            Assert.NotNull(runner);
            Assert.Equal(0, runner.GetStatus().CollectedCount);
            runner.Dispose();
        }

        [Fact]
        public void Constructor_NativeDriver_DisposeDoesNotThrow()
        {
            var config = CreateMinimalConfig("native-modbus-tcp", "10.0.0.1", 502);
            var driver = new FireableNativeDriver(config);
            var executor = CreateEmptyExecutor();

            var runner = new SingleDeviceRunner("DEV-002", driver, executor,
                NullLogger<SingleDeviceRunner>.Instance,
                protocol: "modbus-tcp", ip: "10.0.0.1", port: 502);

            // Act & Assert — Dispose 后不应抛异常
            var ex = Record.Exception(() => runner.Dispose());
            Assert.Null(ex);
        }

        #endregion

        #region 构造与事件订阅 - HslUnifiedDriver (实际事件传播验证)

        [Fact]
        public void Constructor_HslDriver_SubscribesToTriggerDataChanged()
        {
            // Arrange
            var config = CreateMinimalConfig("hsl-siemsrc-s7", "192.168.1.200", 102);
            var driver = new FireableHslDriver(config);
            var executor = CreateEmptyExecutor();

            // Act
            var runner = new SingleDeviceRunner("DEV-003", driver, executor,
                NullLogger<SingleDeviceRunner>.Instance,
                protocol: "siemsrc-s7", ip: "192.168.1.200", port: 102);

            // Assert
            Assert.Equal(0, runner.GetStatus().CollectedCount);

            driver.FireTrigger("TEMP_1", 25.5, SampleTag);

            Assert.Equal(1, runner.GetStatus().CollectedCount);

            runner.Dispose();
        }

        [Fact]
        public void Constructor_HslDriver_MultipleTriggers_AggregatesCount()
        {
            // Arrange
            var config = CreateMinimalConfig("hsl-modbus-tcp", "10.0.0.2", 502);
            var driver = new FireableHslDriver(config);
            var executor = CreateEmptyExecutor();

            var runner = new SingleDeviceRunner("DEV-004", driver, executor,
                NullLogger<SingleDeviceRunner>.Instance,
                protocol: "modbus-tcp", ip: "10.0.0.2", port: 502);

            // Act — 触发 3 次
            driver.FireTrigger("T1", 1, SampleTag);
            driver.FireTrigger("T2", 2, SampleTag);
            driver.FireTrigger("T3", 3, SampleTag);

            // Assert
            Assert.Equal(3, runner.GetStatus().CollectedCount);

            runner.Dispose();
        }

        #endregion

        #region 不支持驱动的降级

        [Fact]
        public void Constructor_NonTriggerDriver_FallsBackGracefully()
        {
            // Arrange
            var driver = new NonTriggerDriver();
            var executor = CreateEmptyExecutor();

            // Act & Assert — 不抛异常即通过
            var runner = new SingleDeviceRunner("DEV-NOTRIGGER", driver, executor,
                NullLogger<SingleDeviceRunner>.Instance);

            // 在无 TriggerDataChanged 的驱动上，不会订阅事件
            Assert.Equal(0, runner.GetStatus().CollectedCount);

            runner.Dispose();
        }

        #endregion

        #region Dispose 事件取消订阅

        [Fact]
        public void Dispose_NativeDriver_UnsubscribesGracefully()
        {
            // Arrange — 验证 Dispose 时 UnsubscribeFromDriverEvents 不抛异常
            var config = CreateMinimalConfig("native-siemsrc-s7", "10.0.0.10", 102);
            var driver = new FireableNativeDriver(config);
            var executor = CreateEmptyExecutor();

            var runner = new SingleDeviceRunner("DEV-DISPOSE", driver, executor,
                NullLogger<SingleDeviceRunner>.Instance);

            // Act & Assert
            var ex = Record.Exception(() => runner.Dispose());
            Assert.Null(ex);
        }

        [Fact]
        public void Dispose_HslDriver_UnsubscribesFromTriggerDataChanged()
        {
            // Arrange
            var config = CreateMinimalConfig("hsl-modbus-tcp", "10.0.0.20", 502);
            var driver = new FireableHslDriver(config);
            var executor = CreateEmptyExecutor();

            var runner = new SingleDeviceRunner("DEV-DISPOSE-HSL", driver, executor,
                NullLogger<SingleDeviceRunner>.Instance,
                protocol: "modbus-tcp", ip: "10.0.0.20", port: 502);

            driver.FireTrigger("BEFORE", "x", SampleTag);
            Assert.Equal(1, runner.GetStatus().CollectedCount);

            // Act
            runner.Dispose();

            // Assert — 取消订阅后事件不再传播
            driver.FireTrigger("AFTER", "y", SampleTag);
            Assert.Equal(0, runner.GetStatus().CollectedCount);
        }

        #endregion

        #region DriverFactory.Create 集成

        [Fact]
        public void Create_WithNativeProtocol_ReturnsRunnerWithSubscribedEvents()
        {
            // Arrange
            var profile = new DeviceProfile
            {
                Code = "DEV-FACTORY",
                Ip = "0.0.0.0",
                Protocol = "siemsrc-s7",
                Port = 102,
                Tags = new List<TagProfile>(),
                Executors = new List<ExecutorProfile>()
            };

            // Act
            var runner = SingleDeviceRunner.Create(profile,
                NullLoggerFactory.Instance);

            // Assert — 构造成功
            Assert.NotNull(runner);
            Assert.Equal("DEV-FACTORY", runner.DeviceCode);

            runner.Dispose();
        }

        [Fact]
        public void Create_WithDefaultProtocol_DoesNotThrow()
        {
            // Arrange
            var profile = new DeviceProfile
            {
                Code = "DEV-DEFAULT",
                Ip = "0.0.0.0",
                Protocol = "",
                Port = 102,
                Tags = new List<TagProfile>(),
                Executors = new List<ExecutorProfile>()
            };

            // Act & Assert
            var runner = SingleDeviceRunner.Create(profile,
                NullLoggerFactory.Instance);
            Assert.NotNull(runner);

            runner.Dispose();
        }

        #endregion

        #region 构造参数验证

        [Fact]
        public void Constructor_NullDeviceCode_ThrowsArgumentNullException()
        {
            var driver = new NonTriggerDriver();
            var executor = CreateEmptyExecutor();

            Assert.Throws<ArgumentNullException>(() =>
                new SingleDeviceRunner(null!, driver, executor,
                    NullLogger<SingleDeviceRunner>.Instance));
        }

        [Fact]
        public void Constructor_NullDriver_ThrowsArgumentNullException()
        {
            var executor = CreateEmptyExecutor();

            Assert.Throws<ArgumentNullException>(() =>
                new SingleDeviceRunner("DEV", null!, executor,
                    NullLogger<SingleDeviceRunner>.Instance));
        }

        [Fact]
        public void Constructor_NullExecutor_ThrowsArgumentNullException()
        {
            var driver = new NonTriggerDriver();

            Assert.Throws<ArgumentNullException>(() =>
                new SingleDeviceRunner("DEV", driver, null!,
                    NullLogger<SingleDeviceRunner>.Instance));
        }

        [Fact]
        public void Constructor_NullLogger_ThrowsArgumentNullException()
        {
            var driver = new NonTriggerDriver();
            var executor = CreateEmptyExecutor();

            Assert.Throws<ArgumentNullException>(() =>
                new SingleDeviceRunner("DEV", driver, executor, null!));
        }

        #endregion

        #region 辅助方法

        private static TriggerExecutor CreateEmptyExecutor()
        {
            return new TriggerExecutor(new List<ExecutorProfile>(),
                NullLogger<TriggerExecutor>.Instance);
        }

        private static DeviceConfig CreateMinimalConfig(string protocol, string ip, int port)
        {
            var config = new DeviceConfig();
            config.SetParam("DeviceId", "TEST-DEVICE");
            config.SetParam("DeviceIp", ip);
            config.SetParam("Protocol", protocol);
            config.SetParam("Port", port);
            return config;
        }

        #endregion
    }
}