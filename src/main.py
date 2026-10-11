"""
Point of Light — launcher.

Melty calls:
  pointoflight.exe [--halo-path <path>] [--ori-path <path>]

Paths are optional; the exe auto-detects Xbox Game Pass and Steam
installs when they are absent or invalid.
"""
from __future__ import annotations
import argparse
import subprocess
import sys
import threading
from pathlib import Path


# ── path detection ────────────────────────────────────────────────────────────

def _find_halo() -> Path | None:
    """Locate Halo Infinite Campaign on this PC (Game Pass or Steam)."""
    candidates: list[Path] = []

    # Common Xbox Game Pass / Xbox app install roots
    for drive in "CDEFGH":
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
        if p.is_dir() and (p / "HaloInfinite.exe").exists():
            return p

    return None


def _find_ori() -> Path | None:
    """Locate Ori and the Will of the Wisps on this PC (Game Pass or Steam)."""
    candidates: list[Path] = []

    # Common Xbox Game Pass / Xbox app install roots
    for drive in "CDEFGH":
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

def _launch_halo(halo_path: Path) -> None:
    exe = halo_path / "HaloInfinite.exe"
    if not exe.exists():
        for found in halo_path.rglob("HaloInfinite.exe"):
            exe = found
            break
    if not exe.exists():
        sys.exit(f"[Point of Light] HaloInfinite.exe not found under {halo_path}")
    subprocess.Popen([str(exe), "-singleplayer", "-novid"])
    print(f"[Point of Light] Launched: {exe}")


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
    if halo_path and not (halo_path / "HaloInfinite.exe").exists():
        halo_path = None  # provided path is bad; fall through to auto-detect
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

    print("[Point of Light] Waiting for Halo Infinite (up to 2 min)...")
    bridge = HaloInfiniteBridge()
    bridge.wait_for_process(timeout=120)

    overlay = OriTrailOverlay()
    overlay_thread = threading.Thread(target=overlay.run, daemon=True, name="overlay")
    overlay_thread.start()

    input_handler = InputHandler()
    controller = OriMovementController(bridge, audio, overlay, input_handler)

    print("[Point of Light] Active — Ori's movement lives in the Chief. Good luck.")
    controller.run()

    overlay.stop()


if __name__ == "__main__":
    main()
