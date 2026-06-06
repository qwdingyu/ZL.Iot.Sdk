// ============================================================
//  RunnerConfig / DeviceProfile / TagProfile / ExecutorProfile 单元测试
//  覆盖：默认值、属性赋值、列表初始化
// ============================================================

using System.Collections.Generic;
using ZL.Iot.Runner.Configuration;

namespace ZL.Iot.Runner.Tests
{
    /// <summary>
    /// 配置模型类单元测试
    /// 覆盖：
    /// 1. 默认值正确
    /// 2. 属性赋值
    /// 3. 列表初始非空
    /// 4. 多设备/多标签/多执行器场景
    /// </summary>
    public class RunnerConfigTests
    {
        [Fact]
        public void RunnerConfig_Default_HasCorrectDefaults()
        {
            var config = new RunnerConfig();
            Assert.NotNull(config.Runner);
            Assert.NotNull(config.Devices);
            Assert.Empty(config.Devices);
        }

        [Fact]
        public void RunnerOptions_Default_HasCorrectDefaults()
        {
            var options = new RunnerOptions();
            Assert.Equal("ZL.Iot.Runner", options.Name);
            Assert.Equal("Information", options.LogLevel);
            Assert.NotNull(options.DataStorage);
        }

        [Fact]
        public void DataStorageOptions_Default_UsesSqlite()
        {
            var storage = new DataStorageOptions();
            Assert.Equal("Sqlite", storage.Type);
            Assert.Equal("Data Source=./data/iot_runner.db", storage.ConnectionString);
        }

        [Fact]
        public void DeviceProfile_Default_HasCorrectDefaults()
        {
            var device = new DeviceProfile();
            Assert.Equal("", device.Code);
            Assert.Equal("SiemensS7", device.Protocol);
            Assert.Equal("192.168.1.100", device.Ip);
            Assert.Equal(102, device.Port);
            Assert.Equal(0, device.Rack);
            Assert.Equal(1, device.Slot);
            Assert.Equal(5000, device.ConnectTimeout);
            Assert.Equal(200, device.ReadInterval);
            Assert.NotNull(device.Tags);
            Assert.NotNull(device.Executors);
        }

        [Fact]
        public void TagProfile_Default_HasCorrectDefaults()
        {
            var tag = new TagProfile();
            Assert.Equal("", tag.Id);
            Assert.Equal("", tag.Description);
            Assert.Equal("", tag.Address);
            Assert.Equal("bool", tag.DataType);
            Assert.Equal(1, tag.Length);
            Assert.Equal("ASCII", tag.StringEncoding);
            Assert.True(tag.Enable);
            Assert.Equal("", tag.TagType);
            Assert.Equal(0, tag.Deadband);
            Assert.Equal(0, tag.ScanRate);
        }

        [Fact]
        public void ExecutorProfile_Default_HasCorrectDefaults()
        {
            var exe = new ExecutorProfile();
            Assert.Equal("", exe.BizCode);
            Assert.Equal("", exe.TagId);
            Assert.Equal(0, exe.JudgeType);
            Assert.Equal("", exe.JudgeExp);
            Assert.Equal("M", exe.ExeType);
            Assert.Equal("", exe.Script);
            Assert.Equal(0, exe.ExeOrder);
            Assert.True(exe.Enable);
        }

        [Fact]
        public void RunnerConfig_CanAddMultipleDevices()
        {
            var config = new RunnerConfig();
            config.Devices.Add(new DeviceProfile { Code = "d1" });
            config.Devices.Add(new DeviceProfile { Code = "d2" });
            config.Devices.Add(new DeviceProfile { Code = "d3" });

            Assert.Equal(3, config.Devices.Count);
            Assert.Equal("d1", config.Devices[0].Code);
            Assert.Equal("d2", config.Devices[1].Code);
            Assert.Equal("d3", config.Devices[2].Code);
        }

        [Fact]
        public void DeviceProfile_CanAddMultipleTagsAndExecutors()
        {
            var device = new DeviceProfile { Code = "d1" };
            for (int i = 0; i < 5; i++)
            {
                device.Tags.Add(new TagProfile { Id = $"T{i}" });
                device.Executors.Add(new ExecutorProfile { BizCode = $"E{i}", TagId = $"T{i}" });
            }

            Assert.Equal(5, device.Tags.Count);
            Assert.Equal(5, device.Executors.Count);
        }
    }
}