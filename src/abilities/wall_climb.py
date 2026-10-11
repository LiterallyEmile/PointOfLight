"""
Ori Wall Climb — grab a wall mid-air, slide slowly, jump off in any direction.

Detection: each tick we cast a ray (position + velocity direction) against the
nearby geometry.  In practice we proxy this with a simple heuristic:
if the player is airborne, not grounded, velocity has significant horizontal
component, and is NOT accelerating (i.e. hitting something), we treat them
as wall-adjacent.
"""
import time
import math
from ..config import Timing


_STUCK_SPEED_THRESHOLD = 1.0   # if horizontal speed drops below this while airborne, assume wall contact


class WallClimbAbility:
    SLIDE_VY    = Timing.WALL_SLIDE_SPEED   # slow downward drift
    GRAB_TIMEOUT = Timing.WALL_GRAB_TIMEOUT
    WALLJUMP_VY  = 11.0
    WALLJUMP_H   = 9.0

    def __init__(self, bridge, audio, overlay):
        self._bridge        = bridge
        self._audio         = audio
        self._overlay       = overlay
        self._grabbing      = False
        self._grab_start    = 0.0
        self._wall_normal   = (1.0, 0.0, 0.0)   # rough estimate
        self._last_vx       = 0.0
        self._last_vz       = 0.0

    def update(self, jump_pressed: bool) -> None:
        grounded = self._bridge.is_grounded()

        if grounded:
            self._grabbing = False
            return

        vx, vy, vz = self._bridge.get_velocity()
        h_speed = math.sqrt(vx*vx + vz*vz)

        if not self._grabbing:
            prev_h = math.sqrt(self._last_vx**2 + self._last_vz**2)
            # Wall-contact heuristic: we had horizontal speed and now we don't
            if prev_h > 4.0 and h_speed < _STUCK_SPEED_THRESHOLD and vy < 2.0:
                self._start_grab(vx, vz)
        else:
            elapsed = time.time() - self._grab_start
            if elapsed > self.GRAB_TIMEOUT:
                # Force jump-off after too long
                self._wall_jump()
                return
            # Slide slowly down
            self._bridge.set_velocity(0.0, self.SLIDE_VY, 0.0)
            if jump_pressed:
                self._wall_jump()

        self._last_vx = vx
        self._last_vz = vz

    def _start_grab(self, vx: float, vz: float) -> None:
        self._grabbing   = True
        self._grab_start = time.time()
        mag = math.sqrt(vx*vx + vz*vz)
        if mag > 0:
            # Wall normal points back at us (opposite of approach direction)
            self._wall_normal = (-vx/mag, 0.0, -vz/mag)
        self._bridge.set_velocity(0.0, 0.0, 0.0)
        self._audio.play("SOUND_WALL_GRAB")
        self._overlay.emit_burst(color=(180, 220, 255), radius=18)

    def _wall_jump(self) -> None:
        nx, _, nz = self._wall_normal
        self._bridge.set_velocity(
            nx * self.WALLJUMP_H,
            self.WALLJUMP_VY,
            nz * self.WALLJUMP_H,
        )
        self._grabbing = False
        self._audio.play("SOUND_WALL_JUMP")
        self._overlay.emit_burst(color=(200, 230, 255), radius=24)
