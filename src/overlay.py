"""
Point of Light — Ori-style light trail overlay.

A transparent, click-through, always-on-top window rendered over the game.
Uses pygame with a per-pixel alpha surface and Win32 layered window tricks
to sit invisibly on top of Halo Infinite.

Trail particles track screen-centre motion and fade with age, giving the
glowing cyan spirit-light aesthetic from Ori.
"""
from __future__ import annotations
import time
import collections
import math
from typing import Tuple, List, Deque
from .config import Visual

try:
    import pygame
    import ctypes
    _OK = True
except ImportError:
    _OK = False
    print("[Overlay] pygame not found — trails disabled.")


Color = Tuple[int, int, int]


class TrailParticle:
    __slots__ = ("x", "y", "born", "color", "radius")

    def __init__(self, x: float, y: float, color: Color, radius: int):
        self.x      = x
        self.y      = y
        self.born   = time.time()
        self.color  = color
        self.radius = radius


class OriTrailOverlay:
    TRAIL_LIFE = 0.45    # seconds a trail particle lives

    def __init__(self):
        self._running   = False
        self._particles: List[TrailParticle] = []
        self._bursts: List[dict] = []
        self._arcs: List[dict]   = []
        self._screen_w  = 1920
        self._screen_h  = 1080

    def run(self) -> None:
        if not _OK:
            return
        pygame.init()
        info = pygame.display.Info()
        self._screen_w = info.current_w
        self._screen_h = info.current_h

        flags = pygame.NOFRAME | pygame.SRCALPHA
        screen = pygame.display.set_mode((self._screen_w, self._screen_h), flags)
        pygame.display.set_caption("Point of Light Overlay")

        # Make window transparent and click-through via Win32
        hwnd = pygame.display.get_wm_info().get("window")
        if hwnd:
            self._setup_transparent_window(hwnd)

        clock = pygame.time.Clock()
        self._running = True
        cx = self._screen_w // 2
        cy = self._screen_h // 2
        prev_cx, prev_cy = cx, cy

        while self._running:
            for event in pygame.event.get():
                if event.type == pygame.QUIT:
                    self._running = False

            screen.fill((0, 0, 0, 0))
            now = time.time()

            # Add trail particle at crosshair-relative position
            dx_m = cx - prev_cx
            dy_m = cy - prev_cy
            if abs(dx_m) + abs(dy_m) > 2:
                self._particles.append(
                    TrailParticle(cx, cy, Visual.TRAIL_BASE_COLOR, 6)
                )

            # Draw and age particles
            survivors = []
            for p in self._particles:
                age = now - p.born
                frac = age / self.TRAIL_LIFE
                if frac >= 1.0:
                    continue
                alpha = int(Visual.TRAIL_ALPHA_START * (1.0 - frac))
                r = int(p.radius * (1.0 - frac * 0.4))
                surf = pygame.Surface((r*2, r*2), pygame.SRCALPHA)
                cr, cg, cb = p.color
                pygame.draw.circle(surf, (cr, cg, cb, alpha), (r, r), r)
                # Glow ring
                gr, gg, gb = Visual.TRAIL_GLOW_COLOR
                pygame.draw.circle(surf, (gr, gg, gb, alpha // 3), (r, r), r+2, 2)
                screen.blit(surf, (int(p.x) - r, int(p.y) - r))
                survivors.append(p)
            self._particles = survivors

            # Draw burst flashes
            active_bursts = []
            for b in self._bursts:
                age = now - b["born"]
                if age > 0.25:
                    continue
                frac = age / 0.25
                alpha = int(200 * (1 - frac))
                radius = int(b["radius"] * (1 + frac * 1.5))
                surf = pygame.Surface((radius*2, radius*2), pygame.SRCALPHA)
                cr, cg, cb = b["color"]
                pygame.draw.circle(surf, (cr, cg, cb, alpha), (radius, radius), radius, 3)
                screen.blit(surf, (cx - radius, cy - radius))
                active_bursts.append(b)
            self._bursts = active_bursts

            prev_cx, prev_cy = cx, cy
            pygame.display.flip()
            clock.tick(Visual.OVERLAY_FPS)

        pygame.quit()

    def _setup_transparent_window(self, hwnd: int) -> None:
        """Make the pygame window transparent and click-through using Win32 API."""
        import ctypes
        GWL_EXSTYLE    = -20
        WS_EX_LAYERED  = 0x80000
        WS_EX_TRANSPARENT = 0x20
        LWA_ALPHA      = 0x2
        SetWindowLong  = ctypes.windll.user32.SetWindowLongW
        SetLayeredWindowAttributes = ctypes.windll.user32.SetLayeredWindowAttributes
        SetWindowPos   = ctypes.windll.user32.SetWindowPos
        HWND_TOPMOST   = -1
        SWP_NOMOVE     = 0x0002
        SWP_NOSIZE     = 0x0001

        ex_style = SetWindowLong(hwnd, GWL_EXSTYLE, WS_EX_LAYERED | WS_EX_TRANSPARENT)
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE)

    def stop(self) -> None:
        self._running = False

    # ------------------------------------------------------------------
    # Effect emitters (called from ability code)
    # ------------------------------------------------------------------

    def emit_burst(self, color: Color, radius: int) -> None:
        self._bursts.append({"color": color, "radius": radius, "born": time.time()})

    def emit_ring(self, color: Color, radius: int) -> None:
        self._bursts.append({"color": color, "radius": radius, "born": time.time()})

    def emit_streak(self, color: Color, length: int) -> None:
        cx = self._screen_w // 2
        cy = self._screen_h // 2
        for i in range(0, length, 4):
            self._particles.append(TrailParticle(cx - i, cy + 2, color, 4))

    def emit_arc(self, start: tuple, end: tuple, color: Color) -> None:
        # Visual only — draws a fading line from crosshair toward target
        self._bursts.append({"color": color, "radius": 20, "born": time.time()})
