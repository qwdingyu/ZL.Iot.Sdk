// ============================================================
//  PackageBuilder 单元测试
//  覆盖：PackDirectory、BuildPackage、ShouldExclude、
//        SHA256 计算、manifest.json 内容、zip 结构
// ============================================================

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZL.Iot.Runner.Generator.Core;

namespace ZL.Iot.Runner.Generator.Tests;

public class PackageBuilderTests : IDisposable
{
    private readonly string _tempDir;

    public PackageBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PkgBuilderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTestSource(string name = "test")
    {
        var dir = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "app.exe"), "fake-exe-content");
        File.WriteAllText(Path.Combine(dir, "runner.config.json"), "{}");
        File.WriteAllText(Path.Combine(dir, "NLog.config"), "<nlog/>");
        var subDir = Path.Combine(dir, "logs");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, ".gitkeep"), "");
        return dir;
    }

    #region PackDirectory

    [Fact]
    public void PackDirectory_CreatesValidZip()
    {
        var sourceDir = CreateTestSource();
        var zipBytes = PackageBuilder.PackDirectory(sourceDir, "test.zip");

        Assert.NotEmpty(zipBytes);

        // 验证 zip 可正常解压
        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entries = archive.Entries.ToList();
        Assert.NotEmpty(entries);

        var names = entries.Select(e => e.Name).ToList();
        Assert.Contains("app.exe", names);
        Assert.Contains("runner.config.json", names);
        Assert.Contains("NLog.config", names);
    }

    [Fact]
    public void PackDirectory_PreservesFileContent()
    {
        var sourceDir = CreateTestSource();
        var zipBytes = PackageBuilder.PackDirectory(sourceDir, "test.zip");

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var exeEntry = archive.GetEntry("app.exe")!;

        using var reader = new StreamReader(exeEntry.Open());
        var content = reader.ReadToEnd();
        Assert.Equal("fake-exe-content", content);
    }

    [Fact]
    public void PackDirectory_NonExistentDir_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => PackageBuilder.PackDirectory("/nonexistent/dir", "x.zip"));
    }

    [Fact]
    public void PackDirectory_ExcludesPatterns()
    {
        var sourceDir = CreateTestSource();

        // 添加应被排除的文件
        var binDir = Path.Combine(sourceDir, "bin", "Debug");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(binDir, "something.dll"), "dll-content");

        var objDir = Path.Combine(sourceDir, "obj");
        Directory.CreateDirectory(objDir);
        File.WriteAllText(Path.Combine(objDir, "cache"), "obj-content");

        var zipBytes = PackageBuilder.PackDirectory(
            sourceDir, "test.zip", new[] { "bin/", "obj/" });

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var allPaths = archive.Entries.Select(e => e.FullName).ToList();

        Assert.DoesNotContain(allPaths, p => p.Contains("bin/"));
        Assert.DoesNotContain(allPaths, p => p.Contains("obj/"));
        // 正常文件仍在
        Assert.Contains(allPaths, p => p == "app.exe");
    }

    [Fact]
    public void PackDirectory_NullExcludePatterns_DefaultsToEmpty()
    {
        var sourceDir = CreateTestSource();
        // 不应抛出 NullReferenceException
        var zipBytes = PackageBuilder.PackDirectory(sourceDir, "test.zip", excludePatterns: null);
        Assert.NotEmpty(zipBytes);
    }

    #endregion

    #region BuildPackage

    [Fact]
    public void BuildPackage_ReturnsZipAndManifest()
    {
        var sourceDir = CreateTestSource();
        var (zipBytes, manifest) = PackageBuilder.BuildPackage(
            sourceDir, "MyApp", "1.0.0", "Console", "win-x64", selfContained: true);

        Assert.NotEmpty(zipBytes);
        Assert.NotNull(manifest);
        Assert.Equal("MyApp", manifest.ApplicationName);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("Console", manifest.HostType);
        Assert.Equal("win-x64", manifest.RuntimeIdentifier);
        Assert.True(manifest.SelfContained);
        Assert.NotEqual(default(DateTime), manifest.BuildTime);
    }

    [Fact]
    public void BuildPackage_CalculatesSha256()
    {
        var sourceDir = CreateTestSource();
        var (zipBytes, manifest) = PackageBuilder.BuildPackage(
            sourceDir, "MyApp", "1.0.0", "Console", "win-x64", selfContained: true);

        Assert.NotEmpty(manifest.Sha256);
        // SHA256 是 64 位十六进制字符串
        Assert.Equal(64, manifest.Sha256.Length);
        Assert.All(manifest.Sha256, c => Assert.InRange(c, (char)'0', (char)'f'));
    }

    [Fact]
    public void BuildPackage_IncludesFileHashes()
    {
        var sourceDir = CreateTestSource();
        var fileHashes = new Dictionary<string, string>
        {
            ["app.exe"] = "abc123",
            ["config.json"] = "def456"
        };

        var (_, manifest) = PackageBuilder.BuildPackage(
            sourceDir, "MyApp", "1.0.0", "Console", "win-x64", true, fileHashes);

        Assert.Equal(2, manifest.Files.Count);
        Assert.Equal("abc123", manifest.Files["app.exe"]);
        Assert.Equal("def456", manifest.Files["config.json"]);
    }

    [Fact]
    public void BuildPackage_IncludesDeviceSummaries()
    {
        var sourceDir = CreateTestSource();
        var devices = new List<DeviceSummary>
        {
            new() { Code = "plc1", Protocol = "SiemensS7", Ip = "192.168.1.1", TagCount = 5 }
        };

        var (_, manifest) = PackageBuilder.BuildPackage(
            sourceDir, "MyApp", "1.0.0", "Console", "win-x64", true, devices: devices);

        Assert.Single(manifest.Devices);
        Assert.Equal("plc1", manifest.Devices[0].Code);
        Assert.Equal(5, manifest.Devices[0].TagCount);
    }

    [Fact]
    public void BuildPackage_ZipContainsManifest()
    {
        var sourceDir = CreateTestSource();
        var (zipBytes, _) = PackageBuilder.BuildPackage(
            sourceDir, "MyApp", "1.0.0", "Console", "win-x64", true);

        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);

        // 验证 manifest.json 是合法 JSON
        using var reader = new StreamReader(manifestEntry.Open());
        var json = reader.ReadToEnd();
        var parsed = JsonSerializer.Deserialize<PackageManifest>(json);
        Assert.NotNull(parsed);
        Assert.Equal("MyApp", parsed.ApplicationName);
    }

    [Fact]
    public void BuildPackage_CleansUpTempManifest()
    {
        var sourceDir = CreateTestSource();
        Assert.False(Directory.GetFiles(sourceDir).Any(f => f.EndsWith("manifest.json")));

        PackageBuilder.BuildPackage(sourceDir, "MyApp", "1.0.0", "Console", "win-x64", true);

        // 临时 manifest.json 应已被清理
        Assert.False(File.Exists(Path.Combine(sourceDir, "manifest.json")));
    }

    [Fact]
    public void BuildPackage_Sha256IsLowercaseHex()
    {
        var sourceDir = CreateTestSource();
        var (_, manifest) = PackageBuilder.BuildPackage(
            sourceDir, "MyApp", "1.0.0", "Console", "win-x64", true);

        Assert.Equal(manifest.Sha256, manifest.Sha256.ToLowerInvariant());
    }

    #endregion

    #region 可重复性

    [Fact]
    public void PackDirectory_SameContent_ProducesSameHash()
    {
        var sourceDir = CreateTestSource();

        var bytes1 = PackageBuilder.PackDirectory(sourceDir, "test.zip");
        var bytes2 = PackageBuilder.PackDirectory(sourceDir, "test.zip");

        var hash1 = Convert.ToHexString(SHA256.HashData(bytes1));
        var hash2 = Convert.ToHexString(SHA256.HashData(bytes2));

        // 同一目录两次打包应产生相同内容（同一进程内时间戳不影响文件内容）
        Assert.Equal(hash1, hash2);
    }

    #endregion
}
