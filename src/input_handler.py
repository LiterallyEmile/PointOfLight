"""
Point of Light — input handler.

Reads keyboard and XInput controller state so ability code can query
button/key events without caring about the input device.
"""
from __future__ import annotations
import time
from typing import Set, Dict

try:
    import keyboard as _kb
    _KB_OK = True
except ImportError:
    _KB_OK = False
    print("[Input] 'keyboard' library not found — install: pip install keyboard")

try:
    import inputs as _inputs
    _CONTROLLER_OK = True
except ImportError:
    _CONTROLLER_OK = False


class InputHandler:
    def __init__(self):
        self._pressed: Set[str]  = set()
        self._just_pressed: Set[str] = set()
        self._just_released: Set[str] = set()
        self._axes: Dict[str, float] = {"move_x": 0.0, "move_z": 0.0}
        self._prev: Set[str] = set()

        if _KB_OK:
            try:
                _kb.on_press(self._on_key_down)
                _kb.on_release(self._on_key_up)
            except Exception as exc:
                print(f"[Input] Keyboard hook failed (run as admin for full input): {exc}")

    def _on_key_down(self, event) -> None:
        self._pressed.add(event.name.lower())

    def _on_key_up(self, event) -> None:
        self._pressed.discard(event.name.lower())

    def tick(self) -> None:
        self._just_pressed  = self._pressed - self._prev
        self._just_released = self._prev    - self._pressed
        self._prev = set(self._pressed)
        self._read_wasd()

    def _read_wasd(self) -> None:
        x = 0.0
        z = 0.0
        if self.is_held("d"):  x += 1.0
        if self.is_held("a"):  x -= 1.0
        if self.is_held("w"):  z += 1.0
        if self.is_held("s"):  z -= 1.0
        self._axes["move_x"] = x
        self._axes["move_z"] = z

    def is_held(self, key: str) -> bool:
        return key.lower() in self._pressed

    def just_pressed(self, key: str) -> bool:
        return key.lower() in self._just_pressed

    def just_released(self, key: str) -> bool:
        return key.lower() in self._just_released

    def move_direction(self):
        """Returns (dx, dz) normalised movement direction from WASD."""
        return self._axes["move_x"], self._axes["move_z"]
