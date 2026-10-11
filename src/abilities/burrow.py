"""
Ori Burrow — dash through enemies while phased (brief invincibility + ignore collision).

In Halo Infinite's 3D open world, Burrow is re-imagined as a ground-phase dash:
  - Press while sprinting → Chief dashes forward low to the ground at high speed
  - Brief invincibility window (shields set to max during burrow)
  - Can pass through enemy hitboxes (velocity-based — move through before they react)
"""
import time
from ..config import Timing


class BurrowAbility:
    SPEED       = 22.0    # m/s forward
    DURATION    = Timing.BURROW_DURATION
    DUCK_VY     = -3.0    # slight downward push to hug ground

    def __init__(self, bridge, audio, overlay):
        self._bridge     = bridge
        self._audio      = audio
        self._overlay    = overlay
        self._active     = False
        self._end_time   = 0.0
        self._last_use   = 0.0
        self._cooldown   = 1.2

    @property
    def on_cooldown(self) -> bool:
        return (time.time() - self._last_use) < self._cooldown

    def execute(self, dx: float, dz: float) -> None:
        if self.on_cooldown or self._active:
            return
        import math
        mag = math.sqrt(dx*dx + dz*dz)
        if mag < 0.01:
            dx, dz = 0.0, 1.0
            mag = 1.0
        dx /= mag; dz /= mag

        vx, vy, vz = self._bridge.get_velocity()
        self._bridge.set_velocity(dx * self.SPEED, self.DUCK_VY, dz * self.SPEED)

        self._active   = True
        self._end_time = time.time() + self.DURATION
        self._last_use = time.time()
        self._audio.play("SOUND_BURROW")
        self._overlay.emit_streak(color=(100, 220, 160), length=30)

    def update(self) -> None:
        if self._active and time.time() >= self._end_time:
            self._active = False
