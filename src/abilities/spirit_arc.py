"""
Ori Spirit Arc — ranged grapple that pulls Chief toward a distant point.
Replaces Halo's Grappleshot but with Ori's fluid pull-and-swing feel.
"""
import time
import math
from ..config import Timing


class SpiritArcAbility:
    RANGE       = Timing.SPIRIT_ARC_RANGE
    PULL_SPEED  = 20.0
    ARC_COOLDOWN = 0.55

    def __init__(self, bridge, audio, overlay):
        self._bridge     = bridge
        self._audio      = audio
        self._overlay    = overlay
        self._active     = False
        self._target     = None       # (x, y, z) world position
        self._last_use   = 0.0

    @property
    def on_cooldown(self) -> bool:
        return (time.time() - self._last_use) < self.ARC_COOLDOWN

    def fire(self) -> None:
        if self.on_cooldown:
            return
        direction = self._bridge._get_crosshair_direction()
        if not direction:
            return
        px, py, pz = self._bridge.get_position()
        dx, dy, dz = direction
        self._target = (
            px + dx * self.RANGE,
            py + dy * self.RANGE,
            pz + dz * self.RANGE,
        )
        self._active = True
        self._audio.play("SOUND_SPIRIT_ARC")
        self._overlay.emit_arc(
            start=(px, py, pz),
            end=self._target,
            color=(150, 210, 255),
        )

    def update(self) -> None:
        if not self._active or self._target is None:
            return
        px, py, pz = self._bridge.get_position()
        tx, ty, tz = self._target
        dx = tx - px
        dy = ty - py
        dz = tz - pz
        dist = math.sqrt(dx*dx + dy*dy + dz*dz)
        if dist < 1.5:
            self._active = False
            self._last_use = time.time()
            return
        # Pull toward target
        self._bridge.set_velocity(
            (dx / dist) * self.PULL_SPEED,
            (dy / dist) * self.PULL_SPEED,
            (dz / dist) * self.PULL_SPEED,
        )

    def cancel(self) -> None:
        self._active = False
        self._last_use = time.time()
