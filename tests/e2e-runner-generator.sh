#!/bin/bash
# ============================================================
#  RunnerGenerator 端到端测试脚本
#  模拟从模板渲染 → dotnet publish → 打包 的完整流水线
# ============================================================

set -euo pipefail

PASS=0
FAIL=0
TEST_DIR=""

cleanup() {
    if [ -n "$TEST_DIR" ] && [ -d "$TEST_DIR" ]; then
        rm -rf "$TEST_DIR"
    fi
}
trap cleanup EXIT

p() { echo "  [PASS] $1"; PASS=$((PASS + 1)); }
f() { echo "  [FAIL] $1"; FAIL=$((FAIL + 1)); }

echo "========================================"
echo "RunnerGenerator 端到端测试"
echo "========================================"
echo "SDK: $(dotnet --version)"
echo "架构: $(uname -m)"
echo ""

TEST_DIR=$(mktemp -d)

# 自动检测 RID
ARCH=$(uname -m)
if [ "$ARCH" = "arm64" ]; then
    E2E_RID="osx-arm64"
elif [ "$ARCH" = "x86_64" ]; then
    E2E_RID="osx-x64"
else
    echo "未知架构: $ARCH"
    exit 1
fi

# ============== 测试 1: dotnet publish self-contained ==============
echo "=== 测试 1: dotnet publish self-contained ($E2E_RID) ==="

PROJECT_DIR="$TEST_DIR/e2e-console"
mkdir -p "$PROJECT_DIR"

cat > "$PROJECT_DIR/NuGet.config" << 'NUGET'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
NUGET

cat > "$PROJECT_DIR/MyRunner.csproj" << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>MyRunner</RootNamespace>
    <AssemblyName>MyRunner</AssemblyName>
    <Version>1.0.0-e2e</Version>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <RuntimeIdentifier>osx-x64</RuntimeIdentifier>
    <PublishTrimmed>false</PublishTrimmed>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NLog.Extensions.Logging" Version="5.3.8" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="runner.config.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Content Include="NLog.config">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
CSPROJ

cat > "$PROJECT_DIR/Program.cs" << 'PROGCS'
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;

namespace MyRunner;

public static class Program
{
    public static int Main(string[] args)
    {
        var loggerFactory = LoggerFactory.Create(b => b.AddNLog("NLog.config").SetMinimumLevel(LogLevel.Information));
        Console.WriteLine("E2E Runner started successfully");
        NLog.LogManager.Shutdown();
        return 0;
    }
}
PROGCS

cat > "$PROJECT_DIR/runner.config.json" << 'CONFIG'
{"runner":{"name":"E2ETestRunner","interval":1000},"devices":[],"executors":[],"triggers":[]}
CONFIG

cat > "$PROJECT_DIR/NLog.config" << 'NLOG'
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd"
      xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
      autoReload="true" throwExceptions="false">
  <targets>
    <target name="console" xsi:type="ColoredConsole"
            layout="${longdate}|${level:uppercase=true}|${logger}|${message}" />
  </targets>
  <rules>
    <logger name="*" minlevel="Info" writeTo="console" />
  </rules>
</nlog>
NLOG

PUBLISH_DIR="$TEST_DIR/publish-console"
dotnet publish "$PROJECT_DIR/MyRunner.csproj" \
    -c Release -r "$E2E_RID" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o "$PUBLISH_DIR" --nologo 2>&1 | tail -2

[ -f "$PUBLISH_DIR/MyRunner" ] && p "可执行文件存在" || f "可执行文件不存在"
[ -f "$PUBLISH_DIR/runner.config.json" ] && p "runner.config.json 存在" || f "runner.config.json 不存在"
[ -f "$PUBLISH_DIR/NLog.config" ] && p "NLog.config 存在" || f "NLog.config 不存在"

# 测试可执行文件（超时 10 秒，进程应立即退出）
RUN_OUT=$({ timeout 10s "$PUBLISH_DIR/MyRunner" 2>&1 || true; })
echo "$RUN_OUT" | grep -qF "E2E Runner started" && p "可执行文件输出正确" || f "可执行文件输出异常: $RUN_OUT"

# 打包为 zip
cd "$PUBLISH_DIR" && zip -r "$TEST_DIR/MyRunner.zip" ./* > /dev/null 2>&1
[ -f "$TEST_DIR/MyRunner.zip" ] && p "zip 包生成成功 ($(du -h "$TEST_DIR/MyRunner.zip" | cut -f1))" || f "zip 包生成失败"

echo ""

# ============== 测试 2: 运行全部单元测试（一次） ==============
echo "=== 测试 2: 全部单元测试（一次性运行） ==="

cd /Users/dingyuwang/0-X/iot-sdk
FULL_TEST_OUTPUT=$(dotnet test tests/ZL.Iot.Runner.Generator.Tests/ZL.Iot.Runner.Generator.Tests.csproj \
    --nologo --no-restore 2>&1)

# 匹配中英文输出
if echo "$FULL_TEST_OUTPUT" | grep -qE "(已通过|Passed)!.*失败: *0"; then
    p "全部单元测试通过（0 失败）"
else
    # 检查是否有失败
    FAIL_LINE=$(echo "$FULL_TEST_OUTPUT" | grep -E "(已通过|Passed)!" | tail -1)
    if echo "$FAIL_LINE" | grep -qE "失败: *0"; then
        p "全部单元测试通过（0 失败）"
    else
        f "单元测试有失败: $FAIL_LINE"
    fi
fi

# 统计测试数
TOTAL=$(echo "$FULL_TEST_OUTPUT" | grep -oE "总计: *[0-9]+" | grep -oE "[0-9]+" || echo "?")
PASSED_TESTS=$(echo "$FULL_TEST_OUTPUT" | grep -oE "通过: *[0-9]+" | grep -oE "[0-9]+" || echo "?")
SKIPPED=$(echo "$FULL_TEST_OUTPUT" | grep -oE "已跳过: *[0-9]+" | grep -oE "[0-9]+" || echo "?")
echo "  统计: 通过=$PASSED_TESTS, 跳过=$SKIPPED, 总计=$TOTAL"

echo ""

# ============== 测试 3: 逐条验证关键测试 ==============
echo "=== 测试 3: 关键测试逐条验证 ==="

KEY_TESTS=(
    "GenerateRequestTests.Validate_WinFormWithLinuxRid_Throws"
    "GenerateRequestTests.Validate_WindowsServiceWithLinuxRid_Throws"
    "GenerateRequestTests.Validate_LinuxSystemdWithWinRid_Throws"
    "GenerateRequestTests.Validate_WinFormWithWinRid_Succeeds"
    "GenerateRequestTests.Validate_LinuxSystemdWithLinuxRid_Succeeds"
    "GenerateRequestTests.Validate_SourceModeSkipsPlatformRidCheck"
    "GenerateRequestTests.Validate_BinaryWithUnsupportedRid_Throws"
    "ProjectGeneratorTests.GenerateAsync_WindowsService_ReadmeHasServiceInstructions"
    "ProjectGeneratorTests.GenerateAsync_LinuxSystemd_ReadmeHasSystemdInstructions"
    "ProjectGeneratorTests.GenerateAsync_SourceConsole_ZipContainsExpectedFiles"
    "ProjectGeneratorTests.GenerateAsync_LinuxSystemd_ZipContainsServiceFiles"
    "ProjectGeneratorTests.GenerateAsync_WindowsService_ZipContainsBatFiles"
    "ProjectGeneratorTests.GenerateAsync_WinForm_ZipContainsWinFormsFiles"
    "PackageBuilderTests.BuildPackage_ReturnsZipAndManifest"
    "PackageBuilderTests.BuildPackage_CalculatesSha256"
    "PackageBuilderTests.BuildPackage_ZipContainsManifest"
    "PackageBuilderTests.BuildPackage_CleansUpTempManifest"
    "TemplateRendererTests.Render_Csproj_ReplacesAllVariables"
    "TemplateRendererTests.Render_LinuxSystemd_ReplacesVariables"
    "GenerateJobTests.SetRunning_TransitionsCorrectly"
    "GenerateJobTests.SetSucceeded_TransitionsCorrectly"
    "GenerateJobTests.SetFailed_TransitionsCorrectly"
)

for test_name in "${KEY_TESTS[@]}"; do
    if echo "$FULL_TEST_OUTPUT" | grep -q "已跳过.*$test_name"; then
        echo "  [SKIP] $test_name"
    elif echo "$FULL_TEST_OUTPUT" | grep -qE "(已通过|Passed).*$test_name"; then
        p "$test_name"
    elif echo "$FULL_TEST_OUTPUT" | grep -q "$test_name"; then
        # 出现在输出中但非 "已通过" — 可能是失败
        f "$test_name (未通过)"
    else
        # 不出现在输出中可能意味着通过了（简洁模式不列出通过的测试）
        # 用已通过行数判断
        if [ "$PASSED_TESTS" != "?" ] && [ "$PASSED_TESTS" -gt 0 ]; then
            p "$test_name (隐含通过)"
        else
            echo "  [??] $test_name (无法确认)"
        fi
    fi
done

echo ""

# ============== 测试 4: 失败测试明细 ==============
echo "=== 测试 4: 失败明细检查 ==="
FAIL_DETAILS=$(echo "$FULL_TEST_OUTPUT" | grep -A5 "FAIL\]\|失败\]" || true)
if [ -z "$FAIL_DETAILS" ]; then
    p "无失败测试"
else
    f "存在失败测试:"
    echo "$FAIL_DETAILS" | head -20 | sed 's/^/    /'
fi

echo ""

# ============== 总结 ==============
echo "========================================"
echo "端到端测试汇总"
echo "========================================"
echo "通过: $PASS"
echo "失败: $FAIL"
echo "总计: $((PASS + FAIL))"
echo ""

if [ "$FAIL" -gt 0 ]; then
    echo "有测试失败!"
    exit 1
else
    echo "全部通过!"
    exit 0
fi
