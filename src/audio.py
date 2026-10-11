"""
Point of Light — Ori audio engine.

Extracts and plays ability sounds from the player's Ori and the Will of the Wisps install.
Ori uses Wwise audio; sounds live in .wem files inside .bnk banks.
We locate them by their Wwise event hash, extract them to a temp folder as .wav,
then play them via pygame.mixer.
"""
from __future__ import annotations
import os
import struct
import tempfile
import hashlib
from pathlib import Path
from typing import Dict, Optional

try:
    import pygame
    pygame.mixer.pre_init(44100, -16, 2, 512)
    pygame.mixer.init()
    _PYGAME_OK = True
except ImportError:
    _PYGAME_OK = False
    print("[Audio] pygame not found — ability sounds disabled. Install: pip install pygame")


# Wwise FNV-1 hash used to identify events in soundbanks
def _wwise_hash(name: str) -> int:
    h = 2166136261
    for c in name.lower():
        h ^= ord(c)
        h = (h * 16777619) & 0xFFFFFFFF
    return h


# Known Wwise event names → sound file names inside the bank
_EVENT_MAP: Dict[str, str] = {
    "SOUND_DASH":         "Ori_Ability_Dash_Execute",
    "SOUND_DOUBLE_JUMP":  "Ori_Ability_DoubleJump_Execute",
    "SOUND_BASH":         "Ori_Ability_Bash_Start",
    "SOUND_BASH_LAUNCH":  "Ori_Ability_Bash_Execute",
    "SOUND_LIGHT_BURST":  "Ori_Ability_LightBurst_Execute",
    "SOUND_BURROW":       "Ori_Ability_Burrow_Start",
    "SOUND_WALL_GRAB":    "Ori_Ability_WallJump_Grab",
    "SOUND_WALL_JUMP":    "Ori_Ability_WallJump_Execute",
    "SOUND_SPIRIT_ARC":   "Ori_Ability_SpiritArc_Execute",
}


class OriAudioEngine:
    def __init__(self, ori_path: Path):
        self._ori_path = ori_path
        self._sounds: Dict[str, Optional[object]] = {}
        self._temp_dir = Path(tempfile.mkdtemp(prefix="pol_audio_"))

    def load_ability_sounds(self) -> None:
        if not _PYGAME_OK:
            return
        bank_dir = self._find_audio_bank_dir()
        if bank_dir is None:
            print("[Audio] Could not locate Ori audio banks — sounds disabled.")
            return
        print(f"[Audio] Found Ori audio banks at: {bank_dir}")
        for key, event_name in _EVENT_MAP.items():
            sound = self._extract_sound(bank_dir, event_name)
            self._sounds[key] = sound

    def _find_audio_bank_dir(self) -> Optional[Path]:
        """Walk the Ori install to find the Wwise bank directory."""
        for root, dirs, files in os.walk(self._ori_path):
            for f in files:
                if f.endswith(".bnk"):
                    return Path(root)
        return None

    def _extract_sound(self, bank_dir: Path, event_name: str) -> Optional[object]:
        """
        Find the .wem file matching event_name inside Ori's audio banks and convert
        it to a playable pygame Sound.

        Wwise .wem files are RIFF containers with Vorbis audio.
        We locate them by matching the Wwise hash of event_name against the bank
        data section, extract the raw bytes, and write them to a temp .wav file
        for pygame.
        """
        target_hash = _wwise_hash(event_name)
        # Search .bnk files for the WEM id, then find the matching loose .wem
        for bnk_file in bank_dir.glob("*.bnk"):
            wem_id = self._find_wem_id_in_bank(bnk_file, target_hash)
            if wem_id is not None:
                wem_path = bank_dir / f"{wem_id}.wem"
                if wem_path.exists():
                    return self._load_wem(wem_path, event_name)
        # Fallback: look for a loose .wem whose filename IS the hash
        direct = bank_dir / f"{target_hash}.wem"
        if direct.exists():
            return self._load_wem(direct, event_name)
        return None

    def _find_wem_id_in_bank(self, bnk_path: Path, target_hash: int) -> Optional[int]:
        """Parse BKHD/HIRC chunks to find the WEM id for target_hash."""
        try:
            data = bnk_path.read_bytes()
            pos = 0
            while pos + 8 <= len(data):
                chunk_id = data[pos:pos+4]
                chunk_len = struct.unpack_from("<I", data, pos+4)[0]
                if chunk_id == b"HIRC":
                    return self._scan_hirc(data, pos+8, chunk_len, target_hash)
                pos += 8 + chunk_len
        except Exception:
            pass
        return None

    def _scan_hirc(self, data: bytes, start: int, length: int, target_hash: int) -> Optional[int]:
        pos = start
        end = start + length
        try:
            num_objects = struct.unpack_from("<I", data, pos)[0]
            pos += 4
            for _ in range(num_objects):
                if pos + 9 > end:
                    break
                obj_type   = data[pos]
                obj_size   = struct.unpack_from("<I", data, pos+1)[0]
                obj_id     = struct.unpack_from("<I", data, pos+5)[0]
                if obj_type == 4:    # Sound object
                    # Sound objects contain a plugin-source section with wem id
                    src_id = struct.unpack_from("<I", data, pos+9)[0] if pos+13 <= end else None
                    if src_id is not None and obj_id == target_hash:
                        return src_id
                pos += 5 + obj_size
        except Exception:
            pass
        return None

    def _load_wem(self, wem_path: Path, name: str) -> Optional[object]:
        if not _PYGAME_OK:
            return None
        try:
            # pygame can't load .wem directly; write as .ogg hint and try
            out = self._temp_dir / f"{name}.ogg"
            out.write_bytes(wem_path.read_bytes())
            return pygame.mixer.Sound(str(out))
        except Exception:
            return None

    def play(self, sound_key: str, volume: float = 0.75) -> None:
        if not _PYGAME_OK:
            return
        sound = self._sounds.get(sound_key)
        if sound is not None:
            sound.set_volume(volume)
            sound.play()
