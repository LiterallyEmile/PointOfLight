using System.Collections.Generic;
using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// <summary>
    /// Ori and the Will of the Wisps' combat, mapped onto Cuphead's buttons.
    ///   Attack  — the selected weapon: Spirit Edge (3-hit sword combo), Spirit Arc (bow),
    ///             Spirit Smash (hammer) or Spirit Star (boomerang shuriken).
    ///   Ability — costs energy: Spirit Spear; hold Down for Light Burst; hold Up for Regenerate.
    ///   Bash    — freeze time next to a bullet or enemy, aim, and launch off it,
    ///             sending bullets back the other way.
    /// Energy cells refill as Ori deals damage, like Ori's energy orbs.
    /// Sizes and speeds are in player heights (H) so they scale with the game.
    /// </summary>
    internal sealed class OriCombat
    {
        enum Weapon { Edge, Arc, Smash, Star }
        static readonly string[] WeaponNames = { "Spirit Edge", "Spirit Arc", "Spirit Smash", "Spirit Star" };
        const float RegenerateCost = 3f, RegenerateTime = 0.6f;

        sealed class Swing
        {
            public Weapon Kind;
            public Vector2 Dir;
            public float Age, Duration, HitFrom, HitTo, Damage, Reach;
            public Vector2 Size;
            public int Step;
            public bool FxDone;
            public readonly Dictionary<int, float> Hits = new Dictionary<int, float>();
        }

        readonly PluginConfig _cfg;
        readonly World _world;
        readonly Sfx _sfx;

        Weapon _weapon;
        public float Energy;
        float _cooldown, _comboWindow, _regen = -1f, _nameFlash, _energyShake, _trail;
        int _comboStep;
        bool _queued;
        Swing _swing;

        // Bash
        bool _bashing;
        Component _bashTarget;
        bool _bashIsProjectile;
        Vector2 _bashTargetPos, _bashAim = Vector2.up;
        float _bashTimer, _bashCooldown, _launch, _savedScale = 1f;
        Vector2 _launchVel;
        GameObject _bashArrow, _bashRing;

        public OriCombat(PluginConfig cfg, World world, Sfx sfx)
        {
            _cfg = cfg;
            _world = world;
            _sfx = sfx;
            Energy = cfg.MaxEnergy.Value;
            world.OnDamage = (dmg, gains) =>
            {
                _sfx.Play("hit", 0.6f, 0.15f);
                if (gains) Energy = Mathf.Min(_cfg.MaxEnergy.Value, Energy + dmg * _cfg.EnergyPerDamage.Value);
            };
        }

        float Dmg(float d) { return d * _cfg.DamageMultiplier.Value; }

        public bool Ducking(PlayerLink p, OriInput inp) { return p.Grounded && inp.Dir.y < 0f && inp.Dir.x == 0f; }

        Vector2 Aim(PlayerLink p, OriInput inp)
        {
            var d = inp.Dir;
            if (d == Vector2.zero || (p.Grounded && d.y < 0f)) return new Vector2(p.Facing, 0f);
            return d.normalized;
        }

        Vector2 MeleeDir(PlayerLink p, OriInput inp)
        {
            if (inp.Dir.y > 0f) return Vector2.up;
            if (inp.Dir.y < 0f && !p.Grounded) return Vector2.down;
            return new Vector2(p.Facing, 0f);
        }

        // ── per frame ──────────────────────────────────────────────────────

        public void Update(PlayerLink p, OriInput inp, OriRig rig)
        {
            float dt = Time.deltaTime, h = p.H;
            _world.PlayerRoot = p.Root;
            _world.SortingLayer = p.SortingLayer;
            _world.SortingOrder = p.SortingOrder;
            _nameFlash = Mathf.Max(0f, _nameFlash - Time.unscaledDeltaTime);
            _energyShake = Mathf.Max(0f, _energyShake - Time.unscaledDeltaTime);

            UpdateBash(p, inp);
            TickShots(p, dt);       // scaled time, so they crawl during Bash slow-mo
            if (_bashing) return;

            _cooldown -= dt;
            _bashCooldown -= dt;
            _comboWindow -= dt;
            if (_comboWindow <= 0f && _swing == null) _comboStep = 0;

            if (inp.CycleDown)
            {
                _weapon = (Weapon)(((int)_weapon + 1) % WeaponNames.Length);
                _nameFlash = 1.2f;
                _swing = null;
                _sfx.Play("cycle");
            }

            if (_regen >= 0f)
            {
                _regen += dt;
                if (_world.Rand(0f, 1f) < 0.6f)
                {
                    var f = _world.Spawn(Art.Glow, p.Center + new Vector2(_world.Rand(-0.4f, 0.4f) * h, _world.Rand(-0.4f, 0.1f) * h),
                                         0f, Vector2.one * h * 0.15f, OriRig.GlowBlue, 0.6f, 12);
                    f.Vel = new Vector2(0f, h * 1.5f);
                }
                if (_regen >= RegenerateTime) FinishRegenerate(p);
            }

            // Attack
            if (_swing == null && _cooldown <= 0f && (inp.AttackDown || inp.AttackHeld))
                StartAttack(p, inp, rig);
            else if (_swing != null && inp.AttackDown && _swing.Kind == Weapon.Edge && _swing.Step < 2)
                _queued = true;

            if (_swing != null) TickSwing(p, inp, rig, dt);

            if (inp.AbilityDown && _swing == null && _regen < 0f) UseAbility(p, inp);

            // Ori's light trail while moving.
            _trail -= dt;
            if (_trail <= 0f && p.Velocity.sqrMagnitude > h * h * 0.5f)
            {
                _trail = 0.05f;
                var f = _world.Spawn(Art.Glow, p.Center + new Vector2(-p.Facing * 0.15f * h, _world.Rand(-0.3f, 0.2f) * h), 0f,
                                     Vector2.one * h * _world.Rand(0.07f, 0.14f), new Color(0.7f, 0.88f, 1f, 0.8f), 0.5f, -1);
                f.Vel = new Vector2(0f, h * 0.3f);
                f.ScaleTo = f.ScaleFrom * 0.2f;
            }
        }

        /// Runs after Cuphead's motor so the Bash launch isn't overwritten.
        public void LateUpdate(PlayerLink p)
        {
            if (_launch <= 0f) return;
            float dt = Time.deltaTime;
            float total = 0.22f;
            float k = Mathf.Clamp01(_launch / total);
            Vector3 step = _launchVel * (2f * k) * dt;
            var pos = p.Root.position + step;
            Rect view;
            if (World.ViewBounds(out view))
            {
                pos.x = Mathf.Clamp(pos.x, view.xMin + p.H * 0.4f, view.xMax - p.H * 0.4f);
                pos.y = Mathf.Min(pos.y, view.yMax - p.H * 1.1f);
            }
            p.Root.position = pos;
            GameApi.CancelFall(p.Motor);
            _launch -= dt;
            _world.Sparks(p.Center, p.H, 1, OriRig.GlowBlue, 1f);
        }

        // ── melee ──────────────────────────────────────────────────────────

        void StartAttack(PlayerLink p, OriInput inp, OriRig rig)
        {
            float h = p.H;
            switch (_weapon)
            {
                case Weapon.Edge:
                {
                    int step = _comboWindow > 0f ? (_comboStep + 1) % 3 : 0;
                    _comboStep = step;
                    bool finisher = step == 2;
                    _swing = new Swing
                    {
                        Kind = Weapon.Edge, Step = step, Dir = MeleeDir(p, inp),
                        Duration = finisher ? 0.3f : 0.2f, HitFrom = 0.03f, HitTo = 0.12f,
                        Damage = Dmg(finisher ? 18f : 11f), Reach = 0.62f * h,
                        Size = finisher ? new Vector2(1.5f * h, 1.1f * h) : new Vector2(1.25f * h, 0.95f * h),
                    };
                    break;
                }
                case Weapon.Smash:
                    _swing = new Swing
                    {
                        Kind = Weapon.Smash, Dir = MeleeDir(p, inp), Duration = 0.55f, HitFrom = 0.2f, HitTo = 0.3f,
                        Damage = Dmg(32f), Reach = 0.75f * h, Size = new Vector2(1.7f * h, 1.4f * h),
                    };
                    rig.Attack(1f);   // wind the hammer up overhead
                    break;
                case Weapon.Arc:
                {
                    var aim = Aim(p, inp);
                    var s = _world.Fire("arc", Art.Spear, p.Center + aim * 0.35f * h, aim * 17f * h,
                                        new Vector2(0.6f * h, 0.11f * h), new Color(0.75f, 0.9f, 1f), 0.13f * h, Dmg(6f), 1.5f);
                    s.GainsEnergy = true;
                    _cooldown = 0.24f;
                    rig.Attack(aim.y);
                    _sfx.Play("arc");
                    break;
                }
                case Weapon.Star:
                {
                    int stars = 0;
                    foreach (var x in _world.Shots) if (x.Kind == "star") stars++;
                    if (stars >= 2) { _cooldown = 0.05f; break; }
                    var aim = Aim(p, inp);
                    var s = _world.Fire("star", Art.Star, p.Center + aim * 0.3f * h, aim * 11f * h,
                                        new Vector2(0.5f * h, 0.5f * h), Color.white, 0.3f * h, Dmg(3f), 3f);
                    s.Returns = true;
                    s.TickRate = 0.12f;
                    s.GainsEnergy = true;
                    s.Spin = -900f;
                    s.FaceVelocity = false;
                    _cooldown = 0.3f;
                    rig.Attack(aim.y);
                    _sfx.Play("star");
                    break;
                }
            }
        }

        void TickSwing(PlayerLink p, OriInput inp, OriRig rig, float dt)
        {
            var s = _swing;
            float h = p.H;
            s.Age += dt;
            if (!s.FxDone && s.Age >= s.HitFrom)
            {
                s.FxDone = true;
                float angle = Mathf.Atan2(s.Dir.y, s.Dir.x) * Mathf.Rad2Deg;
                Vector2 at = p.Center + s.Dir * s.Reach * 0.55f;
                bool smash = s.Kind == Weapon.Smash;
                float w = smash ? 2.0f * h : (s.Step == 2 ? 1.65f * h : 1.4f * h);
                var fx = _world.Spawn(Art.Slash, at, angle, new Vector2(w, w * (s.Step == 1 ? -1f : 1f)),
                                      smash ? new Color(0.65f, 0.85f, 1f) : new Color(0.85f, 0.95f, 1f), smash ? 0.22f : 0.14f, 11);
                fx.ScaleTo = fx.ScaleFrom * 1.15f;
                if (smash)
                {
                    var ring = _world.Spawn(Art.Ring, p.Center + s.Dir * s.Reach, 0f, Vector2.one * h * 0.4f, OriRig.GlowBlue, 0.35f, 10);
                    ring.ScaleTo = Vector2.one * h * 2.4f;
                    _sfx.Play("smash");
                }
                else _sfx.Play("edge", s.Step == 2 ? 1.1f : 0.9f);
                rig.Attack(s.Dir.y);
            }
            if (s.Age >= s.HitFrom && s.Age <= s.HitTo)
            {
                bool vertical = s.Dir.x == 0f;
                Vector2 size = vertical ? new Vector2(s.Size.y, s.Size.x) : s.Size;
                Vector2 center = p.Center + s.Dir * s.Reach + (vertical ? new Vector2(p.Facing * 0.1f * h, 0f) : Vector2.zero);
                int n = _world.Apply(_world.Box(center, size, 0f), s.Damage, p.Center, s.Hits, 0f, true, h);
                // Ori's downward strike bounces off what it hits.
                if (n > 0 && s.Dir.y < 0f) _launchVel = Vector2.up * (1.2f * h / 0.22f);
                if (n > 0 && s.Dir.y < 0f) _launch = 0.22f;
            }
            if (s.Age >= s.Duration)
            {
                _swing = null;
                _comboWindow = 0.32f;
                if (s.Kind == Weapon.Smash) _cooldown = 0.1f;
                if (s.Kind == Weapon.Edge && s.Step == 2) { _cooldown = 0.15f; _comboWindow = 0f; }
                if (_queued) { _queued = false; StartAttack(p, inp, rig); }
            }
        }

        // ── abilities ──────────────────────────────────────────────────────

        bool Spend(float cost)
        {
            if (Energy + 1e-4f < cost) { _energyShake = 0.3f; _sfx.Play("empty"); return false; }
            Energy -= cost;
            return true;
        }

        void UseAbility(PlayerLink p, OriInput inp)
        {
            float h = p.H;
            if (inp.Dir.y > 0f && inp.Dir.x == 0f)
            {
                // Regenerate
                int? hp = GameApi.GetHealth(p.Stats), max = GameApi.GetMaxHealth(p.Stats);
                if (!hp.HasValue) { GameApi.WarnOnce("regen", "Regenerate needs the player's health, which wasn't found."); _sfx.Play("empty"); return; }
                if (hp.Value >= (max ?? 3)) { _sfx.Play("empty"); return; }
                if (!Spend(Mathf.Min(RegenerateCost, _cfg.MaxEnergy.Value))) return;
                _regen = 0f;
                _sfx.Play("heal");
                return;
            }
            if (inp.Dir.y < 0f)
            {
                // Light Burst: lobbed orb of light that explodes.
                if (!Spend(1f)) return;
                var aim = Aim(p, inp);
                var vel = new Vector2((aim.x != 0f ? aim.x : p.Facing) * 7f * h, 8f * h);
                var s = _world.Fire("burst", Art.Glow, p.Center + new Vector2(p.Facing * 0.3f * h, 0.2f * h), vel,
                                    Vector2.one * 0.55f * h, new Color(0.8f, 0.93f, 1f), 0.25f * h, 0f, 1.1f);
                s.Gravity = 20f * h;
                s.Explodes = true;
                s.ExplodeRadius = 1.5f * h;
                s.ExplodeDamage = Dmg(35f);
                s.FloorY = p.Feet.y - 0.2f * h;
                s.FaceVelocity = false;
                _sfx.Play("bash_launch", 0.5f);
                return;
            }
            // Spirit Spear: heavy piercing throw.
            if (!Spend(1f)) return;
            var dir = Aim(p, inp);
            var spear = _world.Fire("spear", Art.Spear, p.Center + dir * 0.4f * h, dir * 21f * h,
                                    new Vector2(1.35f * h, 0.24f * h), new Color(0.85f, 0.95f, 1f), 0.2f * h, Dmg(45f), 1.4f);
            spear.Pierce = true;
            _world.Sparks(p.Center + dir * 0.4f * h, h, 6, OriRig.GlowBlue);
            _sfx.Play("spear");
        }

        void FinishRegenerate(PlayerLink p)
        {
            _regen = -1f;
            int? hp = GameApi.GetHealth(p.Stats);
            if (hp.HasValue && GameApi.SetHealth(p.Stats, hp.Value + 1))
            {
                var ring = _world.Spawn(Art.Ring, p.Center, 0f, Vector2.one * p.H * 0.3f, OriRig.GlowBlue, 0.5f, 10);
                ring.ScaleTo = Vector2.one * p.H * 1.8f;
                _world.Sparks(p.Center, p.H, 12, OriRig.Light, 2f);
            }
            else GameApi.WarnOnce("sethp", "Regenerate couldn't set the player's health.");
        }

        // ── projectiles ────────────────────────────────────────────────────

        void TickShots(PlayerLink p, float dt)
        {
            float h = p.H;
            Rect view;
            bool haveView = World.ViewBounds(out view);
            var shots = _world.Shots;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                if (s.Go == null) { shots.RemoveAt(i); continue; }
                s.Age += dt;
                bool dead = s.Age >= s.Life;

                if (s.Returns)
                {
                    // Spirit Star: fly out, slow down, then home back to Ori.
                    float outTime = 0.45f;
                    if (s.Age < outTime) s.Vel *= Mathf.Max(0f, 1f - dt * 3f);
                    else
                    {
                        var to = p.Center - s.Pos;
                        if (to.magnitude < 0.35f * h) dead = true;
                        s.Vel = Vector2.Lerp(s.Vel, to.normalized * 14f * h, dt * 8f);
                    }
                }
                s.Vel += Vector2.down * s.Gravity * dt;
                s.Pos += s.Vel * dt;
                s.Go.transform.position = new Vector3(s.Pos.x, s.Pos.y, s.Go.transform.position.z);
                if (s.FaceVelocity && s.Vel.sqrMagnitude > 0f)
                    s.Go.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(s.Vel.y, s.Vel.x) * Mathf.Rad2Deg);
                if (s.Spin != 0f) s.Go.transform.Rotate(0f, 0f, s.Spin * dt);

                s.TrailTimer -= dt;
                if (s.TrailTimer <= 0f)
                {
                    s.TrailTimer = 0.03f;
                    var t = _world.Spawn(Art.Glow, s.Pos, 0f, Vector2.one * h * (s.Kind == "spear" ? 0.3f : 0.18f),
                                         new Color(0.6f, 0.82f, 1f, 0.6f), 0.25f, 9);
                    t.ScaleTo = t.ScaleFrom * 0.2f;
                }

                var cols = _world.Circle(s.Pos, s.Radius);
                if (s.Explodes)
                {
                    bool touched = false;
                    foreach (var c in cols) if (GameApi.ReceiverFor(c, p.Root) != null) { touched = true; break; }
                    if (touched || dead || s.Pos.y < s.FloorY) { Explode(s, h); dead = true; }
                }
                else if (s.Damage > 0f)
                {
                    int n = _world.Apply(cols, s.Damage, s.Pos, s.Hit, s.TickRate, s.GainsEnergy, h);
                    if (n > 0 && !s.Pierce && !s.Returns) dead = true;
                }

                if (haveView && !s.Returns && !s.Explodes)
                {
                    float m = h;
                    if (s.Pos.x < view.xMin - m || s.Pos.x > view.xMax + m || s.Pos.y < view.yMin - m || s.Pos.y > view.yMax + m) dead = true;
                }
                if (dead)
                {
                    if (!s.Explodes) _world.Sparks(s.Pos, h, 3, OriRig.Light, 1.5f);
                    Object.Destroy(s.Go);
                    shots.RemoveAt(i);
                }
            }
        }

        void Explode(Shot s, float h)
        {
            var hits = new Dictionary<int, float>();
            _world.Apply(_world.Circle(s.Pos, s.ExplodeRadius), s.ExplodeDamage, s.Pos, hits, 0f, false, h);
            var ring = _world.Spawn(Art.Ring, s.Pos, 0f, Vector2.one * h * 0.3f, new Color(0.85f, 0.95f, 1f), 0.4f, 12);
            ring.ScaleTo = Vector2.one * s.ExplodeRadius * 2f;
            var glow = _world.Spawn(Art.Glow, s.Pos, 0f, Vector2.one * s.ExplodeRadius * 2.4f, new Color(0.6f, 0.85f, 1f, 0.9f), 0.35f, 11);
            glow.ScaleTo = glow.ScaleFrom * 0.6f;
            _world.Sparks(s.Pos, h, 14, OriRig.Light, 4f);
            _sfx.Play("burst");
        }

        // ── Bash ───────────────────────────────────────────────────────────

        void UpdateBash(PlayerLink p, OriInput inp)
        {
            float slow = _cfg.BashSlowMo.Value;
            if (!_bashing)
            {
                // If something paused mid-Bash and handed our slow-mo back, undo it.
                if (Mathf.Abs(Time.timeScale - slow) < 1e-4f && _savedScale > slow) Time.timeScale = _savedScale;
                if (inp.BashDown && _bashCooldown <= 0f && _swing == null) TryStartBash(p);
                return;
            }

            _bashTimer += Time.unscaledDeltaTime;
            if (_bashTarget != null) _bashTargetPos = _bashTarget.transform.position;
            if (inp.Dir != Vector2.zero) _bashAim = inp.Dir.normalized;

            float h = p.H;
            float pulse = 1f + Mathf.Sin(_bashTimer * 14f) * 0.08f;
            _bashRing.transform.position = new Vector3(_bashTargetPos.x, _bashTargetPos.y, p.Root.position.z);
            SetSize(_bashRing, Art.Ring, Vector2.one * h * 1.2f * pulse);
            float ang = Mathf.Atan2(_bashAim.y, _bashAim.x) * Mathf.Rad2Deg;
            _bashArrow.transform.position = (Vector3)(p.Center + _bashAim * 0.8f * h) + new Vector3(0f, 0f, p.Root.position.z);
            _bashArrow.transform.rotation = Quaternion.Euler(0f, 0f, ang);

            if (inp.BashUp || !inp.BashHeld || _bashTimer > 1.25f) ExecuteBash(p);
        }

        void TryStartBash(PlayerLink p)
        {
            float h = p.H, best = float.MaxValue;
            Component target = null;
            bool isProjectile = false;
            foreach (var col in _world.Circle(p.Center, 2.0f * h))
            {
                var proj = GameApi.ProjectileFor(col, p.Root);
                if (proj != null)
                {
                    float d = ((Vector2)proj.transform.position - p.Center).sqrMagnitude;
                    // Bullets always beat enemies, like in Ori.
                    if (!isProjectile || d < best) { best = d; target = proj; isProjectile = true; }
                    continue;
                }
                if (isProjectile) continue;
                var r = GameApi.ReceiverFor(col, p.Root);
                if (r == null) continue;
                Vector2 near = col.bounds.ClosestPoint(p.Center);
                float dr = (near - p.Center).sqrMagnitude;
                if (dr < best && dr < (1.4f * h) * (1.4f * h)) { best = dr; target = r; _bashTargetPos = near; }
            }
            if (target == null) return;

            _bashing = true;
            _bashTarget = target;
            _bashIsProjectile = isProjectile;
            if (isProjectile) _bashTargetPos = target.transform.position;
            _bashTimer = 0f;
            var away = p.Center - _bashTargetPos;
            _bashAim = away.sqrMagnitude > 1e-6f ? away.normalized : Vector2.up;
            _savedScale = Time.timeScale > _cfg.BashSlowMo.Value ? Time.timeScale : 1f;
            Time.timeScale = _cfg.BashSlowMo.Value;
            _swing = null;

            _bashRing = MakeGo(Art.Ring, OriRig.GlowBlue, 14);
            _bashArrow = MakeGo(Art.Spear, new Color(1f, 1f, 1f, 0.85f), 14);
            SetSize(_bashArrow, Art.Spear, new Vector2(0.9f * h, 0.2f * h));
            _sfx.Play("bash_start", 1f, 0f);
        }

        void ExecuteBash(PlayerLink p)
        {
            _bashing = false;
            if (Mathf.Abs(Time.timeScale - _cfg.BashSlowMo.Value) < 1e-4f) Time.timeScale = _savedScale;
            if (_bashRing != null) Object.Destroy(_bashRing);
            if (_bashArrow != null) Object.Destroy(_bashArrow);
            _bashCooldown = 0.3f;

            float h = p.H;
            var aim = _bashAim;
            // Never drive Ori into the floor; down-Bashes just push sideways.
            var launch = new Vector2(aim.x, p.Grounded ? Mathf.Max(0f, aim.y) : aim.y);
            _launchVel = launch * (3.4f * h / 0.22f);
            _launch = 0.22f;
            GameApi.CancelFall(p.Motor);

            if (_bashIsProjectile)
            {
                if (_bashTarget != null) Object.Destroy(_bashTarget.gameObject);
                var orb = _world.Fire("bash", Art.Glow, _bashTargetPos, -aim * 18f * h, Vector2.one * 0.6f * h,
                                      new Color(0.75f, 0.92f, 1f), 0.3f * h, Dmg(30f), 2f);
                orb.FaceVelocity = false;
                Energy = Mathf.Min(_cfg.MaxEnergy.Value, Energy + 0.3f);
            }
            else if (_bashTarget != null)
            {
                foreach (var col in _world.Circle(_bashTargetPos, 0.2f * h))
                {
                    var r = GameApi.ReceiverFor(col, p.Root);
                    if (r != null && GameApi.Damage(col, r, Dmg(12f), p.Center)) break;
                }
            }
            var ring = _world.Spawn(Art.Ring, _bashTargetPos, 0f, Vector2.one * h * 0.5f, Color.white, 0.3f, 12);
            ring.ScaleTo = Vector2.one * h * 2f;
            _world.Sparks(_bashTargetPos, h, 10, OriRig.Light, 3f);
            _bashTarget = null;
            _sfx.Play("bash_launch");
        }

        GameObject MakeGo(Sprite s, Color c, int boost)
        {
            var go = new GameObject("Ori.Bash");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.color = c;
            sr.sortingLayerID = _world.SortingLayer;
            sr.sortingOrder = _world.SortingOrder + boost;
            return go;
        }

        static void SetSize(GameObject go, Sprite s, Vector2 size)
        {
            var n = s.bounds.size;
            go.transform.localScale = new Vector3(size.x / n.x, size.y / n.y, 1f);
        }

        /// Leave no slow-motion or stray objects behind (scene change, toggle off).
        public void Reset()
        {
            if (_bashing && Mathf.Abs(Time.timeScale - _cfg.BashSlowMo.Value) < 1e-4f) Time.timeScale = _savedScale;
            _bashing = false;
            if (_bashRing != null) Object.Destroy(_bashRing);
            if (_bashArrow != null) Object.Destroy(_bashArrow);
            _bashTarget = null;
            _swing = null;
            _launch = 0f;
            _regen = -1f;
            _world.Clear();
        }

        // ── HUD ────────────────────────────────────────────────────────────

        GUIStyle _label, _hint;

        public void DrawHud(bool hints)
        {
            float s = Screen.height / 1080f;
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _hint = new GUIStyle(GUI.skin.label);
            }
            _label.fontSize = Mathf.RoundToInt(26 * s);
            _hint.fontSize = Mathf.RoundToInt(16 * s);

            float x = 28f * s, y = 24f * s, size = 38f * s, gap = 10f * s;
            if (_energyShake > 0f) x += Mathf.Sin(Time.unscaledTime * 90f) * 6f * s * (_energyShake / 0.3f);
            int cells = Mathf.Max(1, Mathf.RoundToInt(_cfg.MaxEnergy.Value));
            var prev = GUI.color;
            for (int i = 0; i < cells; i++)
            {
                var r = new Rect(x + i * (size + gap), y, size, size);
                float fill = Mathf.Clamp01(Energy - i);
                GUI.color = new Color(0.1f, 0.2f, 0.4f, 0.75f);
                GUI.DrawTexture(r, Art.Disc.texture);
                if (fill > 0f)
                {
                    float inset = (1f - fill) * size * 0.5f + 4f * s;
                    GUI.color = fill >= 1f ? new Color(0.55f, 0.85f, 1f, 1f) : new Color(0.45f, 0.7f, 1f, 0.8f);
                    GUI.DrawTexture(new Rect(r.x + inset, r.y + inset, r.width - inset * 2f, r.height - inset * 2f), Art.Disc.texture);
                    if (fill >= 1f)
                    {
                        GUI.color = new Color(0.6f, 0.85f, 1f, 0.5f);
                        GUI.DrawTexture(new Rect(r.x - size * 0.3f, r.y - size * 0.3f, size * 1.6f, size * 1.6f), Art.Glow.texture);
                    }
                }
            }

            string name = WeaponNames[(int)_weapon];
            float flash = Mathf.Clamp01(_nameFlash);
            var labelRect = new Rect(x, y + size + 8f * s, 600f * s, 40f * s);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(labelRect.x + 2f * s, labelRect.y + 2f * s, labelRect.width, labelRect.height), name, _label);
            GUI.color = Color.Lerp(new Color(0.85f, 0.95f, 1f), new Color(1f, 1f, 0.75f), flash);
            GUI.Label(labelRect, name, _label);

            if (hints)
            {
                string line = string.Format("{0} attack   {1} switch   {2} Spear  (↓ Light Burst, ↑ Regenerate)   hold {3} Bash   {4} Cuphead",
                    _cfg.AttackKey.Value, _cfg.CycleKey.Value, _cfg.AbilityKey.Value, _cfg.BashKey.Value, _cfg.Toggle.Value);
                var hr = new Rect(x, labelRect.y + 34f * s, 1400f * s, 30f * s);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.Label(new Rect(hr.x + 1f, hr.y + 1f, hr.width, hr.height), line, _hint);
                GUI.color = new Color(0.85f, 0.92f, 1f, 0.85f);
                GUI.Label(hr, line, _hint);
            }
            GUI.color = prev;
        }
    }
}
