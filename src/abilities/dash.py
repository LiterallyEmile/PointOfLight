"""Ori Dash — short directional burst, airborne or grounded."""
import time
import math
from ..config import Timing


class DashAbility:
    SPEED       = 16.0     # m/s horizontal burst
    UP_BOOST    = 2.0      # small upward kick so air dashes feel floaty

    def __init__(self, bridge, audio, overlay):
        self._bridge   = bridge
        self._audio    = audio
        self._overlay  = overlay
        self._last_use = 0.0
        self._ready    = True

    @property
    def on_cooldown(self) -> bool:
        return (time.time() - self._last_use) < Timing.DASH_COOLDOWN

    def execute(self, dx: float, dz: float) -> None:
        """dx/dz: horizontal direction (-1..1), normalised by caller."""
        if self.on_cooldown:
            return
        mag = math.sqrt(dx*dx + dz*dz)
        if mag < 0.01:
            dx, dz = 0.0, 1.0      # dash forward if no direction given
            mag = 1.0
        dx /= mag
        dz /= mag

        vx, vy, vz = self._bridge.get_velocity()
        # Cancel current horizontal momentum, inject dash
        self._bridge.set_velocity(
            dx * self.SPEED,
            max(vy, self.UP_BOOST),
            dz * self.SPEED,
        )
        self._last_use = time.time()
        self._audio.play("SOUND_DASH")
        self._overlay.emit_burst(color=(160, 200, 255), radius=22)
