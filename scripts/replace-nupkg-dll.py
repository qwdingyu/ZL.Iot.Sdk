#!/usr/bin/env python3
"""替换 nupkg 中指定 TFM 的 DLL（用于混淆后的注入）"""
import zipfile, os, shutil, sys

nupkg = sys.argv[1]
dll_path = sys.argv[2]
tfm = sys.argv[3] if len(sys.argv) > 2 else "net8.0"

dll_name = os.path.basename(dll_path)
tmp = nupkg + ".tmp"

with zipfile.ZipFile(nupkg, "r") as zin:
    target_entry = f"lib/{tfm}/{dll_name}"
    found = False
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            if item.filename == target_entry:
                zout.writestr(item, open(dll_path, "rb").read())
                found = True
            else:
                zout.writestr(item, zin.read(item.filename))
    if not found:
        # 尝试模糊匹配（可能 TFM 不完全匹配）
        for item in zin.infolist():
            if item.filename.endswith(f"/{dll_name}"):
                print(f"  ⚠️ 未精确匹配 {target_entry}，但已找到 {item.filename}，跳过替换")
                break

shutil.move(tmp, nupkg)
print(f"  ✅ Replaced {target_entry}")
