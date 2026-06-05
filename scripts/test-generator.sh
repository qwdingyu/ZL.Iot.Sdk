#!/bin/bash
# ============================================================
#  ZL.Iot.Runner.Generator 测试脚本
#  每次变动后运行此脚本确保质量
#  用法: bash iot-sdk/test-generator.sh
# ============================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR"
TEST_PROJECT="$PROJECT_DIR/ZL.Iot.Runner.Generator.Tests/ZL.Iot.Runner.Generator.Tests.csproj"

echo "============================================"
echo "  ZL.Iot.Runner.Generator 测试流水线"
echo "============================================"
echo ""

# Step 1: 清理构建输出
echo "[1/4] 清理构建输出..."
rm -rf "$PROJECT_DIR/ZL.Iot.Runner.Templates/bin" \
       "$PROJECT_DIR/ZL.Iot.Runner.Templates/obj" \
       "$PROJECT_DIR/ZL.Iot.Runner.Generator.Tests/bin" \
       "$PROJECT_DIR/ZL.Iot.Runner.Generator.Tests/obj"
echo "      完成"
echo ""

# Step 2: 构建测试项目（包含所有依赖）
echo "[2/4] 构建测试项目..."
cd "$PROJECT_DIR"
dotnet build "$TEST_PROJECT" -v:quiet -m:1
echo "      完成"
echo ""

# Step 3: 运行测试
echo "[3/4] 运行测试..."
dotnet test "$TEST_PROJECT" --no-build \
    --logger "console;verbosity=normal" \
    --results-directory "$PROJECT_DIR/test-results" \
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=trx
TEST_EXIT=$?
echo ""

# Step 4: 汇总
echo "[4/4] 汇总"
echo "============================================"
if [ $TEST_EXIT -eq 0 ]; then
    echo "  ✅ 所有测试通过"
    echo "============================================"
    exit 0
else
    echo "  ❌ 部分测试失败，请查看上方详情"
    echo "============================================"
    exit $TEST_EXIT
fi
