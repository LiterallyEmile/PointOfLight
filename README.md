# Point of Light

**Ori and the Will of the Wisps movement in Halo Infinite campaign.**

Master Chief loses his Grappleshot and Thruster. In their place: every movement
ability from Ori — Dash, Double Jump, Bash, Light Burst, Burrow, Wall Climb, and
Spirit Arc — with Ori's glowing light trails and ability sounds.

## Required games

| Game | Platform | Notes |
|------|----------|-------|
| Halo Infinite | PC (Xbox App or Steam) | Campaign required |
| Ori and the Will of the Wisps | PC (Steam or Xbox App) | Audio assets |

Pass both paths on the command line, or let the exe auto-detect them.

## Abilities

| Ability | Key (KB+M) | What it does |
|---------|-----------|-------------|
| Dash | Shift | Short directional burst. Air or ground. |
| Double Jump | Space (×2) | Full-height second jump while airborne. |
| Bash | E | Slows time briefly; launch off the nearest enemy in your crosshair direction. |
| Light Burst | Q (hold → release) | Charge and release for an explosive upward launch. |
| Burrow | C | Low-speed ground dash through enemies, brief invincibility. |
| Wall Climb | Auto (press into wall mid-air) | Grab wall, slide slowly, jump off. |
| Spirit Arc | Right Click | Grapple toward aimed point. |

## Ori in Cuphead (what Melty installs)

`melty.json` now installs [Ori in Cuphead](cuphead/README.md): Ori's combat
(Spirit Edge, Arc, Smash, Star, Spear, Light Burst, Regenerate, Bash and energy)
and an Ori skin in Cuphead. The Halo Infinite tool below is still in the repo
and still built by CI as an artifact, but Melty no longer offers it.

## Credits

Movement design and code: Point of Light project  
Ori and the Will of the Wisps: Moon Studios / Xbox Game Studios  
Halo Infinite: 343 Industries / Xbox Game Studios  
Cuphead: Studio MDHR

## License

MIT — remixes welcome.

## Building from source (Windows)

```
pip install -r requirements.txt
python build.py
# output: dist/pointoflight.exe
```
