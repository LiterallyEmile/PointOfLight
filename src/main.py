"""
Point of Light — launcher.

Melty calls:
  pointoflight.exe [--halo-path <path>] [--ori-path <path>]

Paths are optional; the exe auto-detects Xbox Game Pass and Steam
installs when they are absent or invalid.
"""
from __future__ import annotations
import argparse
import string
import subprocess
import sys
import threading
from pathlib import Path


# ── path detection ────────────────────────────────────────────────────────────

def _is_real_halo_install(path: Path) -> bool:
    """True when path is a full Halo Infinite install, not just Melty's bare marker copy.

    Steam installs have HaloInfinite.exe.
    Game Pass installs have the exe in a locked WindowsApps folder, but the
    Content folder contains subdirectories (maps/, data/, etc.).
    Melty's managed copy has only one .mapinfo marker file and no subdirectories.
    """
    if not path.is_dir():
        return False
    # Steam install: exe is directly present
    if (path / "HaloInfinite.exe").exists():
        return True
    # Game Pass Content folder: has at least one subdirectory
    try:
        for item in path.iterdir():
            if item.is_dir():
                return True
    except OSError:
        pass
    return False


def _find_halo() -> Path | None:
    """Locate Halo Infinite Campaign on this PC (Game Pass or Steam)."""
    candidates: list[Path] = []

    # All drive letters — Xbox games can be on any drive the user chose
    for drive in string.ascii_uppercase:
        for sub in [
            f"{drive}:/XboxGames/Halo Infinite/Content",
            f"{drive}:/XboxGames/Halo Infinite",
            f"{drive}:/Program Files/ModifiableWindowsApps/Halo Infinite",
        ]:
            candidates.append(Path(sub))

    # Steam via registry (App ID 1708091)
    try:
        import winreg
        for hive in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
            try:
                key = winreg.OpenKey(
                    hive,
                    r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1708091",
                )
                loc, _ = winreg.QueryValueEx(key, "InstallLocation")
                winreg.CloseKey(key)
                candidates.append(Path(loc))
            except OSError:
                pass
    except ImportError:
        pass

    for p in candidates:
        if _is_real_halo_install(p):
            return p
    # Also check the Content sub-folder of each candidate
    for base in list(candidates):
        p = base / "Content"
        if _is_real_halo_install(p):
            return p

    return None


def _find_ori() -> Path | None:
    """Locate Ori and the Will of the Wisps on this PC (Game Pass or Steam)."""
    candidates: list[Path] = []

    for drive in string.ascii_uppercase:
        for name in ("Ori and the Will of the Wisps", "OriAndTheWillOfTheWisps"):
            candidates.append(Path(f"{drive}:/XboxGames/{name}"))

    # Steam via registry (App ID 1057090)
    try:
        import winreg
        for hive in (winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER):
            try:
                key = winreg.OpenKey(
                    hive,
                    r"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1057090",
                )
                loc, _ = winreg.QueryValueEx(key, "InstallLocation")
                winreg.CloseKey(key)
                candidates.append(Path(loc))
            except OSError:
                pass
    except ImportError:
        pass

    for p in candidates:
        if p.is_dir():
            return p

    return None


# ── launch helpers ────────────────────────────────────────────────────────────

_CREATE_NO_WINDOW = 0x08000000

def _launch_halo_appx() -> bool:
    """Launch Halo Infinite via its AppX package (Xbox Game Pass). Returns True if launched."""
    # Try known hardcoded AUMID first (avoids PowerShell window)
    known_aumids = [
        "Microsoft.HaloInfinite_8wekyb3d8bbwe!HaloInfinite",
        "Microsoft.HaloInfinite_8wekyb3d8bbwe!App",
    ]
    for aumid in known_aumids:
        try:
            subprocess.Popen(
                ["explorer.exe", f"shell:AppsFolder\\{aumid}"],
                creationflags=_CREATE_NO_WINDOW,
            )
            print(f"[Point of Light] Launched via AppX: {aumid}")
            return True
        except Exception:
            continue
    # Dynamic lookup — hidden PowerShell window
    try:
        ps = subprocess.run(
            [
                "powershell", "-NoProfile", "-WindowStyle", "Hidden",
                "-NonInteractive", "-Command",
                "Get-AppxPackage | Where-Object { $_.Name -match 'Halo' } | "
                "Select-Object -ExpandProperty PackageFamilyName",
            ],
            capture_output=True, text=True, timeout=20,
            creationflags=_CREATE_NO_WINDOW,
        )
        for pfn in ps.stdout.strip().splitlines():
            pfn = pfn.strip()
            if not pfn:
                continue
            for entry in ("HaloInfinite", "App"):
                try:
                    subprocess.Popen(
                        ["explorer.exe", f"shell:AppsFolder\\{pfn}!{entry}"],
                        creationflags=_CREATE_NO_WINDOW,
                    )
                    print(f"[Point of Light] Launched via AppX: {pfn}!{entry}")
                    return True
                except Exception:
                    continue
    except Exception:
        pass
    return False


def _launch_halo(halo_path: Path) -> None:
    candidates = [
        halo_path / "HaloInfinite.exe",
        halo_path.parent / "HaloInfinite.exe",  # Content sub-folder case
    ]
    # Xbox Game Pass: exe lives in a locked WindowsApps directory
    try:
        import glob as _glob
        for pat in [
            r"C:\Program Files\WindowsApps\Microsoft.HaloInfinite_*\HaloInfinite.exe",
            r"C:\Program Files\WindowsApps\Microsoft.HaloInfinite_*\Content\HaloInfinite.exe",
        ]:
            hits = _glob.glob(pat)
            if hits:
                candidates.append(Path(hits[0]))
    except Exception:
        pass
    for exe in candidates:
        try:
            if exe.exists():
                subprocess.Popen([str(exe), "-singleplayer", "-novid"])
                print(f"[Point of Light] Launched: {exe}")
                return
        except OSError:
            continue
    # Game Pass: launch via AppX package manager
    if _launch_halo_appx():
        return
    print("[Point of Light] Could not launch Halo Infinite automatically.")
    print("[Point of Light] Please start Halo Infinite from the Xbox app now.")
    print("[Point of Light] Waiting for the game process (up to 3 min)...")


# ── entry point ───────────────────────────────────────────────────────────────

def main() -> None:
    parser = argparse.ArgumentParser(
        prog="pointoflight",
        description="Point of Light — Ori movement in Halo Infinite campaign",
    )
    parser.add_argument("--halo-path", default="",
                        help="Halo Infinite install directory (optional; auto-detected if omitted)")
    parser.add_argument("--ori-path",  default="",
                        help="Ori and the Will of the Wisps install directory (optional; auto-detected if omitted)")
    args = parser.parse_args()

    # Resolve Halo Infinite path
    halo_path = Path(args.halo_path) if args.halo_path else None
    if halo_path and not _is_real_halo_install(halo_path):
        halo_path = None  # bare Melty copy or wrong path; fall through to auto-detect
    if halo_path is None:
        halo_path = _find_halo()
    if halo_path is None:
        sys.exit(
            "[Point of Light] Could not find Halo Infinite Campaign.\n"
            "Please install it via Steam or Xbox, or pass --halo-path <folder>."
        )

    # Resolve Ori path (optional — audio degrades gracefully)
    ori_path = Path(args.ori_path) if args.ori_path else None
    if ori_path and not ori_path.is_dir():
        ori_path = None
    if ori_path is None:
        ori_path = _find_ori()
    if ori_path is None:
        print("[Point of Light] Ori not found — ability sounds will be silent.")

    print("[Point of Light] Loading Ori's spirit...")

    from .audio    import OriAudioEngine
    from .overlay  import OriTrailOverlay
    from .game_bridge   import HaloInfiniteBridge
    from .ori_movement  import OriMovementController
    from .input_handler import InputHandler

    audio = OriAudioEngine(ori_path)
    if ori_path is not None:
        audio.load_ability_sounds()

    _launch_halo(halo_path)

    import time

    while True:
        print("[Point of Light] Waiting for Halo Infinite (up to 3 min)...")
        try:
            bridge = HaloInfiniteBridge()
            bridge.wait_for_process(timeout=180)
        except TimeoutError:
            print("[Point of Light] Halo Infinite not detected — retrying in 20 s...")
            print("[Point of Light] If the game is not open, please start it from the Xbox app.")
            time.sleep(20)
            continue
        except Exception as exc:
            print(f"[Point of Light] Bridge error: {exc}. Retrying in 10 s...")
            time.sleep(10)
            continue

        overlay = OriTrailOverlay()
        overlay_thread = threading.Thread(target=overlay.run, daemon=True, name="overlay")
        overlay_thread.start()

        input_handler = InputHandler()
        controller = OriMovementController(bridge, audio, overlay, input_handler)

        print("[Point of Light] Active — Ori's movement lives in the Chief. Good luck.")
        try:
            controller.run()
        except Exception as exc:
            print(f"[Point of Light] Controller error: {exc}")
        finally:
            overlay.stop()

        print("[Point of Light] Halo Infinite session ended. Waiting 15 s before reconnect...")
        time.sleep(15)


if __name__ == "__main__":
    main()
