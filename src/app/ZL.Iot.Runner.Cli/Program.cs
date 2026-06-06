// ============================================================
//  ZL.Iot.Runner.Cli
//  -------------------------------------------------------------
//  开发期演示 CLI：加载 runner.config.json/xml → 起 Runner → 阻塞到 Ctrl+C。
//  商业化产物走 ZL.Iot.Runner.Generator 生成的"瘦壳"宿主，
//  本 CLI 仅作为"开发者本地直接跑 Runner"的入口。
// ============================================================

using Microsoft.Extensions.Logging;
using ZL.Iot.Runner.Configuration;
using ZL.Iot.Runner.Runtime;

namespace ZL.Iot.Runner.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var configPath = ResolveConfigPath(args);

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = false;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });
        var logger = loggerFactory.CreateLogger("ZL.Iot.Runner.Cli");

        logger.LogInformation("========================================");
        logger.LogInformation("ZL.Iot.Runner.Cli 启动（开发期演示入口）");
        logger.LogInformation("配置文件: {ConfigPath}", configPath);
        logger.LogInformation("========================================");

        // 加载配置
        RunnerConfig config;
        try
        {
            config = ConfigLoader.Load(configPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "配置加载失败");
            return 1;
        }

        // 创建 Runner（薄壳：直接调 Run()）
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (s, e) => cts.Cancel();

        try
        {
            var runner = new DeviceRunner(config, loggerFactory);
            runner.Run(cts.Token);  // 阻塞到 Ctrl+C
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Runner 异常退出");
            return 2;
        }

        logger.LogInformation("ZL.Iot.Runner.Cli 优雅退出");
        return 0;
    }

    /// <summary>
    /// 解析配置文件路径
    /// 优先级：命令行参数 > ./runner.config.json > ./runner.config.xml
    /// </summary>
    private static string ResolveConfigPath(string[] args)
    {
        if (args != null && args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            return args[0];
        }

        foreach (var candidate in new[] { "./runner.config.json", "./runner.config.xml" })
        {
            if (File.Exists(candidate)) return candidate;
        }

        return "./runner.config.json";  // 找不到任何配置时返回默认（让 ConfigLoader 抛 FileNotFoundException）
    }
}
