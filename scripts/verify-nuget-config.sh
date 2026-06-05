#!/usr/bin/env bash
# ============================================================
# verify-nuget-config.sh — NuGet.config 合规性验证脚本
#
# 背景: 2026-06-05 发现 NuGet.config 中使用 $(MSBuildThisFileDirectory)
#       导致 Grpc.Tools.targets 未被导入，47 个 proto 编译错误。
#
# 用法: bash scripts/verify-nuget-config.sh [项目根目录]
#   默认检查当前目录。退出码 0 = 通过，1 = 发现违规。
#
# 检查项:
#   1. MSBuild 属性检测 — NuGet.config 中禁止使用 $(...) 语法
#   2. 配置文件唯一性 — 禁止同时存在 NuGet.config 和 nuget.config
#   3. globalPackagesFolder 合理性 — 如果设置了，值不能包含 MSBuild 属性
#   4. 生成的 .nuget.g.props 路径有效性 — NuGetPackageRoot 必须指向真实目录
# ============================================================
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="${1:-$(cd "$SCRIPT_DIR/.." && pwd)}"

PASS=0
FAIL=0
WARN=0

pass() { echo "  ✅ PASS  $*"; PASS=$((PASS + 1)); }
fail() { echo "  ❌ FAIL  $*"; FAIL=$((FAIL + 1)); }
warn() { echo "  ⚠️  WARN  $*"; WARN=$((WARN + 1)); }

echo "========================================"
echo " NuGet.config 合规性验证"
echo " 项目: $PROJECT_ROOT"
echo " 时间: $(date '+%Y-%m-%d %H:%M:%S')"
echo "========================================"
echo ""

# ------------------------------------------------------------------
# 检查 1: 定位 NuGet 配置文件
# ------------------------------------------------------------------
echo "[检查 1] 配置文件定位"

# On case-insensitive filesystems (macOS HFS+/APFS, Windows NTFS),
# NuGet.config, nuget.config, NuGet.Config all resolve to the same inode.
# We detect actual duplicates by comparing device+inode.
CONFIG_FILES=()
SEEN_INODES=()
for f in "NuGet.config" "nuget.config" "NuGet.Config"; do
    if [ -f "$PROJECT_ROOT/$f" ]; then
        INODE=$(stat -f '%d:%i' "$PROJECT_ROOT/$f" 2>/dev/null || stat -c '%d:%i' "$PROJECT_ROOT/$f" 2>/dev/null || echo "unknown")
        ALREADY_SEEN=0
        for si in "${SEEN_INODES[@]+"${SEEN_INODES[@]}"}"; do
            if [ "$si" = "$INODE" ]; then
                ALREADY_SEEN=1
                break
            fi
        done
        if [ $ALREADY_SEEN -eq 0 ]; then
            CONFIG_FILES+=("$f")
            SEEN_INODES+=("$INODE")
        fi
    fi
done

if [ ${#CONFIG_FILES[@]} -eq 0 ]; then
    warn "未发现 NuGet 配置文件（可能使用默认全局配置）"
    echo ""
    echo "========================================"
    echo " 结果: $PASS 通过, $FAIL 失败, $WARN 警告"
    echo "========================================"
    exit 0
fi

if [ ${#CONFIG_FILES[@]} -gt 1 ]; then
    fail "发现 ${#CONFIG_FILES[@]} 个不同的 NuGet 配置文件: ${CONFIG_FILES[*]}"
    fail "只保留 NuGet.config，删除其他变体"
else
    pass "发现唯一配置文件: ${CONFIG_FILES[0]}"
fi

NUGET_CONFIG="$PROJECT_ROOT/${CONFIG_FILES[0]}"

echo ""

# ------------------------------------------------------------------
# 检查 2: MSBuild 属性检测（核心检查）
# ------------------------------------------------------------------
echo "[检查 2] MSBuild 属性检测（核心）"

# 匹配 $(...) 模式，排除 XML 注释中的内容
# 用 perl 删除多行 XML 注释 <!-- ... -->，然后检查剩余内容
NON_COMMENT_LINES=$(perl -0777 -pe 's/<!--.*?-->//gs' "$NUGET_CONFIG" 2>/dev/null || sed ':a;/<!--/!b;n;/-->/d;ba' "$NUGET_CONFIG" | sed 's/<!--.*//')

if echo "$NON_COMMENT_LINES" | grep -qE '\$\([A-Za-z_]'; then
    fail "NuGet.config 中包含 MSBuild 属性引用 \$(...) 语法"
    echo "$NON_COMMENT_LINES" | grep -nE '\$\([A-Za-z_]' | while read -r line; do
        echo "      → $line"
    done
    echo ""
    echo "      原因: NuGet.config 由 NuGet.exe/dotnet-cli 解析，不支持 MSBuild 属性。"
    echo "      后果: NuGetPackageRoot 包含字面量 \$(...)，导致 package targets 的 Exists() 失败。"
    echo "      修复: 使用相对路径（如 .nuget/packages）或删除 globalPackagesFolder。"
else
    pass "未发现 MSBuild 属性引用"
fi

echo ""

# ------------------------------------------------------------------
# 检查 3: globalPackagesFolder 值合理性
# ------------------------------------------------------------------
echo "[检查 3] globalPackagesFolder 配置"

GPF_VALUE=$(perl -ne 'if (/globalPackagesFolder.*?value="([^"]+)"/) { print $1 }' "$NUGET_CONFIG" 2>/dev/null || true)

if [ -z "$GPF_VALUE" ]; then
    pass "未设置 globalPackagesFolder（使用默认全局缓存 ~/.nuget/packages/）"
else
    # 检查是否包含 MSBuild 属性
    if echo "$GPF_VALUE" | grep -qE '\$\('; then
        fail "globalPackagesFolder 值 '$GPF_VALUE' 包含 MSBuild 属性"
    # 检查是否为相对路径
    elif [[ "$GPF_VALUE" == ./* ]] || [[ "$GPF_VALUE" == ../* ]] || [[ "$GPF_VALUE" == .* ]]; then
        pass "globalPackagesFolder 使用相对路径: $GPF_VALUE"
        # 验证相对路径是否存在
        RESOLVED="$PROJECT_ROOT/$GPF_VALUE"
        if [ -d "$RESOLVED" ]; then
            pass "相对路径解析为有效目录: $RESOLVED"
        else
            warn "相对路径 '$GPF_VALUE' 解析为 '$RESOLVED'，该目录不存在（首次 restore 后会自动创建）"
        fi
    # 检查是否为绝对路径
    elif [[ "$GPF_VALUE" == /* ]]; then
        if [ -d "$GPF_VALUE" ]; then
            pass "globalPackagesFolder 使用绝对路径: $GPF_VALUE"
        else
            fail "globalPackagesFolder 绝对路径 '$GPF_VALUE' 不存在"
        fi
    else
        warn "globalPackagesFolder 值 '$GPF_VALUE' 格式非常规"
    fi
fi

echo ""

# ------------------------------------------------------------------
# 检查 4: 生成的 .nuget.g.props 路径有效性（如果存在）
# ------------------------------------------------------------------
echo "[检查 4] 生成的 NuGet 文件路径有效性"

FOUND_GPROPS=0
for gprops in $(timeout 5 find "$PROJECT_ROOT" -maxdepth 5 -path "*/obj/*.csproj.nuget.g.props" 2>/dev/null | head -5); do
    FOUND_GPROPS=1
    NUGET_ROOT=$(perl -ne 'if (/<NuGetPackageRoot[^>]*>\s*([^<\s]+)\s*<\/NuGetPackageRoot>/) { print $1; exit }' "$gprops" 2>/dev/null || true)

    if [ -z "$NUGET_ROOT" ]; then
        warn "无法从 $(basename "$gprops") 提取 NuGetPackageRoot"
        continue
    fi

    # 检查是否包含字面量 MSBuild 属性
    if echo "$NUGET_ROOT" | grep -qF '$('; then
        fail "$(basename "$gprops"): NuGetPackageRoot 包含字面量 MSBuild 属性: $NUGET_ROOT"
        echo "      → 需要删除 obj/ 目录并重新 dotnet restore"
    elif [ -d "$NUGET_ROOT" ]; then
        pass "$(basename "$gprops"): NuGetPackageRoot 指向有效目录"
    else
        warn "$(basename "$gprops"): NuGetPackageRoot '$NUGET_ROOT' 不是有效目录"
    fi
done

if [ $FOUND_GPROPS -eq 0 ]; then
    warn "未发现生成的 .nuget.g.props 文件（需要先执行 dotnet restore）"
fi

echo ""

# ------------------------------------------------------------------
# 检查 5: proto 代码生成验证（如果项目有 .proto 文件）
# ------------------------------------------------------------------
echo "[检查 5] Proto 代码生成验证"

PROTO_COUNT=$(timeout 5 find "$PROJECT_ROOT" -maxdepth 6 -name "*.proto" -not -path "*/obj/*" -not -path "*/.nuget/*" 2>/dev/null | wc -l)

if [ "$PROTO_COUNT" -gt 0 ]; then
    # 查找包含 Protobuf Include 的 csproj
    PROTO_PROJECTS=$(grep -rl '<Protobuf Include' "$PROJECT_ROOT" --include="*.csproj" 2>/dev/null || true)

    if [ -n "$PROTO_PROJECTS" ]; then
        while IFS= read -r csproj; do
            PROJ_DIR=$(dirname "$csproj")
            GEN_FILES=$(find "$PROJ_DIR/obj" -name "*.cs" -path "*Protos*" 2>/dev/null | wc -l)

            if [ "$GEN_FILES" -gt 0 ]; then
                pass "$(basename "$PROJ_DIR"): proto 已生成 $GEN_FILES 个 .cs 文件"
            else
                warn "$(basename "$PROJ_DIR"): 有 .proto 引用但未发现生成的 .cs 文件"
                echo "      → 运行 'dotnet build' 触发 proto 代码生成"
            fi
        done <<< "$PROTO_PROJECTS"
    else
        warn "发现 $PROTO_COUNT 个 .proto 文件但未找到引用它们的 csproj"
    fi
else
    pass "项目中无 .proto 文件，跳过 proto 验证"
fi

echo ""

# ------------------------------------------------------------------
# 汇总
# ------------------------------------------------------------------
echo "========================================"
echo " 结果: $PASS 通过, $FAIL 失败, $WARN 警告"
echo "========================================"

if [ $FAIL -gt 0 ]; then
    echo ""
    echo " ❌ 验证失败！请修复上述问题后重新运行。"
    echo "    快速修复: 从 NuGet.config 中移除 globalPackagesFolder 配置"
    exit 1
fi

if [ $WARN -gt 0 ]; then
    echo ""
    echo " ⚠️  验证通过但有警告，建议检查。"
    exit 0
fi

echo ""
echo " ✅ 所有检查通过！"
exit 0
