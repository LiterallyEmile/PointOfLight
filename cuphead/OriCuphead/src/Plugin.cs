using System.IO;
using BepInEx;
using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// <summary>
    /// Point of Light for Cuphead: Ori and the Will of the Wisps' combat and look
    /// on player one. Cuphead's own movement, jump, dash and parry are untouched.
    /// </summary>
    [BepInPlugin(Guid, "Point of Light: Ori in Cuphead", Version)]
    [BepInProcess("Cuphead.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.pointoflight.oricuphead";
        public const string Version = "0.1.0";
        const float HintSeconds = 20f;

        PluginConfig _cfg;
        readonly OriInput _input = new OriInput();
        readonly World _world = new World();
        Sfx _sfx;
        OriCombat _combat;
        PlayerLink _player;
        OriRig _rig;
        bool _apiDone, _on = true;
        float _search, _hint;

        void Awake()
        {
            _cfg = new PluginConfig(Config);
            var dir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            Art.Build(dir);
            _sfx = new Sfx(gameObject, dir) { Volume = _cfg.Volume.Value };
            _combat = new OriCombat(_cfg, _world, _sfx);
            Logger.LogInfo("Point of Light loaded — Ori is waiting for a level." + (Art.Custom != null ? " Using skin/ori.png." : ""));
        }

        bool Active { get { return _on && _cfg.Enabled.Value; } }
        bool CombatOn { get { return Active && _cfg.Combat.Value; } }
        bool Ready { get { return _player != null && _player.Alive && _player.Calibrated; } }

        void Update()
        {
            if (!_apiDone)
            {
                _search -= Time.unscaledDeltaTime;
                if (_search > 0f) return;
                _search = 1f;
                _apiDone = GameApi.TryInit(_cfg, Logger);
                if (!_apiDone) return;
                if (!GameApi.Ready) Logger.LogError("Player controller type not found — Point of Light is idle.");
            }
            if (!GameApi.Ready) return;

            _input.Poll(_cfg);
            if (_input.ToggleDown && _cfg.Enabled.Value)
            {
                _on = !_on;
                Logger.LogInfo(_on ? "Ori mode on." : "Ori mode off — Cuphead is back.");
                if (!_on) Detach(false);
            }

            if (_player != null && !_player.Alive) Detach(true);
            if (_player == null)
            {
                if (!Active) return;
                _search -= Time.unscaledDeltaTime;
                if (_search > 0f) return;
                _search = 0.5f;
                var players = GameApi.FindPlayers();
                if (players.Count == 0) return;
                var pick = players[0];
                foreach (var c in players) if (GameApi.IsPlayerOne(c)) { pick = c; break; }
                _player = new PlayerLink(pick);
                _hint = HintSeconds;
            }

            if (_player.Tick(Time.deltaTime, _input.Dir, CombatOn))
                Logger.LogInfo("Ori joined " + _player.Describe());
            if (!_player.Calibrated) return;

            _hint -= Time.unscaledDeltaTime;
            if (CombatOn && !_player.Dead && !GameApi.IsPaused())
            {
                if (_rig == null || _rig.Destroyed) _rig = new OriRig();
                _combat.Update(_player, _input, _rig);
            }
        }

        void LateUpdate()
        {
            if (!Ready) return;
            bool skin = Active && _cfg.Skin.Value && !_player.Dead;
            _player.HideCuphead(skin);

            if (_rig == null || _rig.Destroyed) _rig = new OriRig();
            bool flickerOff = _player.HitFlicker > 0f && ((int)(Time.time / 0.06f) & 1) == 1;
            _rig.SetVisible(skin && !flickerOff);
            if (skin) _rig.Sync(_player, _cfg.SkinScale.Value, _combat.Ducking(_player, _input));

            if (CombatOn && !_player.Dead && !GameApi.IsPaused()) _combat.LateUpdate(_player);
            _world.TickFx(Time.deltaTime);
        }

        void OnGUI()
        {
            if (!Ready || !CombatOn || _player.Dead || !_cfg.Hud.Value) return;
            _combat.DrawHud(_cfg.Hints.Value && _hint > 0f);
        }

        /// Give the player back to Cuphead. `lost` = the level ended or the player was destroyed.
        void Detach(bool lost)
        {
            if (_combat != null) _combat.Reset();
            if (_player != null && !lost) _player.Release();
            if (lost) _player = null;
            if (_rig != null) _rig.SetVisible(false);
        }

        void OnDestroy()
        {
            Detach(false);
        }
    }
}
