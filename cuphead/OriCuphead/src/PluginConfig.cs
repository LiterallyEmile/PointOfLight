using BepInEx.Configuration;
using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// BepInEx/config/com.pointoflight.oricuphead.cfg — edit while the game is closed.
    internal sealed class PluginConfig
    {
        public readonly ConfigEntry<bool> Enabled, Skin, Combat, Hud, Hints;
        public readonly ConfigEntry<KeyCode> Toggle;
        public readonly ConfigEntry<float> DamageMultiplier, MaxEnergy, EnergyPerDamage, SkinScale, Volume, BashSlowMo;
        public readonly ConfigEntry<KeyCode> AttackKey, AttackPad, AbilityKey, AbilityPad, CycleKey, CyclePad, BashKey, BashPad;
        public readonly ConfigEntry<KeyCode> Left, Right, Up, Down;
        public readonly ConfigEntry<string> PlayerControllerType, WeaponManagerType, StatsType, MotorType,
                                            DamageReceiverType, DamageDealerType, ProjectileType;

        public PluginConfig(ConfigFile f)
        {
            Enabled  = f.Bind("General", "Enabled", true, "Turn the whole mod on or off.");
            Toggle   = f.Bind("General", "ToggleKey", KeyCode.F8, "Press in a level to switch between Ori and Cuphead.");
            Skin     = f.Bind("General", "OriSkin", true, "Replace the player's look with Ori.");
            Combat   = f.Bind("General", "OriCombat", true, "Replace the finger-gun with Ori's weapons, energy and Bash.");
            Hud      = f.Bind("General", "ShowHud", true, "Show Ori's energy cells and the current weapon.");
            Hints    = f.Bind("General", "ShowControls", true, "Show a one-line controls reminder under the energy cells.");
            SkinScale = f.Bind("General", "OriSize", 1.0f, "Ori's height relative to Cuphead's.");
            Volume   = f.Bind("General", "Volume", 0.7f, "Ori sound effect volume (0-1).");

            DamageMultiplier = f.Bind("Balance", "DamageMultiplier", 1.0f, "Scales every Ori attack. 1.0 is tuned to roughly match Cuphead's Peashooter.");
            MaxEnergy        = f.Bind("Balance", "EnergyCells", 3f, "How many energy cells Ori has. Spirit Spear and Light Burst cost 1; Regenerate costs 3.");
            EnergyPerDamage  = f.Bind("Balance", "EnergyPerDamage", 0.008f, "Energy cells gained per point of damage dealt.");
            BashSlowMo       = f.Bind("Balance", "BashTimeScale", 0.08f, "Game speed while aiming a Bash.");

            AttackKey  = f.Bind("Controls", "Attack", KeyCode.X, "Use the selected weapon (Cuphead's Shoot key by default).");
            AttackPad  = f.Bind("Controls", "AttackPad", KeyCode.JoystickButton2, "Controller: X on Xbox.");
            AbilityKey = f.Bind("Controls", "Ability", KeyCode.V, "Spirit Spear. Hold Down for Light Burst, Up for Regenerate (Cuphead's EX key by default).");
            AbilityPad = f.Bind("Controls", "AbilityPad", KeyCode.JoystickButton1, "Controller: B on Xbox.");
            CycleKey   = f.Bind("Controls", "CycleWeapon", KeyCode.Tab, "Cycle Spirit Edge → Spirit Arc → Spirit Smash → Spirit Star.");
            CyclePad   = f.Bind("Controls", "CycleWeaponPad", KeyCode.JoystickButton3, "Controller: Y on Xbox.");
            BashKey    = f.Bind("Controls", "Bash", KeyCode.B, "Hold near a bullet or enemy to Bash, aim, release to launch.");
            BashPad    = f.Bind("Controls", "BashPad", KeyCode.JoystickButton5, "Controller: RB on Xbox.");
            Left  = f.Bind("Controls", "AimLeft", KeyCode.LeftArrow, "Keyboard aim (the left stick is read too).");
            Right = f.Bind("Controls", "AimRight", KeyCode.RightArrow, "");
            Up    = f.Bind("Controls", "AimUp", KeyCode.UpArrow, "");
            Down  = f.Bind("Controls", "AimDown", KeyCode.DownArrow, "");

            const string adv = "Only change these if the log says a type wasn't found after a game update.";
            PlayerControllerType = f.Bind("Advanced", "PlayerControllerType", "LevelPlayerController", adv);
            WeaponManagerType    = f.Bind("Advanced", "WeaponManagerType", "LevelPlayerWeaponManager", adv);
            StatsType            = f.Bind("Advanced", "StatsType", "PlayerStatsManager", adv);
            MotorType            = f.Bind("Advanced", "MotorType", "LevelPlayerMotor", adv);
            DamageReceiverType   = f.Bind("Advanced", "DamageReceiverType", "DamageReceiver", adv);
            DamageDealerType     = f.Bind("Advanced", "DamageDealerType", "DamageDealer", adv);
            ProjectileType       = f.Bind("Advanced", "ProjectileType", "AbstractProjectile", adv);
        }
    }
}
