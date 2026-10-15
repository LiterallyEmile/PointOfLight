# Point of Light: Ori in Cuphead

**Ori and the Will of the Wisps' combat and Ori's look, in Cuphead.**

Player one becomes Ori. Cuphead's finger-gun is replaced with Ori's weapon
wheel, an energy meter and Bash. Movement, jump, dash and parry stay
Cuphead's, so every level is still beatable.

This is a [BepInEx 5](https://docs.bepinex.dev/) plugin (`OriCuphead.dll`).
It doesn't touch the Halo tool in the root of this repo.

## Controls

| Action | Keyboard | Xbox pad | What it does |
|--------|----------|----------|--------------|
| Attack | X *(Shoot)* | X | Uses the selected weapon. Hold to keep attacking. |
| Switch weapon | Tab | Y | Spirit Edge → Spirit Arc → Spirit Smash → Spirit Star |
| Ability | V *(EX)* | B | **Spirit Spear**, costs 1 energy |
| Ability + Down | ↓ + V | ↓ + B | **Light Burst**, costs 1 energy |
| Ability + Up | ↑ + V | ↑ + B | **Regenerate**, costs 3 energy, heals 1 HP |
| Bash | hold B | hold RB | Freezes time next to a bullet or enemy. Aim, then let go to launch. |
| Ori ↔ Cuphead | F8 | | Switches back to vanilla Cuphead without restarting. |

Aim with the arrow keys or the left stick. Every binding can be changed in the config.

## Weapons and abilities

| Ori ability | In Cuphead |
|-------------|------------|
| **Spirit Edge** | 3-hit sword combo. The third hit is a bigger finisher. Hold Up to slash upward. In the air, hold Down for a down-slash that bounces Ori off whatever it hits. |
| **Spirit Arc** | Fast light arrows, 8-way aim. Safe ranged damage, lower DPS. |
| **Spirit Smash** | Slow hammer swing with a huge hitbox, a shockwave and the highest DPS. |
| **Spirit Star** | Throws a spinning star that flies out, slows down and homes back to Ori, hitting everything it passes several times. Up to two at once. |
| **Spirit Spear** | A heavy piercing throw that goes through every enemy in its path. |
| **Light Burst** | A lobbed orb of light that explodes on contact or when it lands. |
| **Regenerate** | Channels for 0.6 s, then heals 1 HP. |
| **Bash** | Time slows almost to a stop. Ori launches the way you aim, and a bashed bullet flies back the opposite way as a big light orb. Bashing an enemy hurts it. Bashing a bullet gives back some energy. |
| **Energy** | 3 cells, shown top-left. They refill as Ori deals damage, like Ori's energy orbs. |

Damage is tuned to be close to Cuphead's Peashooter. Melee does more because
you have to get close. `DamageMultiplier` in the config scales all of it.

## The Ori skin

Ori is drawn in code from simple shapes: big pointed ears, a swaying tail,
tiny limbs and a soft blue glow. Ori runs, leans, tucks in the air, squashes
when ducking and slashes with the attack, and leaves a light trail while
moving. No art from Ori and the Will of the Wisps is shipped or extracted.

**Your own art:** put a PNG at `BepInEx/plugins/OriCuphead/skin/ori.png`
(feet at the bottom edge, facing right). It replaces the drawn Ori and still
bobs, squashes and flips.

**Your own sounds:** the sounds are synthesised. To replace one, put a 16-bit
PCM `.wav` in `BepInEx/plugins/OriCuphead/sounds/`, named after the sound it
replaces: `edge`, `smash`, `arc`, `star`, `spear`, `burst`, `bash_start`,
`bash_launch`, `heal`, `hit`, `empty` or `cycle`.

## Install

### Through Melty (easiest)

Install **Point of Light** in Melty and launch it. The launcher does the rest:

1. It finds your Cuphead folder, through Steam or GOG.
2. It installs BepInEx 5 there, choosing 32- or 64-bit to match your game.
3. It copies the Ori plugin into `BepInEx/plugins/OriCuphead/`.
4. It starts Cuphead and waits for it to close.
5. It removes the Ori plugin again, so starting Cuphead outside Melty gives you vanilla Cuphead.
   BepInEx itself stays installed; with no plugins it changes nothing.

Ori shows up about a second after any run 'n gun or boss level starts.

### By hand

1. Install BepInEx 5 (not 6) into your Cuphead folder, the one with
   `Cuphead.exe`. Get the x64 build, unless Task Manager shows the game as
   "Cuphead (32 bit)", in which case get x86. Run the game once, then close it.
2. Build the plugin against your own game. This needs the [.NET SDK](https://dotnet.microsoft.com/download), 6 or newer.
   ```
   cd cuphead/OriCuphead
   dotnet build -c Release -p:CupheadDir="C:\Program Files (x86)\Steam\steamapps\common\Cuphead"
   ```
   This copies `OriCuphead.dll` into `BepInEx/plugins/OriCuphead/`. It stays
   there until you delete it, so every normal launch has Ori.

Settings are in `BepInEx/config/com.pointoflight.oricuphead.cfg`, which is
created after the first run.

### How CI builds it without Cuphead

GitHub Actions doesn't have Cuphead's files. Instead, it compiles against
Unity 2017.4 reference DLLs and BepInEx 5 from BepInEx's NuGet feed. It then
runs `tools/Retarget`, which points every Unity reference at plain
`UnityEngine.dll`. Every Unity version ships that file, so the plugin loads
whatever Unity version Cuphead is built on. The build fails if any Unity call
the plugin makes can't be resolved.

## How it hooks in, and what to do if something doesn't work

The plugin finds Cuphead's classes by name at runtime: `LevelPlayerController`,
`LevelPlayerWeaponManager`, `DamageReceiver`, `DamageDealer`,
`AbstractProjectile`, `PlayerStatsManager` and `LevelPlayerMotor`. It doesn't
use compile-time Harmony patches, so a game update can only switch off the
feature that lost its class. The game won't crash. Here is what it does:

- It turns off the player's weapon-manager component. That's how the finger-gun stops firing.
- It hides Cuphead's sprites and draws Ori in their place.
- It hits enemies by sweeping Ori's hitboxes over the colliders in the level
  and calling Cuphead's own damage code. Boss HP, phases and grading keep working.

**This has not been play-tested in Cuphead yet.** It builds against Unity
and .NET 3.5, but the class and member names come from Cuphead's
decompiled code and haven't been checked against a live game. On startup the
plugin writes what it found to `BepInEx/LogOutput.log`. It logs lines such as
`Found player controller: …`, `Damage path: …` and `Ori joined player …`, and
a warning for each class it couldn't find. If something doesn't work, that log
says why. A class name that changed can be fixed under `[Advanced]` in the
config without rebuilding.

Known limits:
- Only player one becomes Ori. In co-op, player two stays as they are.
- Plane (shmup) levels and the overworld map are left unchanged.
- Cuphead's EX and Super are replaced by Ori's abilities, so cards do nothing while Ori mode is on.
