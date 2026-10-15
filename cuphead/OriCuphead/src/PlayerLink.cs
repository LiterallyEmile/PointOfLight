using System.Collections.Generic;
using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// <summary>
    /// One Cuphead player that Ori is riding: measures the player's size,
    /// hides Cuphead's sprites, holds the finger-gun off, and works out
    /// facing, grounded and hit state each frame.
    /// </summary>
    internal sealed class PlayerLink
    {
        const float CalibrateSeconds = 1.0f;   // also lets the weapon manager's Start() run first
        const float HitFlickerSeconds = 1.6f;

        public readonly Component Controller;
        public readonly Transform Root;
        readonly Behaviour _weapons;
        public readonly Component Stats, Motor;

        readonly List<float> _heights = new List<float>(), _feet = new List<float>();
        float _age, _rescan;
        public bool Calibrated { get; private set; }
        public float H { get; private set; } = 1f;     // player height in world units
        float _feetOffset;

        readonly Dictionary<SpriteRenderer, bool> _hidden = new Dictionary<SpriteRenderer, bool>();
        public int SortingLayer, SortingOrder;

        public int Facing { get; private set; } = 1;
        public bool Grounded { get; private set; } = true;
        public Vector2 Velocity { get; private set; }
        Vector3 _lastPos;
        float _stillY;

        int? _lastHp;
        public float HitFlicker { get; private set; }
        public bool Dead { get; private set; }

        public PlayerLink(Component controller)
        {
            Controller = controller;
            Root = controller.transform;
            _weapons = GameApi.FindOnPlayer(controller, GameApi.WeaponManager) as Behaviour;
            Stats = GameApi.FindOnPlayer(controller, GameApi.Stats);
            Motor = GameApi.FindOnPlayer(controller, GameApi.Motor);
            _lastPos = Root.position;
            _lastHp = GameApi.GetHealth(Stats);
        }

        public bool Alive { get { return Controller != null && Controller.gameObject.activeInHierarchy; } }

        public Vector2 Feet { get { return (Vector2)Root.position + new Vector2(0f, _feetOffset); } }
        public Vector2 Center { get { return Feet + new Vector2(0f, H * 0.45f); } }

        public string Describe()
        {
            var names = new List<string>();
            foreach (var c in Controller.GetComponents<Component>()) if (c != null) names.Add(c.GetType().Name);
            return string.Format("player '{0}' height={1:0.##} weapons={2} stats={3} motor={4} components=[{5}]",
                Controller.name, H, _weapons != null, Stats != null, Motor != null, string.Join(", ", names.ToArray()));
        }

        /// Called from Update. Returns true once calibration just finished.
        public bool Tick(float dt, Vector2 inputDir, bool combat)
        {
            bool justCalibrated = false;
            _age += Time.unscaledDeltaTime;
            if (!Calibrated)
            {
                Bounds b;
                if (VisibleBounds(out b))
                {
                    _heights.Add(b.size.y);
                    _feet.Add(b.min.y - Root.position.y);
                }
                if (_age >= CalibrateSeconds && _heights.Count > 5)
                {
                    H = Median(_heights);
                    _feetOffset = Median(_feet);
                    Calibrated = true;
                    justCalibrated = true;
                }
            }

            // Facing follows the stick like Cuphead does; fall back to the sprite's flip.
            if (inputDir.x != 0f) Facing = inputDir.x > 0 ? 1 : -1;
            else if (_age < 0.1f) Facing = Root.lossyScale.x < 0 ? -1 : 1;

            if (dt > 0f)
            {
                var p = Root.position;
                Velocity = (p - _lastPos) / dt;
                // Grounded ≈ vertical position hasn't changed for a few frames.
                if (Mathf.Abs(p.y - _lastPos.y) < H * 0.002f) _stillY += dt; else _stillY = 0f;
                Grounded = _stillY > 0.05f;
                _lastPos = p;
            }

            var hp = GameApi.GetHealth(Stats);
            if (hp.HasValue && _lastHp.HasValue && hp.Value < _lastHp.Value) HitFlicker = HitFlickerSeconds;
            if (hp.HasValue) { Dead = hp.Value <= 0; _lastHp = hp; }
            HitFlicker = Mathf.Max(0f, HitFlicker - dt);

            // Cuphead re-enables things on state changes; hold the gun off every frame.
            if (_weapons != null && Calibrated)
            {
                if (combat && _weapons.enabled) _weapons.enabled = false;
                else if (!combat && !_weapons.enabled) _weapons.enabled = true;
            }
            return justCalibrated;
        }

        bool VisibleBounds(out Bounds b)
        {
            b = new Bounds();
            bool any = false;
            foreach (var sr in Root.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!sr.enabled || sr.sprite == null || Art.IsOurs(sr)) continue;
                if (!any) { b = sr.bounds; any = true; SortingLayer = sr.sortingLayerID; SortingOrder = sr.sortingOrder; }
                else
                {
                    b.Encapsulate(sr.bounds);
                    if (sr.sortingOrder > SortingOrder) { SortingOrder = sr.sortingOrder; SortingLayer = sr.sortingLayerID; }
                }
            }
            return any && b.size.y > 0f;
        }

        static float Median(List<float> v)
        {
            var a = v.ToArray();
            System.Array.Sort(a);
            return a[a.Length / 2];
        }

        /// Called from LateUpdate, after Cuphead's animator has run.
        public void HideCuphead(bool hide)
        {
            _rescan -= Time.unscaledDeltaTime;
            if (hide)
            {
                if (_rescan <= 0f)
                {
                    _rescan = 0.5f;
                    foreach (var sr in Root.GetComponentsInChildren<SpriteRenderer>(true))
                        if (!_hidden.ContainsKey(sr) && !Art.IsOurs(sr)) _hidden[sr] = sr.enabled;
                }
                foreach (var sr in _hidden.Keys)
                    if (sr != null && sr.enabled) sr.enabled = false;
            }
            else if (_hidden.Count > 0)
            {
                foreach (var kv in _hidden)
                    if (kv.Key != null) kv.Key.enabled = kv.Value;
                _hidden.Clear();
            }
        }

        public void Release()
        {
            HideCuphead(false);
            if (_weapons != null) _weapons.enabled = true;
        }
    }
}
