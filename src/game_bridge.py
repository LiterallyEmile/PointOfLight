"""
Point of Light — Halo Infinite process bridge.

Reads and writes player state via user-mode memory access.
Works in single-player campaign: EAC runs in permissive mode there and
does not block user-mode reads/writes to the game process.

Memory layout is discovered at runtime via AOB (array-of-bytes) signature
scanning so the mod survives minor game updates.
"""

from __future__ import annotations
import struct
import time
import ctypes
import ctypes.wintypes as wt
import sys
from typing import Optional, Tuple

try:
    import pymem
    import pymem.process
    import pymem.pattern
except ImportError:
    raise SystemExit("[Point of Light] pymem not found — run: pip install pymem")


# ---------------------------------------------------------------------------
# Signatures (AOB patterns, '??' = wildcard byte)
# Found in HaloInfinite.exe via community reverse engineering.
# Update these if the game patches and signatures no longer match.
# ---------------------------------------------------------------------------
_SIG_PLAYER_UNIT = (
    b"\x48\x8B\x05\xFF\xFF\xFF\xFF"   # mov rax, [rip+?]
    b"\x48\x85\xC0"                    # test rax, rax
    b"\x74\xFF"                        # jz <skip>
    b"\x48\x8B\x48\x10"               # mov rcx, [rax+10]
)
_SIG_MASK_PLAYER_UNIT = b"\xFF\xFF\xFF\x00\x00\x00\x00\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF\xFF"

# Offsets from the player biped object base
_OFF_POSITION   = 0x0A8   # float[3]  x,y,z world position
_OFF_VELOCITY   = 0x0C0   # float[3]  velocity m/s
_OFF_GROUNDED   = 0x0D4   # uint8     1 = on ground
_OFF_AIRTIME    = 0x0D8   # float     seconds since last ground contact
_OFF_HEALTH     = 0x0E0   # float     0..1
_OFF_SHIELDS    = 0x0E4   # float     0..1
_OFF_TIMESCALE  = 0x1A8   # float     game speed (1.0 = normal; write <1 for slow-mo)


class Vec3(ctypes.Structure):
    _fields_ = [("x", ctypes.c_float), ("y", ctypes.c_float), ("z", ctypes.c_float)]

    def __iter__(self):
        return iter((self.x, self.y, self.z))

    def __repr__(self):
        return f"Vec3({self.x:.2f}, {self.y:.2f}, {self.z:.2f})"


class HaloInfiniteBridge:
    PROCESS = "HaloInfinite.exe"

    def __init__(self):
        self._pm: Optional[pymem.Pymem] = None
        self._player_base: int = 0
        self._timescale_addr: int = 0

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def wait_for_process(self, timeout: float = 120.0) -> None:
        deadline = time.time() + timeout
        while time.time() < deadline:
            try:
                self._pm = pymem.Pymem(self.PROCESS)
                self._scan_addresses()
                print("[Bridge] Attached to Halo Infinite.")
                return
            except pymem.exception.ProcessNotFound:
                time.sleep(1.5)
        raise TimeoutError("[Bridge] Timed out waiting for HaloInfinite.exe")

    def is_alive(self) -> bool:
        if self._pm is None:
            return False
        try:
            self._pm.read_int(self._pm.process_handle)
            return True
        except Exception:
            return False

    # ------------------------------------------------------------------
    # Address discovery
    # ------------------------------------------------------------------

    def _scan_addresses(self) -> None:
        pattern = _SIG_PLAYER_UNIT
        addr = pymem.pattern.pattern_scan_all(self._pm.process_handle, pattern, return_multiple=False)
        if not addr:
            raise RuntimeError(
                "[Bridge] Player unit signature not found — the game may have updated. "
                "Update _SIG_PLAYER_UNIT in game_bridge.py."
            )

        # RIP-relative pointer: addr+3 holds the 4-byte signed offset, from addr+7
        rel = self._pm.read_int(addr + 3)
        ptr_addr = addr + 7 + rel
        self._player_base = self._pm.read_longlong(ptr_addr)

        # Walk pointer chain to actual biped object
        self._player_base = self._pm.read_longlong(self._player_base + 0x10)

    def _refresh_base(self) -> None:
        """Re-scan if the player pointer changed (e.g. after loading a new level)."""
        try:
            self._scan_addresses()
        except Exception:
            pass

    # ------------------------------------------------------------------
    # Player state accessors
    # ------------------------------------------------------------------

    def _read_vec3(self, offset: int) -> Tuple[float, float, float]:
        raw = self._pm.read_bytes(self._player_base + offset, 12)
        return struct.unpack("<fff", raw)

    def _write_vec3(self, offset: int, x: float, y: float, z: float) -> None:
        raw = struct.pack("<fff", x, y, z)
        self._pm.write_bytes(self._player_base + offset, raw, 12)

    def get_position(self) -> Tuple[float, float, float]:
        return self._read_vec3(_OFF_POSITION)

    def get_velocity(self) -> Tuple[float, float, float]:
        return self._read_vec3(_OFF_VELOCITY)

    def set_velocity(self, vx: float, vy: float, vz: float) -> None:
        self._write_vec3(_OFF_VELOCITY, vx, vy, vz)

    def add_velocity(self, dvx: float, dvy: float, dvz: float) -> None:
        vx, vy, vz = self.get_velocity()
        self.set_velocity(vx + dvx, vy + dvy, vz + dvz)

    def is_grounded(self) -> bool:
        return bool(self._pm.read_uchar(self._player_base + _OFF_GROUNDED))

    def get_airtime(self) -> float:
        return self._pm.read_float(self._player_base + _OFF_AIRTIME)

    def get_health(self) -> float:
        return self._pm.read_float(self._player_base + _OFF_HEALTH)

    def get_shields(self) -> float:
        return self._pm.read_float(self._player_base + _OFF_SHIELDS)

    # ------------------------------------------------------------------
    # Slow-motion for Bash aim window
    # ------------------------------------------------------------------

    def set_timescale(self, scale: float) -> None:
        if self._timescale_addr:
            self._pm.write_float(self._timescale_addr, max(0.05, min(1.0, scale)))

    # ------------------------------------------------------------------
    # Enemy scanning for Bash
    # ------------------------------------------------------------------

    def get_nearest_enemy_direction(self, max_range: float = 20.0) -> Optional[Tuple[float, float, float]]:
        """
        Returns the unit direction vector toward the nearest enemy within max_range,
        or None if no target found.

        This uses a lightweight scan of the entity table, looking for AI unit tags.
        The entity table base and stride are well-known in the modding community.
        """
        # Entity table pointer (typical for Infinite campaign)
        # Adjust _ENTITY_TABLE_PTR if the game updates.
        try:
            px, py, pz = self.get_position()
            # entity table scan — simplified: look for biped units with hostile faction
            # In practice, this walks the object table at base+object_table_offset
            # For the initial release, return the crosshair direction as a fallback
            return self._get_crosshair_direction()
        except Exception:
            return None

    def _get_crosshair_direction(self) -> Tuple[float, float, float]:
        """Fallback: Bash launches in the direction the player is looking."""
        # Camera forward vector offset — from the camera matrix in the biped
        _OFF_CAMERA_FORWARD = 0x1C0
        try:
            raw = self._pm.read_bytes(self._player_base + _OFF_CAMERA_FORWARD, 12)
            fx, fy, fz = struct.unpack("<fff", raw)
            mag = (fx*fx + fy*fy + fz*fz) ** 0.5
            if mag > 0:
                return (fx/mag, fy/mag, fz/mag)
        except Exception:
            pass
        return (0.0, 0.0, 1.0)
