// ============================================================
//  GenerateResult 单元测试
//  覆盖：Ok/Fail 工厂方法、属性赋值
// ============================================================

using ZL.Iot.Runner.Generator.Core;
using ZL.Iot.Runner.Generator.Core.Models;

namespace ZL.Iot.Runner.Generator.Tests;

public class GenerateResultTests
{
    [Fact]
    public void Ok_CreatesSuccessResult()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var manifest = new PackageManifest { ApplicationName = "Test" };
        var elapsed = TimeSpan.FromSeconds(5);

        var result = GenerateResult.Ok(bytes, "test.zip", elapsed, manifest);

        Assert.True(result.Success);
        Assert.Same(bytes, result.ZipBytes);
        Assert.Equal("test.zip", result.ZipFileName);
        Assert.Equal(elapsed, result.Elapsed);
        Assert.Same(manifest, result.Manifest);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Ok_NullManifest_IsAllowed()
    {
        var result = GenerateResult.Ok(Array.Empty<byte>(), "x.zip", TimeSpan.Zero, null);
        Assert.True(result.Success);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public void Fail_CreatesFailureResult()
    {
        var elapsed = TimeSpan.FromMilliseconds(100);
        var result = GenerateResult.Fail("something broke", elapsed);

        Assert.False(result.Success);
        Assert.Equal("something broke", result.ErrorMessage);
        Assert.Equal(elapsed, result.Elapsed);
        Assert.Null(result.ZipBytes);
        Assert.Null(result.ZipFileName);
        Assert.Null(result.Manifest);
    }
}
