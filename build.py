"""
Build script — produces pointoflight.exe via PyInstaller.
Run from the repo root: python build.py
Output: dist/pointoflight.exe
"""
import subprocess
import sys

cmd = [
    sys.executable, "-m", "PyInstaller",
    "--onefile",
    "--name", "pointoflight",
    "--console",
    "--hidden-import", "pymem",
    "--hidden-import", "pygame",
    "--hidden-import", "keyboard",
    "--hidden-import", "inputs",
    "--hidden-import", "win32api",
    "--hidden-import", "win32con",
    "src/main.py",
]

print("Building pointoflight.exe...")
result = subprocess.run(cmd, check=False)
if result.returncode == 0:
    print("\nBuild complete: dist/pointoflight.exe")
else:
    print("\nBuild failed — check output above.")
    sys.exit(result.returncode)
