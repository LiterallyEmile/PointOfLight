using System;
using System.Collections.Generic;
using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// A short-lived sprite: slashes, sparks, rings, glows.
    internal sealed class Fx
    {
        public GameObject Go;
        public SpriteRenderer Sr;
        public float Life, Age, AlphaFrom = 1f, Drag;
        public Vector2 Vel, ScaleFrom, ScaleTo;
        public Color Color;

        public bool Tick(float dt)
        {
            if (Go == null) return false;
            Age += dt;
            float k = Mathf.Clamp01(Age / Life);
            Vel *= Mathf.Max(0f, 1f - Drag * dt);
            Go.transform.position += (Vector3)(Vel * dt);
            var s = Vector2.Lerp(ScaleFrom, ScaleTo, 1f - (1f - k) * (1f - k));
            Go.transform.localScale = new Vector3(s.x, s.y, 1f);
            var c = Color;
            c.a = Color.a * AlphaFrom * (1f - k);
            Sr.color = c;
            if (k >= 1f) { UnityEngine.Object.Destroy(Go); return false; }
            return true;
        }
    }

    /// One of Ori's projectiles.
    internal sealed class Shot
    {
        public string Kind;
        public GameObject Go;
        public SpriteRenderer Sr;
        public Vector2 Pos, Vel;
        public float Radius, Damage, Life, Age, Gravity, Spin;
        public bool Pierce, GainsEnergy, Returns, Explodes, FaceVelocity = true;
        public float TickRate;                 // >0: hits the same target again after this long
        public float ExplodeRadius, ExplodeDamage, FloorY = float.MinValue;
        public readonly Dictionary<int, float> Hit = new Dictionary<int, float>();
        public float TrailTimer;
    }

    /// <summary>
    /// Owns every effect and projectile, and does all hit queries against Cuphead's colliders.
    /// </summary>
    internal sealed class World
    {
        readonly List<Fx> _fx = new List<Fx>();
        public readonly List<Shot> Shots = new List<Shot>();
        readonly System.Random _rng = new System.Random();
        public int SortingLayer, SortingOrder;
        public Transform PlayerRoot;
        public Action<float, bool> OnDamage;    // damage dealt, gains energy

        static readonly Collider2D[] NoHits = new Collider2D[0];

        public float Rand(float a, float b) { return a + (float)_rng.NextDouble() * (b - a); }

        public Fx Spawn(Sprite s, Vector2 pos, float angle, Vector2 size, Color c, float life, int orderBoost = 10)
        {
            var go = new GameObject("Ori.Fx");
            go.transform.position = new Vector3(pos.x, pos.y, PlayerRoot != null ? PlayerRoot.position.z : 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.color = c;
            sr.sortingLayerID = SortingLayer;
            sr.sortingOrder = SortingOrder + orderBoost;
            var native = s.bounds.size;
            var scale = new Vector2(size.x / native.x, size.y / native.y);
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            var fx = new Fx { Go = go, Sr = sr, Life = life, Color = c, ScaleFrom = scale, ScaleTo = scale };
            _fx.Add(fx);
            return fx;
        }

        public void Sparks(Vector2 pos, float h, int count, Color c, float speed = 3f)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Rand(0f, Mathf.PI * 2f);
                var f = Spawn(Art.Glow, pos, 0f, Vector2.one * h * Rand(0.08f, 0.18f), c, Rand(0.25f, 0.5f), 12);
                f.Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h * speed * Rand(0.4f, 1f);
                f.Drag = 4f;
                f.ScaleTo = f.ScaleFrom * 0.3f;
            }
        }

        public Shot Fire(string kind, Sprite s, Vector2 pos, Vector2 vel, Vector2 size, Color c, float radius, float damage, float life)
        {
            var go = new GameObject("Ori.Shot." + kind);
            go.transform.position = new Vector3(pos.x, pos.y, PlayerRoot != null ? PlayerRoot.position.z : 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.color = c;
            sr.sortingLayerID = SortingLayer;
            sr.sortingOrder = SortingOrder + 11;
            var native = s.bounds.size;
            go.transform.localScale = new Vector3(size.x / native.x, size.y / native.y, 1f);
            var shot = new Shot { Kind = kind, Go = go, Sr = sr, Pos = pos, Vel = vel, Radius = radius, Damage = damage, Life = life };
            Shots.Add(shot);
            return shot;
        }

        public void TickFx(float dt)
        {
            for (int i = _fx.Count - 1; i >= 0; i--)
                if (!_fx[i].Tick(dt)) _fx.RemoveAt(i);
        }

        public void Clear()
        {
            foreach (var f in _fx) if (f.Go != null) UnityEngine.Object.Destroy(f.Go);
            foreach (var s in Shots) if (s.Go != null) UnityEngine.Object.Destroy(s.Go);
            _fx.Clear();
            Shots.Clear();
        }

        // ── hit queries ─────────────────────────────────────────────────────

        static Collider2D[] Query(Func<Collider2D[]> q)
        {
            bool old = Physics2D.queriesHitTriggers;   // Cuphead's hitboxes are triggers
            Physics2D.queriesHitTriggers = true;
            try { return q() ?? NoHits; }
            catch { return NoHits; }
            finally { Physics2D.queriesHitTriggers = old; }
        }

        public Collider2D[] Box(Vector2 center, Vector2 size, float angle)
        {
            return Query(() => Physics2D.OverlapBoxAll(center, size, angle, ~0));
        }

        public Collider2D[] Circle(Vector2 center, float radius)
        {
            return Query(() => Physics2D.OverlapCircleAll(center, radius, ~0));
        }

        /// Damages every enemy among the colliders once per `hits` set. Returns how many were hit.
        public int Apply(Collider2D[] cols, float damage, Vector2 origin, Dictionary<int, float> hits, float rehitAfter, bool gainsEnergy, float h)
        {
            int count = 0;
            float now = Time.time;
            foreach (var col in cols)
            {
                var r = GameApi.ReceiverFor(col, PlayerRoot);
                if (r == null) continue;
                int id = r.GetInstanceID();
                float next;
                if (hits.TryGetValue(id, out next) && (rehitAfter <= 0f || now < next)) continue;
                if (!GameApi.Damage(col, r, damage, origin)) continue;
                hits[id] = now + rehitAfter;
                count++;
                Vector2 at = col.bounds.ClosestPoint(origin);
                Sparks(at, h, 5, OriRig.Light);
                Spawn(Art.Glow, at, 0f, Vector2.one * h * 0.6f, new Color(1f, 1f, 1f, 0.9f), 0.12f, 13).ScaleTo = Vector2.one * h * 0.05f;
                if (OnDamage != null) OnDamage(damage, gainsEnergy);
            }
            return count;
        }

        /// Visible play area, so projectiles can be cleaned up once they leave it.
        public static bool ViewBounds(out Rect r)
        {
            r = new Rect();
            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return false;
            float hh = cam.orthographicSize, hw = hh * cam.aspect;
            var p = cam.transform.position;
            r = new Rect(p.x - hw, p.y - hh, hw * 2f, hh * 2f);
            return true;
        }
    }
}
