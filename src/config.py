"""
Point of Light — configuration and keybindings.
"""
from dataclasses import dataclass, field
from typing import Optional
import os

# ---------- keybindings (keyboard) ----------
class Keys:
    DASH        = "shift"       # double-tap or hold
    DOUBLE_JUMP = "space"       # second press while airborne
    BASH        = "e"           # Bash (redirect off nearby target)
    LIGHT_BURST = "q"           # hold to charge, release to launch
    BURROW      = "c"           # while running, dashes through cover
    WALL_CLIMB  = None          # automatic when pressing into a wall mid-air
    SPIRIT_ARC  = "mouse2"      # grapple/arc — replaces Halo grappleshot

# ---------- controller bindings (XInput) ----------
class Buttons:
    DASH        = "LB"          # left bumper
    DOUBLE_JUMP = "A"           # second A press
    BASH        = "RB"
    LIGHT_BURST = "LT"          # hold to charge
    BURROW      = "RS"          # right stick click while sprinting
    SPIRIT_ARC  = "RT"

# ---------- timing (seconds) ----------
class Timing:
    DASH_DURATION        = 0.18
    DASH_COOLDOWN        = 0.45
    DOUBLE_JUMP_WINDOW   = 0.35   # max time after first jump to trigger second
    BASH_SLOWMO_DURATION = 0.40   # slow-motion window while bash aims
    BASH_LAUNCH_SPEED    = 18.0   # m/s launch velocity
    LIGHT_BURST_CHARGE   = 0.55   # hold time for full charge
    LIGHT_BURST_SPEED    = 22.0
    BURROW_DURATION      = 0.22
    WALL_GRAB_TIMEOUT    = 1.2    # max wall-grab before forced jump-off
    WALL_SLIDE_SPEED     = -2.5   # downward velocity while wall-hugging
    SPIRIT_ARC_RANGE     = 12.0   # metres

# ---------- visual ----------
class Visual:
    TRAIL_LENGTH       = 24       # frames of trail history
    TRAIL_BASE_COLOR   = (200, 230, 255)   # cool white-blue
    TRAIL_GLOW_COLOR   = (100, 180, 255)   # outer glow
    TRAIL_ALPHA_START  = 200
    TRAIL_ALPHA_END    = 0
    ABILITY_FLASH_COLOR = (255, 255, 200)
    OVERLAY_FPS        = 60

# ---------- audio ----------
class Audio:
    # Relative paths inside Ori WotW install (Wwise audio bank)
    AUDIO_BANK_SUBPATH = os.path.join("OriBF2", "Data", "StreamingAssets", "Audio", "GeneratedSoundBanks", "Windows")
    # Wwise event IDs for each ability (looked up from Ori's soundbanks)
    SOUND_DASH         = "Ori_Ability_Dash_Execute"
    SOUND_DOUBLE_JUMP  = "Ori_Ability_DoubleJump_Execute"
    SOUND_BASH         = "Ori_Ability_Bash_Start"
    SOUND_BASH_LAUNCH  = "Ori_Ability_Bash_Execute"
    SOUND_LIGHT_BURST  = "Ori_Ability_LightBurst_Execute"
    SOUND_BURROW       = "Ori_Ability_Burrow_Start"
    SOUND_WALL_GRAB    = "Ori_Ability_WallJump_Grab"
    SOUND_WALL_JUMP    = "Ori_Ability_WallJump_Execute"
    SOUND_SPIRIT_ARC   = "Ori_Ability_SpiritArc_Execute"
    VOLUME             = 0.75
