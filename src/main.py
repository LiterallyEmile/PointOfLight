"""
Point of Light — launcher.

Melty calls:
  pointoflight.exe --halo-path <path> --ori-path <path>

Both paths are passed automatically by Melty when the player has the
required games installed.
"""
from __future__ import annotations
import argparse
import subprocess
import sys
import threading
import time
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(
        prog="pointoflight",
        description="Point of Light — Ori movement in Halo Infinite campaign",
    )
    parser.add_argument("--halo-path", required=True,
                        help="Halo Infinite install directory (passed by Melty)")
    parser.add_argument("--ori-path",  required=True,
                        help="Ori and the Will of the Wisps install directory (passed by Melty)")
    args = parser.parse_args()

    halo_path = Path(args.halo_path)
    ori_path  = Path(args.ori_path)

    _require_dir(halo_path, "Halo Infinite")
    _require_dir(ori_path,  "Ori and the Will of the Wisps")

    print("[Point of Light] Loading Ori's spirit...")

    # Late imports so missing deps give a clear error before we start the game
    from .audio    import OriAudioEngine
    from .overlay  import OriTrailOverlay
    from .game_bridge   import HaloInfiniteBridge
    from .ori_movement  import OriMovementController
    from .input_handler import InputHandler

    audio = OriAudioEngine(ori_path)
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


def _require_dir(path: Path, name: str) -> None:
    if not path.exists():
        sys.exit(f"[Point of Light] {name} not found at: {path}\n"
                 "Make sure the game is installed and Melty has the correct path.")


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


if __name__ == "__main__":
    main()
