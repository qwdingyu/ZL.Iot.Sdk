#!/usr/bin/env python3
"""验证 iot-sdk / 消费者项目的引用边界。

规则目标：
1. iot-sdk 仓库内部项目互相引用必须使用 ProjectReference。
2. 跨仓库引用 ZL.PlcBase / iot-sdk 必须使用 local-feed NuGet 包。
3. 禁止重新引入 ZL.Tag 包，Tag 类型由 ZL.IotHub 承载。
4. NuGet 源统一从 local-feed 优先，再回退到 nuget.org，禁止 artifacts 目录作为包源。
"""

from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_EXTERNAL_SOURCE_ROOTS = (
    Path("/Users/dingyuwang/0-X/iot-sdk"),
    Path("/Users/dingyuwang/0-X/ZL.PlcBase"),
)
ALLOWED_EXTERNAL_ZL_PACKAGES = {
    "ZL.IotHub",
    "ZL.IotHub.Bridges",
    "ZL.PFLite",
    "ZL.Iot.Runner.Templates",
}
FORBIDDEN_ZL_PACKAGES = {
    "ZL.Tag",
}
EXCLUDED_DIRS = {
    ".git",
    ".omx",
    ".dotnet-tools",
    ".atomcode",
    ".qwen",
    "artifacts",
    "bin",
    "obj",
    "docs",
}


@dataclass(frozen=True)
class Problem:
    path: Path
    message: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="验证 ProjectReference / PackageReference / NuGet.config 边界规则"
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=REPO_ROOT,
        help="要检查的项目根目录，默认是当前 iot-sdk 仓库",
    )
    parser.add_argument(
        "--mode",
        choices=("repo", "consumer"),
        default="repo",
        help="repo 检查 iot-sdk 自身；consumer 检查 UseThink/tmom 等消费者",
    )
    parser.add_argument(
        "--external-source-root",
        type=Path,
        action="append",
        default=[],
        help="consumer 模式下禁止 ProjectReference 指向的外部源码根目录",
    )
    return parser.parse_args()


def is_excluded(path: Path) -> bool:
    return any(part in EXCLUDED_DIRS for part in path.parts)


def find_files(root: Path, pattern: str) -> list[Path]:
    return sorted(path for path in root.rglob(pattern) if not is_excluded(path.relative_to(root)))


def load_internal_package_ids(root: Path) -> set[str]:
    pipeline = root / "pipeline.json"
    if not pipeline.exists():
        return set()

    data = json.loads(pipeline.read_text(encoding="utf-8"))
    return {
        project["name"]
        for project in data.get("projects", [])
        if isinstance(project, dict) and project.get("name")
    }


def parse_xml(path: Path) -> ET.Element | None:
    try:
        return ET.parse(path).getroot()
    except ET.ParseError as exc:
        raise RuntimeError(f"{path}: XML 解析失败: {exc}") from exc


def iter_items(root: ET.Element, tag: str) -> list[ET.Element]:
    return [item for item in root.iter() if item.tag.endswith(tag)]


def check_project_references(
    project_file: Path,
    project_root: Path,
    mode: str,
    external_source_roots: tuple[Path, ...],
) -> list[Problem]:
    problems: list[Problem] = []
    root = parse_xml(project_file)
    if root is None:
        return problems

    for item in iter_items(root, "ProjectReference"):
        include = item.attrib.get("Include", "")
        if not include:
            continue

        target = (project_file.parent / include).resolve()
        if mode == "repo" and not target.is_relative_to(project_root.resolve()):
            problems.append(
                Problem(
                    project_file,
                    f"跨仓库 ProjectReference 被禁止: {include}。请改为 PackageReference + local-feed。",
                )
            )

        if mode == "consumer":
            for external_root in external_source_roots:
                resolved_external_root = external_root.resolve()
                if target.is_relative_to(resolved_external_root):
                    problems.append(
                        Problem(
                            project_file,
                            f"消费者禁止引用外部源码项目: {include}。请改为 PackageReference。",
                        )
                    )
                    break

    return problems


def check_package_references(
    project_file: Path,
    mode: str,
    internal_package_ids: set[str],
) -> list[Problem]:
    problems: list[Problem] = []
    root = parse_xml(project_file)
    if root is None:
        return problems

    for item in iter_items(root, "PackageReference"):
        package_id = item.attrib.get("Include", "")
        if not package_id:
            continue

        if "Version" in item.attrib:
            problems.append(
                Problem(
                    project_file,
                    f"PackageReference 不允许写 Version: {package_id}。版本必须在 Directory.Packages.props 管理。",
                )
            )

        if package_id in FORBIDDEN_ZL_PACKAGES:
            problems.append(
                Problem(
                    project_file,
                    f"禁止直接引用 {package_id}。Tag 类型由 ZL.IotHub 承载，避免 CS0433。",
                )
            )

        if mode == "repo" and package_id in internal_package_ids:
            problems.append(
                Problem(
                    project_file,
                    f"仓库内部包 {package_id} 不允许 PackageReference。请改为 ProjectReference。",
                )
            )

        if mode == "repo" and package_id.startswith("ZL."):
            is_internal = package_id in internal_package_ids
            is_allowed_external = package_id in ALLOWED_EXTERNAL_ZL_PACKAGES
            is_forbidden = package_id in FORBIDDEN_ZL_PACKAGES
            if not is_internal and not is_allowed_external and not is_forbidden:
                problems.append(
                    Problem(
                        project_file,
                        f"未知 ZL.* 外部包 {package_id}。请先在边界规范中登记允许原因。",
                    )
                )

    return problems


def check_nuget_config(config_file: Path) -> list[Problem]:
    problems: list[Problem] = []
    text = config_file.read_text(encoding="utf-8")
    if "~/" in text:
        problems.append(
            Problem(config_file, "NuGet.config 不展开 ~/，local-feed 必须使用绝对路径。")
        )
    if "$(" in text:
        problems.append(
            Problem(config_file, "NuGet.config 不支持 MSBuild 属性语法 $()。")
        )

    root = parse_xml(config_file)
    if root is None:
        return problems

    sources: list[tuple[str, str]] = []
    for add in iter_items(root, "add"):
        key = add.attrib.get("key", "")
        value = add.attrib.get("value", "")
        if key and value:
            sources.append((key, value))

    for key, value in sources:
        normalized = value.replace("\\", "/")
        if "/artifacts" in normalized or normalized.endswith("artifacts"):
            problems.append(
                Problem(
                    config_file,
                    f"禁止把构建 artifacts 目录作为 NuGet 源: {key}={value}。请统一推到 ~/.nuget/local-feed。",
                )
            )

    keys = [key for key, _ in sources]
    if "local-feed" in keys and "nuget.org" in keys:
        if keys.index("local-feed") > keys.index("nuget.org"):
            problems.append(
                Problem(config_file, "local-feed 必须排在 nuget.org 前面，确保本地开发包优先命中。")
            )
    elif sources:
        problems.append(
            Problem(config_file, "NuGet 源必须同时包含 local-feed 和 nuget.org。")
        )

    return problems


def main() -> int:
    args = parse_args()
    root = args.root.resolve()
    external_source_roots = tuple(args.external_source_root) or DEFAULT_EXTERNAL_SOURCE_ROOTS
    internal_package_ids = load_internal_package_ids(root) if args.mode == "repo" else set()

    problems: list[Problem] = []
    for project_file in find_files(root, "*.csproj"):
        problems.extend(
            check_project_references(project_file, root, args.mode, external_source_roots)
        )
        problems.extend(check_package_references(project_file, args.mode, internal_package_ids))

    for config_file in find_files(root, "NuGet.config"):
        problems.extend(check_nuget_config(config_file))

    if problems:
        print("引用边界检查失败：")
        for problem in problems:
            print(f"  - {problem.path.relative_to(root)}: {problem.message}")
        return 1

    checked_projects = len(find_files(root, "*.csproj"))
    checked_configs = len(find_files(root, "NuGet.config"))
    print(
        f"引用边界检查通过：{root}，模式={args.mode}，"
        f"项目={checked_projects}，NuGet.config={checked_configs}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
