using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// <summary>
    /// The only place that touches Cuphead's own code. Everything is looked up by
    /// name through reflection, so if a game update renames something, one feature
    /// switches off and logs why instead of the whole plugin failing to load.
    /// Type names can be overridden in the config file without recompiling.
    /// </summary>
    internal static class GameApi
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static |
                                 BindingFlags.Public | BindingFlags.NonPublic |
                                 BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy;

        static ManualLogSource _log;
        static readonly HashSet<string> _warned = new HashSet<string>();

        public static bool Ready { get; private set; }
        public static Type PlayerController, WeaponManager, Stats, Motor;
        public static Type DamageReceiver, DamageReceiverChild, DamageDealer, DamageInfo, DamageSource;
        public static Type Projectile, PauseManager;

        // ── setup ──────────────────────────────────────────────────────────

        public static bool TryInit(PluginConfig cfg, ManualLogSource log)
        {
            _log = log;
            Assembly game = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetName().Name == "Assembly-CSharp") { game = asm; break; }
            if (game == null) return false;

            Type[] types;
            try { types = game.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }

            Func<string, Type> find = name =>
            {
                if (string.IsNullOrEmpty(name)) return null;
                var t = game.GetType(name, false);
                if (t != null) return t;
                foreach (var x in types)
                    if (x != null && (x.Name == name || x.FullName == name)) return x;
                return null;
            };

            PlayerController    = find(cfg.PlayerControllerType.Value);
            WeaponManager       = find(cfg.WeaponManagerType.Value);
            Stats               = find(cfg.StatsType.Value);
            Motor               = find(cfg.MotorType.Value);
            DamageReceiver      = find(cfg.DamageReceiverType.Value);
            DamageReceiverChild = find(cfg.DamageReceiverType.Value + "Child");
            DamageDealer        = find(cfg.DamageDealerType.Value);
            Projectile          = find(cfg.ProjectileType.Value);
            PauseManager        = find("PauseManager");
            if (DamageDealer != null)
            {
                DamageInfo   = DamageDealer.GetNestedType("DamageInfo", Any);
                DamageSource = DamageDealer.GetNestedType("DamageSource", Any);
            }
            if (DamageInfo == null)   DamageInfo   = find("DamageInfo");
            if (DamageSource == null) DamageSource = find("DamageSource");

            Report("player controller", PlayerController, cfg.PlayerControllerType.Value, "nothing will happen");
            Report("weapon manager", WeaponManager, cfg.WeaponManagerType.Value, "Cuphead's finger-gun will keep firing alongside Ori's weapons");
            Report("player stats", Stats, cfg.StatsType.Value, "Regenerate and hit-flicker are disabled");
            Report("player motor", Motor, cfg.MotorType.Value, "Bash won't cancel falling speed");
            Report("damage receiver", DamageReceiver, cfg.DamageReceiverType.Value, "Ori's attacks can't hurt anything");
            Report("damage dealer", DamageDealer, cfg.DamageDealerType.Value, "falling back to DamageReceiver.TakeDamage");
            Report("projectile", Projectile, cfg.ProjectileType.Value, "Bash only works on enemies, not bullets");

            Ready = PlayerController != null;
            return true;
        }

        static void Report(string what, Type t, string name, string consequence)
        {
            if (t != null) _log.LogInfo($"Found {what}: {t.FullName}");
            else _log.LogWarning($"Couldn't find {what} type '{name}' — {consequence}. Fix the name under [Advanced] in the config.");
        }

        public static void WarnOnce(string key, string message)
        {
            if (_warned.Add(key)) _log.LogWarning(message);
        }

        // ── generic reflection helpers ─────────────────────────────────────

        static MemberInfo FindMember(Type t, string name)
        {
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                const BindingFlags Here = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var p in cur.GetProperties(Here))
                    if (p.GetIndexParameters().Length == 0 && p.Name == name) return p;
                foreach (var f in cur.GetFields(Here))
                    if (f.Name == name) return f;
            }
            // second pass: case-insensitive
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                const BindingFlags Here = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var p in cur.GetProperties(Here))
                    if (p.GetIndexParameters().Length == 0 && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
                foreach (var f in cur.GetFields(Here))
                    if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        /// Reads the first field or property that exists. Pass a Type for statics.
        public static object Get(object obj, params string[] names)
        {
            if (obj == null) return null;
            var t = obj as Type ?? obj.GetType();
            var target = obj is Type ? null : obj;
            foreach (var n in names)
            {
                var m = FindMember(t, n);
                try
                {
                    var p = m as PropertyInfo;
                    if (p != null && p.GetGetMethod(true) != null) return p.GetValue(target, null);
                    var f = m as FieldInfo;
                    if (f != null) return f.GetValue(target);
                }
                catch { }
            }
            return null;
        }

        public static bool Set(object obj, object value, params string[] names)
        {
            if (obj == null) return false;
            foreach (var n in names)
            {
                var m = FindMember(obj.GetType(), n);
                try
                {
                    var p = m as PropertyInfo;
                    if (p != null && p.GetSetMethod(true) != null)
                    { p.SetValue(obj, Convert.ChangeType(value, p.PropertyType), null); return true; }
                    var f = m as FieldInfo;
                    if (f != null && !f.IsInitOnly)
                    { f.SetValue(obj, Convert.ChangeType(value, f.FieldType)); return true; }
                }
                catch { }
            }
            return false;
        }

        static object EnumValue(Type enumType, params string[] preferred)
        {
            foreach (var p in preferred)
                foreach (var name in Enum.GetNames(enumType))
                    if (string.Equals(name, p, StringComparison.OrdinalIgnoreCase))
                        return Enum.Parse(enumType, name);
            var values = Enum.GetValues(enumType);
            return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(enumType);
        }

        /// Fills a constructor/method parameter list from parameter names and types.
        /// Cuphead isn't obfuscated, so names like "damage", "damagesEnemy" are reliable.
        static object[] FillArgs(ParameterInfo[] ps, float damage, Vector2 origin)
        {
            var args = new object[ps.Length];
            bool damageUsed = false;
            int boolIndex = 0;
            for (int i = 0; i < ps.Length; i++)
            {
                var t = ps[i].ParameterType;
                var n = (ps[i].Name ?? "").ToLowerInvariant();
                if (t == typeof(float) || t == typeof(double) || t == typeof(int))
                {
                    double v = 0;
                    if (n.Contains("rate") || n.Contains("delay") || n.Contains("time")) v = 0;
                    else if (n.Contains("damage") || !damageUsed) { v = damage; damageUsed = true; }
                    args[i] = Convert.ChangeType(v, t);
                }
                else if (t == typeof(bool))
                {
                    if (n.Contains("player")) args[i] = false;
                    else if (n.Contains("enem") || n.Contains("other")) args[i] = true;
                    else args[i] = boolIndex != 0;
                    boolIndex++;
                }
                else if (t.IsEnum)
                {
                    args[i] = t.Name.Contains("Source") ? EnumValue(t, "Neutral", "Player")
                                                       : EnumValue(t, "Neutral", "None");
                }
                else if (t == typeof(Vector2)) args[i] = origin;
                else if (t == typeof(Vector3)) args[i] = (Vector3)origin;
                else args[i] = t.IsValueType ? Activator.CreateInstance(t) : null;
            }
            return args;
        }

        static bool Fillable(ParameterInfo[] ps)
        {
            foreach (var p in ps)
            {
                var t = p.ParameterType;
                if (t.IsByRef || t.IsPointer) return false;
            }
            return true;
        }

        // ── player ─────────────────────────────────────────────────────────

        public static List<Component> FindPlayers()
        {
            var list = new List<Component>();
            if (PlayerController == null) return list;
            foreach (var o in UnityEngine.Object.FindObjectsOfType(PlayerController))
            {
                var c = o as Component;
                if (c != null && c.gameObject.activeInHierarchy) list.Add(c);
            }
            return list;
        }

        public static bool IsPlayerOne(Component controller)
        {
            var id = Get(controller, "id", "playerId", "PlayerId");
            if (id == null) return true;
            var s = id.ToString();
            return s.IndexOf("One", StringComparison.OrdinalIgnoreCase) >= 0 || s == "0";
        }

        public static Component FindOnPlayer(Component controller, Type t)
        {
            if (controller == null || t == null) return null;
            var c = controller.GetComponent(t);
            if (c == null) c = controller.GetComponentInChildren(t);
            if (c == null) c = controller.GetComponentInParent(t);
            if (c == null)
            {
                foreach (var f in controller.GetType().GetFields(Any))
                    if (t.IsAssignableFrom(f.FieldType)) { c = f.GetValue(controller) as Component; if (c != null) break; }
            }
            return c;
        }

        public static int? GetHealth(Component stats)
        {
            var v = Get(stats, "Health", "health", "CurrentHealth");
            if (v == null) return null;
            try { return Convert.ToInt32(v); } catch { return null; }
        }

        public static int? GetMaxHealth(Component stats)
        {
            var v = Get(stats, "HealthMax", "MaxHealth", "maxHealth", "healthMax");
            if (v == null) return null;
            try { return Convert.ToInt32(v); } catch { return null; }
        }

        public static bool SetHealth(Component stats, int hp)
        {
            if (stats == null) return false;
            var m = stats.GetType().GetMethod("SetHealth", Any, null, new[] { typeof(int) }, null);
            if (m != null)
            {
                try { m.Invoke(stats, new object[] { hp }); return true; } catch { }
            }
            return Set(stats, hp, "Health", "<Health>k__BackingField", "health");
        }

        /// Zeroes the motor's downward speed so a Bash launch isn't eaten by gravity.
        public static void CancelFall(Component motor)
        {
            if (motor == null) return;
            var vm = Get(motor, "velocityManager", "velocity", "Velocity");
            if (vm == null) return;
            var y = Get(vm, "y");
            if (y is float && (float)y < 0f) Set(vm, 0f, "y");
        }

        public static bool IsPaused()
        {
            if (Time.timeScale < 0.01f) return true;
            if (PauseManager == null) return false;
            var s = Get(PauseManager, "state", "State");
            return s != null && string.Equals(s.ToString(), "Paused", StringComparison.OrdinalIgnoreCase);
        }

        // ── damage ─────────────────────────────────────────────────────────

        static ConstructorInfo _dealerCtor;
        static MethodInfo _dealMethod, _setFlags, _takeDamage;
        static ConstructorInfo _infoCtor;
        static bool _damageResolved;

        static void ResolveDamage()
        {
            _damageResolved = true;
            if (DamageDealer != null)
            {
                _dealMethod = DamageDealer.GetMethod("DealDamage", Any, null, new[] { typeof(GameObject) }, null);
                _setFlags = DamageDealer.GetMethod("SetDamageFlags", Any, null, new[] { typeof(bool), typeof(bool), typeof(bool) }, null);
                int best = -1;
                foreach (var c in DamageDealer.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var ps = c.GetParameters();
                    if (!Fillable(ps)) continue;
                    int score = ps.Length;
                    foreach (var p in ps)
                        if (p.ParameterType == typeof(bool) && (p.Name ?? "").ToLowerInvariant().Contains("enem")) score += 100;
                        else if (p.ParameterType == typeof(float)) score += 10;
                    if (score > best) { best = score; _dealerCtor = c; }
                }
            }
            if (DamageReceiver != null)
            {
                foreach (var m in DamageReceiver.GetMethods(Any))
                    if (m.Name == "TakeDamage" && m.GetParameters().Length == 1) { _takeDamage = m; break; }
                if (_takeDamage != null)
                {
                    var infoType = _takeDamage.GetParameters()[0].ParameterType;
                    int best = -1;
                    foreach (var c in infoType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        var ps = c.GetParameters();
                        if (!Fillable(ps)) continue;
                        if (ps.Length > best) { best = ps.Length; _infoCtor = c; }
                    }
                }
            }
            var mode = (_dealerCtor != null && _dealMethod != null) ? "DamageDealer.DealDamage"
                     : (_takeDamage != null && _infoCtor != null) ? "DamageReceiver.TakeDamage" : "NONE";
            _log.LogInfo("Damage path: " + mode);
        }

        /// The receiver that owns this collider, or null if it isn't something Ori may hit.
        public static Component ReceiverFor(Collider2D col, Transform playerRoot)
        {
            if (col == null || DamageReceiver == null) return null;
            if (playerRoot != null && col.transform.IsChildOf(playerRoot)) return null;
            var r = col.GetComponent(DamageReceiver);
            if (r == null && DamageReceiverChild != null)
            {
                var child = col.GetComponent(DamageReceiverChild);
                if (child != null) r = Get(child, "receiver", "Receiver") as Component;
            }
            if (r == null) r = col.GetComponentInParent(DamageReceiver);
            if (r == null) return null;
            if (playerRoot != null && r.transform.IsChildOf(playerRoot)) return null;
            var kind = Get(r, "type", "Type");
            if (kind != null && kind.ToString() == "Player") return null;
            if (!r.gameObject.activeInHierarchy) return null;
            return r;
        }

        public static bool Damage(Collider2D hit, Component receiver, float damage, Vector2 origin)
        {
            if (!_damageResolved) ResolveDamage();
            if (_dealerCtor != null && _dealMethod != null)
            {
                try
                {
                    var dealer = _dealerCtor.Invoke(FillArgs(_dealerCtor.GetParameters(), damage, origin));
                    if (_setFlags != null) _setFlags.Invoke(dealer, new object[] { false, true, true });
                    _dealMethod.Invoke(dealer, new object[] { hit.gameObject });
                    return true;
                }
                catch (Exception e)
                {
                    WarnOnce("dealer", "DamageDealer path failed (" + Inner(e) + "), trying TakeDamage.");
                    _dealerCtor = null;
                }
            }
            if (_takeDamage != null && _infoCtor != null && receiver != null)
            {
                try
                {
                    var info = _infoCtor.Invoke(FillArgs(_infoCtor.GetParameters(), damage, origin));
                    _takeDamage.Invoke(receiver, new[] { info });
                    return true;
                }
                catch (Exception e)
                {
                    WarnOnce("take", "TakeDamage path failed: " + Inner(e));
                }
            }
            return false;
        }

        static string Inner(Exception e)
        {
            while (e.InnerException != null) e = e.InnerException;
            return e.GetType().Name + ": " + e.Message;
        }

        // ── projectiles (Bash targets) ─────────────────────────────────────

        public static Component ProjectileFor(Collider2D col, Transform playerRoot)
        {
            if (Projectile == null || col == null) return null;
            if (playerRoot != null && col.transform.IsChildOf(playerRoot)) return null;
            var p = col.GetComponentInParent(Projectile);
            if (p == null || !p.gameObject.activeInHierarchy) return null;
            // Skip the other player's bullets: player projectiles don't damage players.
            var hurtsPlayer = Get(p, "DamagesPlayer", "damagesPlayer");
            if (hurtsPlayer is bool && !(bool)hurtsPlayer) return null;
            return p;
        }
    }
}
