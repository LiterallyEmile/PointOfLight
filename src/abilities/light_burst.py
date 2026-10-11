"""
Ori Light Burst — hold to charge, release for an explosive upward + forward launch.
Full charge fires upward; partial charge scales accordingly.
"""
import time
from ..config import Timing


class LightBurstAbility:
    MIN_HOLD    = 0.15    # seconds before any burst fires
    SPEED_UP    = Timing.LIGHT_BURST_SPEED
    SPEED_FWD   = 10.0

    def __init__(self, bridge, audio, overlay):
        self._bridge      = bridge
        self._audio       = audio
        self._overlay     = overlay
        self._held_since  = None
        self._last_use    = 0.0
        self._cooldown    = 0.7

    @property
    def on_cooldown(self) -> bool:
        return (time.time() - self._last_use) < self._cooldown

    def press(self) -> None:
        if self.on_cooldown:
            return
        self._held_since = time.time()

    def release(self) -> None:
        if self._held_since is None:
            return
        held = time.time() - self._held_since
        self._held_since = None
        if held < self.MIN_HOLD:
            return
        charge = min(held / Timing.LIGHT_BURST_CHARGE, 1.0)
        self._fire(charge)

    def _fire(self, charge: float) -> None:
        vx, _, vz = self._bridge.get_velocity()
        vert  = self.SPEED_UP  * (0.5 + 0.5 * charge)
        horiz = self.SPEED_FWD * charge
        mag_h = (vx*vx + vz*vz) ** 0.5
        if mag_h > 0:
            vx = (vx / mag_h) * horiz
            vz = (vz / mag_h) * horiz
        self._bridge.set_velocity(vx, vert, vz)
        self._last_use = time.time()
        self._audio.play("SOUND_LIGHT_BURST")
        self._overlay.emit_burst(color=(255, 255, 180), radius=int(32 + charge * 20))
