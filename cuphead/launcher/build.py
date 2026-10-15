"""
Builds dist/oricuphead.exe — the launcher Melty runs — with PyInstaller.
Run from the repo root on Windows: python cuphead/launcher/build.py

Needs, next to this file in payload/:
  OriCuphead.dll    the retargeted plugin (see .github/workflows/build.yml)
  bepinex_x64.zip   BepInEx 5 for 64-bit Cuphead
  bepinex_x86.zip   BepInEx 5 for 32-bit Cuphead
"""
import subprocess
import sys
from pathlib import Path

here = Path(__file__).resolve().parent
payload = here / "payload"
needed = ["OriCuphead.dll", "bepinex_x64.zip", "bepinex_x86.zip"]
missing = [n for n in needed if not (payload / n).is_file()]
if missing:
    sys.exit(f"Missing from {payload}: {', '.join(missing)}")

sep = ";" if sys.platform == "win32" else ":"
cmd = [sys.executable, "-m", "PyInstaller", "--onefile", "--console", "--name", "oricuphead"]
for n in needed:
    cmd += ["--add-data", f"{payload / n}{sep}."]
cmd.append(str(here / "oricuphead.py"))

print("Building oricuphead.exe...")
sys.exit(subprocess.run(cmd, check=False).returncode)
