"""
Point of Light: Ori in Cuphead — launcher.

Melty calls:
  oricuphead.exe [--cuphead-path <path>]

What it does:
  1. Finds Cuphead (the path Melty passes, else Steam / GOG / common folders).
  2. Installs BepInEx 5 into the Cuphead folder if it isn't there yet
     (32- or 64-bit, matching Cuphead.exe).
  3. Copies the Ori plugin into BepInEx/plugins/OriCuphead/.
  4. Starts Cuphead and waits for it to close.
  5. Removes the Ori plugin again, so starting Cuphead outside Melty is vanilla.
     BepInEx itself stays installed; with no plugins it changes nothing.

The plugin DLL and both BepInEx zips are bundled into the exe by build.py.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import string
import struct
import subprocess
import sys
import time
import zipfile
from pathlib import Path

TAG = "[Point of Light]"
STEAM_APP_ID = "268910"
PLUGIN_DLL = "OriCuphead.dll"
_CREATE_NO_WINDOW = 0x08000000


def log(msg: str) -> None:
    print(f"{TAG} {msg}", flush=True)


# ── bundled files ─────────────────────────────────────────────────────────────

def bundle_dir() -> Path:
    """Where build.py's --add-data files live (PyInstaller unpacks them to _MEIPASS)."""
    base = getattr(sys, "_MEIPASS", None)
    return Path(base) if base else Path(__file__).resolve().parent / "payload"


# ── Melty test-mode helper ────────────────────────────────────────────────────

def melty_check(name: str, passed: bool = True, reason: str = "") -> None:
    """Append a pass/fail line to Melty's test checks file if running in test mode."""
    try:
        appdata = os.environ.get("LOCALAPPDATA", "")
        if not appdata:
            return
        test_json = Path(appdata) / "Melty" / "test" / "point-of-light.json"
        if not test_json.exists():
            return
        checks_path = json.loads(test_json.read_text()).get("checks", "")
        if not checks_path:
            return
        line = f"{'pass' if passed else 'fail'} {name}" + (f": {reason}" if reason else "")
        with open(checks_path, "a") as f:
            f.write(line + "\n")
    except Exception:
        pass


# ── finding Cuphead ───────────────────────────────────────────────────────────

def is_real_cuphead(path: Path | None) -> bool:
    """A full install has Cuphead.exe *and* Cuphead_Data/Managed — Melty's bare marker copy doesn't."""
    return bool(path) and (path / "Cuphead.exe").is_file() and (path / "Cuphead_Data" / "Managed").is_dir()


def _steam_libraries() -> list[Path]:
    libs: list[Path] = []
    try:
        import winreg
        for hive, key in ((winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam"),
                          (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam"),
                          (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Valve\Steam")):
            for value in ("SteamPath", "InstallPath"):
                try:
                    with winreg.OpenKey(hive, key) as k:
                        libs.append(Path(winreg.QueryValueEx(k, value)[0]))
                except OSError:
                    pass
    except ImportError:
        pass
    # Extra libraries are listed in libraryfolders.vdf as  "path"  "D:\\SteamLibrary"
    for steam in list(libs):
        vdf = steam / "steamapps" / "libraryfolders.vdf"
        try:
            for m in re.finditer(r'"path"\s+"([^"]+)"', vdf.read_text(errors="ignore")):
                libs.append(Path(m.group(1).replace("\\\\", "\\")))
        except OSError:
            pass
    return libs


def find_cuphead() -> Path | None:
    candidates: list[Path] = []

    try:
        import winreg
        for hive in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
            for view in (0, getattr(winreg, "KEY_WOW64_64KEY", 0), getattr(winreg, "KEY_WOW64_32KEY", 0)):
                try:
                    with winreg.OpenKey(hive, rf"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {STEAM_APP_ID}",
                                        0, winreg.KEY_READ | view) as k:
                        candidates.append(Path(winreg.QueryValueEx(k, "InstallLocation")[0]))
                except OSError:
                    pass
    except ImportError:
        pass

    for lib in _steam_libraries():
        candidates.append(lib / "steamapps" / "common" / "Cuphead")

    for drive in string.ascii_uppercase:
        root = Path(f"{drive}:/")
        for sub in ("SteamLibrary/steamapps/common/Cuphead",
                    "Program Files (x86)/Steam/steamapps/common/Cuphead",
                    "Program Files/Steam/steamapps/common/Cuphead",
                    "GOG Games/Cuphead",
                    "Program Files (x86)/GOG Galaxy/Games/Cuphead",
                    "Games/Cuphead",
                    "XboxGames/Cuphead/Content"):
            candidates.append(root / sub)

    for p in candidates:
        if is_real_cuphead(p):
            return p
    return None


def exe_arch(exe: Path) -> str:
    """'x64' or 'x86', read from the PE header's Machine field."""
    try:
        with open(exe, "rb") as f:
            f.seek(0x3C)
            pe = struct.unpack("<I", f.read(4))[0]
            f.seek(pe + 4)
            machine = struct.unpack("<H", f.read(2))[0]
        return "x86" if machine == 0x014C else "x64"
    except OSError:
        return "x64"


# ── installing ────────────────────────────────────────────────────────────────

def bepinex_installed(game: Path) -> bool:
    return (game / "BepInEx" / "core" / "BepInEx.dll").is_file() and \
           ((game / "winhttp.dll").is_file() or (game / "version.dll").is_file())


def install_bepinex(game: Path, arch: str) -> None:
    if bepinex_installed(game):
        log("BepInEx is already installed.")
        return
    archive = bundle_dir() / f"bepinex_{arch}.zip"
    if not archive.is_file():
        raise RuntimeError(f"bundled BepInEx ({archive.name}) is missing from this build")
    log(f"Installing BepInEx 5 ({arch}) into {game} ...")
    with zipfile.ZipFile(archive) as z:
        for member in z.infolist():
            target = (game / member.filename).resolve()
            if game.resolve() not in target.parents and target != game.resolve():
                continue    # never write outside the game folder
            # Keep any BepInEx config the player already has.
            if target.exists() and member.filename.lower().endswith((".cfg", ".ini")):
                continue
            z.extract(member, game)


def install_plugin(game: Path) -> Path:
    src = bundle_dir() / PLUGIN_DLL
    if not src.is_file():
        raise RuntimeError(f"bundled {PLUGIN_DLL} is missing from this build")
    dest_dir = game / "BepInEx" / "plugins" / "OriCuphead"
    dest_dir.mkdir(parents=True, exist_ok=True)
    dest = dest_dir / PLUGIN_DLL
    shutil.copyfile(src, dest)
    log(f"Ori plugin installed: {dest}")
    return dest


def remove_plugin(dll: Path) -> None:
    for attempt in range(5):
        try:
            dll.unlink(missing_ok=True)
            try:
                dll.parent.rmdir()          # only if nothing else (skin/, sounds/) is in there
            except OSError:
                pass
            log("Ori plugin removed — Cuphead is vanilla again outside Melty.")
            return
        except PermissionError:
            time.sleep(2)                   # game still shutting down
    log(f"Couldn't remove {dll}; delete it by hand for vanilla Cuphead.")


# ── running ───────────────────────────────────────────────────────────────────

def cuphead_running() -> bool:
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Cuphead.exe", "/NH"],
                             capture_output=True, text=True, timeout=10,
                             creationflags=_CREATE_NO_WINDOW).stdout
        return "cuphead.exe" in out.lower()
    except Exception:
        return False


def launch(game: Path) -> None:
    if "steamapps" in str(game).lower():
        # Through Steam so the Steam overlay, achievements and cloud saves work.
        try:
            os.startfile(f"steam://rungameid/{STEAM_APP_ID}")   # type: ignore[attr-defined]
            log("Starting Cuphead through Steam ...")
            return
        except (AttributeError, OSError):
            pass
    subprocess.Popen([str(game / "Cuphead.exe")], cwd=str(game))
    log("Starting Cuphead ...")


def watch_log(game: Path, seen: set[str], since: float) -> None:
    """Relay the plugin's key log lines, and report them to Melty's test mode."""
    path = game / "BepInEx" / "LogOutput.log"
    try:
        if path.stat().st_mtime < since:
            return                          # still last session's log
        text = path.read_text(errors="ignore")
    except OSError:
        return
    for key, needle in (("plugin-loaded", "Point of Light loaded"),
                        ("ori-active", "Ori joined")):
        if key not in seen and needle in text:
            seen.add(key)
            melty_check(key)
            log({"plugin-loaded": "BepInEx loaded the Ori plugin.",
                 "ori-active": "Ori is in the level!"}[key])
    for line in text.splitlines():
        if "Point of Light" in line or "OriCuphead" in line:
            if ("Warning" in line or "Error" in line) and line not in seen:
                seen.add(line)
                log("game log: " + line.strip())


def main() -> None:
    parser = argparse.ArgumentParser(prog="oricuphead", description="Ori in Cuphead — installs the mod and starts Cuphead")
    parser.add_argument("--cuphead-path", default="", help="Cuphead install folder (optional; auto-detected)")
    parser.add_argument("--no-launch", action="store_true", help="install only, don't start the game")
    args = parser.parse_args()

    game = Path(args.cuphead_path) if args.cuphead_path else None
    if not is_real_cuphead(game):
        game = find_cuphead()
    if game is None:
        melty_check("found-cuphead", False, "Cuphead install not found")
        sys.exit(f"{TAG} Couldn't find Cuphead. Install it (Steam or GOG) or pass --cuphead-path <folder>.")
    log(f"Cuphead: {game}")
    melty_check("started")

    try:
        install_bepinex(game, exe_arch(game / "Cuphead.exe"))
        dll = install_plugin(game)
    except PermissionError:
        melty_check("installed", False, "no write access to the Cuphead folder")
        sys.exit(f"{TAG} Windows refused to write into {game}. Try running Melty as administrator once.")
    except Exception as exc:
        melty_check("installed", False, str(exc))
        sys.exit(f"{TAG} Install failed: {exc}")
    melty_check("installed")

    if args.no_launch:
        log("Installed. Not starting the game (--no-launch).")
        return

    started = time.time()
    try:
        if not cuphead_running():
            launch(game)
        log("Waiting for Cuphead to start ...")
        deadline = time.time() + 180
        while not cuphead_running():
            if time.time() > deadline:
                log("Cuphead didn't start within 3 minutes. Start it yourself — Ori stays installed until it closes.")
                deadline = time.time() + 3600
            time.sleep(2)
        melty_check("game-running")
        log("Cuphead is running. Ori will appear about a second into any level. Press F8 to toggle.")

        seen: set[str] = set()
        misses = 0
        while misses < 3:                       # tolerate a brief gap (e.g. Steam relaunching the exe)
            misses = 0 if cuphead_running() else misses + 1
            watch_log(game, seen, started)
            time.sleep(3)
        log("Cuphead closed.")
    finally:
        remove_plugin(dll)


if __name__ == "__main__":
    main()
