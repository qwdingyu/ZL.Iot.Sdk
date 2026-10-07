using Serilog.Events;

namespace ZL.Shared.Tests;

/// <summary>
/// StructuredLog 引导与写入的端到端回归锁（此前该静态类零覆盖）。
/// 通过向临时 BaseDirectory 写 logging.json 注入 LogRoot/级别/Payload 上限，
/// 全程不碰真实用户目录；Async sink 在 Shutdown 时 flush，故断言都在 Shutdown 之后。
/// 注意：StructuredLog 是静态单例，xUnit 同一类内串行执行，每个用例先 Shutdown 复位。
/// </summary>
public class StructuredLogTests
{
    private static (string baseDir, string logRoot) Prepare(string settingsJson)
    {
        string baseDir = Path.Combine(Path.GetTempPath(), "zl-shared-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDir);
        string logRoot = Path.Combine(baseDir, "logs");
        string settings = settingsJson.Replace("__LOGROOT__", logRoot.Replace("\\", "\\\\"));
        File.WriteAllText(Path.Combine(baseDir, "logging.json"), settings);
        return (baseDir, logRoot);
    }

    private static string[] InstanceLogLines(string logRoot, string instance)
    {
        string dir = Path.Combine(logRoot, "instances");
        string? file = Directory.Exists(dir)
            ? Directory.GetFiles(dir, $"sim-{instance}-*.log").FirstOrDefault()
            : null;
        return file is null ? Array.Empty<string>() : File.ReadAllLines(file);
    }

    [Fact]
    public void 引导_写入_停机_产生实例日志文件()
    {
        var (baseDir, logRoot) = Prepare("""{ "LogRoot": "__LOGROOT__", "MinimumLevel": "Debug" }""");
        try
        {
            StructuredLog.Shutdown();
            StructuredLog.Initialize(new LogBootstrapOptions { BaseDirectory = baseDir, EnableConsole = false });
            StructuredLog.Write(new LogEventMetadata { Instance = "Device1", Direction = "RX" },
                LogEventLevel.Information, "read tag {Tag}", "T1");
            StructuredLog.Shutdown();

            string[] lines = InstanceLogLines(logRoot, "Device1");
            Assert.NotEmpty(lines);
            Assert.Contains(lines, l => l.Contains("[INF]") && l.Contains("[RX]") && l.Contains("read tag T1"));
        }
        finally
        {
            StructuredLog.Shutdown();
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void 超长Payload_记录原始长度()
    {
        var (baseDir, logRoot) = Prepare("""{ "LogRoot": "__LOGROOT__", "PayloadMaxLength": 20 }""");
        try
        {
            StructuredLog.Shutdown();
            StructuredLog.Initialize(new LogBootstrapOptions { BaseDirectory = baseDir });
            StructuredLog.Write(new LogEventMetadata { Instance = "Trunc", Payload = new string('P', 100) },
                LogEventLevel.Information, "payload");
            StructuredLog.Shutdown();

            string[] lines = InstanceLogLines(logRoot, "Trunc");
            Assert.NotEmpty(lines);
            // 输出模板含 [L:{PayloadOriginalLength}]：截断时必须留下原始长度 100
            Assert.Contains(lines, l => l.Contains("[L:100]"));
        }
        finally
        {
            StructuredLog.Shutdown();
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void 级别过滤_MinimumLevel为Error时Information不落盘()
    {
        var (baseDir, logRoot) = Prepare("""{ "LogRoot": "__LOGROOT__", "MinimumLevel": "Error" }""");
        try
        {
            StructuredLog.Shutdown();
            StructuredLog.Initialize(new LogBootstrapOptions { BaseDirectory = baseDir });
            StructuredLog.Write(new LogEventMetadata { Instance = "Level" },
                LogEventLevel.Information, "should not appear");
            StructuredLog.Write(new LogEventMetadata { Instance = "Level" },
                LogEventLevel.Error, "should appear");
            StructuredLog.Shutdown();

            string[] lines = InstanceLogLines(logRoot, "Level");
            Assert.NotEmpty(lines);
            Assert.Contains(lines, l => l.Contains("[ERR]") && l.Contains("should appear"));
            Assert.DoesNotContain(lines, l => l.Contains("should not appear"));
        }
        finally
        {
            StructuredLog.Shutdown();
            Directory.Delete(baseDir, recursive: true);
        }
    }

    [Fact]
    public void 未初始化时Write静默不抛()
    {
        StructuredLog.Shutdown();
        // 不 Initialize，直接 Write：契约是不抛、不落盘。
        // 两个重载共用同一个守卫（!_initialized || payload==null || metadata==null），
        // payload 传 null 与未初始化走的是同一条静默返回路径。
        StructuredLog.Write(new LogEventMetadata { Instance = "Never" }, LogEventLevel.Error, "no-op");
        StructuredLog.Write(null!, new LogEventMetadata { Instance = "Never" });
        StructuredLog.Shutdown();
    }
}
