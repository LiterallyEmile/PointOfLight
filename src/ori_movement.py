"""
Point of Light — main movement controller.

Runs the 60 Hz update loop that reads input and dispatches to each ability.
"""
from __future__ import annotations
import time

from .config import Keys
from .abilities import (
    DashAbility, DoubleJumpAbility, BashAbility,
    LightBurstAbility, BurrowAbility, WallClimbAbility, SpiritArcAbility,
)


class OriMovementController:
    TICK_RATE = 1.0 / 60.0

    def __init__(self, bridge, audio, overlay, input_handler):
        self._bridge  = bridge
        self._input   = input_handler

        self.dash         = DashAbility(bridge, audio, overlay)
        self.double_jump  = DoubleJumpAbility(bridge, audio, overlay)
        self.bash         = BashAbility(bridge, audio, overlay)
        self.light_burst  = LightBurstAbility(bridge, audio, overlay)
        self.burrow       = BurrowAbility(bridge, audio, overlay)
        self.wall_climb   = WallClimbAbility(bridge, audio, overlay)
        self.spirit_arc   = SpiritArcAbility(bridge, audio, overlay)

    def run(self) -> None:
        while self._bridge.is_alive():
            t0 = time.perf_counter()
            try:
                self._tick()
            except Exception as exc:
                print(f"[Movement] tick error: {exc}")
            elapsed = time.perf_counter() - t0
            sleep = self.TICK_RATE - elapsed
            if sleep > 0:
                time.sleep(sleep)
        print("[Point of Light] Halo Infinite closed.")

    def _tick(self) -> None:
        inp = self._input
        inp.tick()

        dx, dz = inp.move_direction()
        jumped = inp.just_pressed("space")

        # ------------------------------------------------------------------
        # Double jump (must run before dash check so space isn't consumed twice)
        self.double_jump.update(jumped)

        # Dash — shift or double-tap direction
        if inp.just_pressed(Keys.DASH):
            self.dash.execute(dx, dz)

        # Bash
        if inp.just_pressed(Keys.BASH):
            self.bash.activate()
        if inp.just_released(Keys.BASH) and self.bash._active:
            self.bash.confirm()
        self.bash.update()

        # Light Burst
        if inp.just_pressed(Keys.LIGHT_BURST):
            self.light_burst.press()
        if inp.just_released(Keys.LIGHT_BURST):
            self.light_burst.release()

        # Burrow — C while moving
        if inp.just_pressed(Keys.BURROW):
            self.burrow.execute(dx, dz)
        self.burrow.update()

        # Wall climb — automatic, no key
        self.wall_climb.update(jumped)

        # Spirit Arc — right-click / mouse2
        if inp.just_pressed(Keys.SPIRIT_ARC):
            self.spirit_arc.fire()
        if inp.just_released(Keys.SPIRIT_ARC):
            self.spirit_arc.cancel()
        self.spirit_arc.update()
