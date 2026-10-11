"""Ori Double Jump — second leap mid-air, full height, omnidirectional."""
import time
from ..config import Timing


class DoubleJumpAbility:
    VERTICAL_SPEED  = 12.0    # m/s upward
    HORIZONTAL_AMP  = 1.15    # multiply current horizontal speed slightly

    def __init__(self, bridge, audio, overlay):
        self._bridge      = bridge
        self._audio       = audio
        self._overlay     = overlay
        self._used        = False   # consumed this airborne period
        self._was_grounded = True

    def update(self, jumped: bool) -> None:
        grounded = self._bridge.is_grounded()

        if grounded:
            self._was_grounded = True
            self._used = False
            return

        if self._was_grounded and not grounded:
            # Just left the ground; first jump consumed by engine
            self._was_grounded = False
            return

        if jumped and not self._used:
            self._execute()

    def _execute(self) -> None:
        vx, vy, vz = self._bridge.get_velocity()
        self._bridge.set_velocity(
            vx * self.HORIZONTAL_AMP,
            self.VERTICAL_SPEED,
            vz * self.HORIZONTAL_AMP,
        )
        self._used = True
        self._audio.play("SOUND_DOUBLE_JUMP")
        self._overlay.emit_burst(color=(200, 230, 255), radius=28)
