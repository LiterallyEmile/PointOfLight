"""
Ori Bash — freeze time briefly, redirect off the nearest enemy/projectile.

Implementation:
  1. Press Bash → game slows to 15 % speed for a short aiming window.
  2. The player's crosshair (or nearest visible enemy) is used as the redirect target.
  3. On confirm (or timeout), the player launches at full speed in that direction
     and the target receives an impulse in the opposite direction.
"""
import time
import math
from ..config import Timing


class BashAbility:
    SLOWMO_SCALE    = 0.15
    LAUNCH_SPEED    = Timing.BASH_LAUNCH_SPEED
    ENEMY_IMPULSE   = 12.0

    def __init__(self, bridge, audio, overlay):
        self._bridge    = bridge
        self._audio     = audio
        self._overlay   = overlay
        self._active    = False
        self._start_time = 0.0
        self._last_use  = 0.0
        _COOLDOWN       = 0.6
        self._cooldown  = _COOLDOWN

    @property
    def on_cooldown(self) -> bool:
        return (time.time() - self._last_use) < self._cooldown

    def activate(self) -> None:
        if self.on_cooldown or self._active:
            return
        self._active = True
        self._start_time = time.time()
        self._bridge.set_timescale(self.SLOWMO_SCALE)
        # Zero gravity during bash window so Ori floats
        vx, vy, vz = self._bridge.get_velocity()
        self._bridge.set_velocity(vx * 0.1, 0.0, vz * 0.1)
        self._audio.play("SOUND_BASH")
        self._overlay.emit_ring(color=(255, 200, 80), radius=36)

    def update(self) -> None:
        if not self._active:
            return
        elapsed = time.time() - self._start_time
        if elapsed >= Timing.BASH_SLOWMO_DURATION:
            self._launch()

    def confirm(self) -> None:
        """Called when player releases the bash key or re-presses to confirm direction."""
        if self._active:
            self._launch()

    def _launch(self) -> None:
        direction = self._bridge.get_nearest_enemy_direction()
        if direction:
            dx, dy, dz = direction
        else:
            dx, dy, dz = 0.0, 0.5, 1.0

        self._bridge.set_velocity(
            dx * self.LAUNCH_SPEED,
            dy * self.LAUNCH_SPEED,
            dz * self.LAUNCH_SPEED,
        )
        self._bridge.set_timescale(1.0)
        self._active = False
        self._last_use = time.time()
        self._audio.play("SOUND_BASH_LAUNCH")
        self._overlay.emit_burst(color=(255, 220, 100), radius=40)
