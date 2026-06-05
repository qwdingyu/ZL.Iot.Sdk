// ============================================================
//  ZL.Iot.Runner.Generator - GenerateResult
//  -------------------------------------------------------------
//  生成结果模型
// ============================================================

using ZL.Iot.Runner.Generator.Core;

namespace ZL.Iot.Runner.Generator.Core.Models;

/// <summary>
/// 生成结果：成功返回 zip 字节流，失败返回错误
/// </summary>
public class GenerateResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 错误信息（Success = false 时）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// zip 字节流（Success = true 时）
    /// </summary>
    public byte[]? ZipBytes { get; set; }

    /// <summary>
    /// zip 文件名（例如 "MyPlc-win-x64.zip"）
    /// </summary>
    public string? ZipFileName { get; set; }

    /// <summary>
    /// 生成耗时
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// 包清单信息（可选）
    /// </summary>
    public PackageManifest? Manifest { get; set; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static GenerateResult Ok(byte[] zipBytes, string zipFileName, TimeSpan elapsed, PackageManifest? manifest = null)
    {
        return new GenerateResult
        {
            Success = true,
            ZipBytes = zipBytes,
            ZipFileName = zipFileName,
            Elapsed = elapsed,
            Manifest = manifest
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static GenerateResult Fail(string errorMessage, TimeSpan elapsed)
    {
        return new GenerateResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Elapsed = elapsed
        };
    }
}
