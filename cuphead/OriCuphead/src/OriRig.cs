using System.Collections.Generic;
using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// <summary>
    /// Ori, built from procedural parts and animated in code: big pointed ears,
    /// a long swaying tail, tiny limbs and a soft blue glow. Laid out in units
    /// where Ori stands 1 tall with feet at 0, facing +x.
    /// If BepInEx/plugins/OriCuphead/skin/ori.png exists it is used instead.
    /// </summary>
    internal sealed class OriRig
    {
        public static readonly Color Light = new Color(0.93f, 0.97f, 1f);
        public static readonly Color Shade = new Color(0.72f, 0.84f, 1f);
        public static readonly Color GlowBlue = new Color(0.45f, 0.75f, 1f);
        static readonly Color Eye = new Color(0.07f, 0.11f, 0.24f);

        readonly GameObject _root;
        readonly Transform _pivot;
        readonly List<SpriteRenderer> _all = new List<SpriteRenderer>();
        readonly SpriteRenderer _glow, _custom;
        readonly Transform _legB, _legF, _armB, _armF, _earB, _earF, _head;
        readonly Transform[] _tail = new Transform[9];
        float _phase, _attack, _attackDir;

        public OriRig()
        {
            _root = new GameObject("Ori.Rig");
            _pivot = new GameObject("Ori.Pivot").transform;
            _pivot.SetParent(_root.transform, false);

            _glow = Part("Ori.Glow", Art.Glow, new Vector2(0f, 0.45f), new Vector2(1.6f, 1.6f), GlowBlue, 0);
            for (int i = 0; i < _tail.Length; i++)
            {
                float s = Mathf.Lerp(0.14f, 0.035f, i / (float)(_tail.Length - 1));
                _tail[i] = Part("Ori.Tail", Art.Disc, Vector2.zero, new Vector2(s, s * 0.9f), Color.Lerp(Light, Shade, i * 0.08f), 1).transform;
            }
            _legB = Part("Ori.LegB", Art.Leg, new Vector2(-0.035f, 0.2f), new Vector2(0.075f, 0.21f), Shade, 2).transform;
            _armB = Part("Ori.ArmB", Art.Leg, new Vector2(-0.02f, 0.38f), new Vector2(0.06f, 0.16f), Shade, 2).transform;
            Part("Ori.Body", Art.Disc, new Vector2(0f, 0.3f), new Vector2(0.21f, 0.27f), Light, 3);
            _legF = Part("Ori.LegF", Art.Leg, new Vector2(0.05f, 0.2f), new Vector2(0.075f, 0.21f), Light, 4).transform;
            _earB = Part("Ori.EarB", Art.Ear, new Vector2(-0.05f, 0.64f), new Vector2(0.11f, 0.36f), Shade, 4).transform;
            _head = Part("Ori.Head", Art.Disc, new Vector2(0.04f, 0.56f), new Vector2(0.37f, 0.31f), Light, 5).transform;
            _earF = Part("Ori.EarF", Art.Ear, new Vector2(0.06f, 0.66f), new Vector2(0.115f, 0.38f), Light, 6).transform;
            _armF = Part("Ori.ArmF", Art.Leg, new Vector2(0.07f, 0.38f), new Vector2(0.06f, 0.16f), Light, 6).transform;
            Part("Ori.Eye", Art.Disc, new Vector2(0.115f, 0.565f), new Vector2(0.05f, 0.08f), Eye, 7);
            Part("Ori.Eye", Art.Disc, new Vector2(0.185f, 0.565f), new Vector2(0.045f, 0.075f), Eye, 7);

            if (Art.Custom != null)
            {
                foreach (var sr in _all) sr.enabled = false;
                _all.Clear();
                var b = Art.Custom.bounds.size;
                _custom = Part("Ori.Custom", Art.Custom, Vector2.zero, new Vector2(b.x / b.y, 1f), Color.white, 3);
                _glow.enabled = true;
                _all.Add(_glow);
            }
        }

        SpriteRenderer Part(string name, Sprite s, Vector2 pos, Vector2 size, Color c, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_pivot, false);
            go.transform.localPosition = pos;
            var native = s.bounds.size;
            go.transform.localScale = new Vector3(size.x / native.x, size.y / native.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = s;
            sr.color = c;
            sr.sortingOrder = order;
            _all.Add(sr);
            return sr;
        }

        public bool Destroyed { get { return _root == null; } }
        public void Destroy() { if (_root != null) Object.Destroy(_root); }

        public void SetVisible(bool v) { if (_root != null && _root.activeSelf != v) _root.SetActive(v); }

        /// Swing the front arm; dir: 0 forward, 1 up, -1 down.
        public void Attack(float dir) { _attack = 1f; _attackDir = dir; }

        public void Sync(PlayerLink p, float scale, bool ducking)
        {
            int baseOrder = p.SortingOrder + 1;
            for (int i = 0; i < _all.Count; i++)
            {
                var sr = _all[i];
                sr.sortingLayerID = p.SortingLayer;
                sr.sortingOrder = baseOrder + OrderOf(sr);
            }

            float h = p.H * scale;
            _root.transform.position = new Vector3(p.Feet.x, p.Feet.y, p.Root.position.z);
            _root.transform.localScale = new Vector3(h * p.Facing, h, 1f);

            float dt = Time.deltaTime, t = Time.time;
            float run = p.Grounded ? Mathf.Clamp01(Mathf.Abs(p.Velocity.x) / (p.H * 1.5f)) : 0f;
            bool air = !p.Grounded;
            bool rising = air && p.Velocity.y > 0f;
            _phase += dt * 17f * Mathf.Max(run, 0.001f);
            _attack = Mathf.Max(0f, _attack - dt * 5f);

            // Body: bob, lean into the run, squash for duck, stretch while rising.
            float bob = air ? 0f : (run > 0.05f ? Mathf.Abs(Mathf.Sin(_phase)) * 0.035f : Mathf.Sin(t * 2.6f) * 0.012f);
            _pivot.localPosition = new Vector3(0f, bob, 0f);
            _pivot.localRotation = Quaternion.Euler(0f, 0f, -9f * run + (air ? (rising ? 4f : -6f) : 0f));
            Vector3 squash = ducking && !air ? new Vector3(1.18f, 0.62f, 1f)
                           : rising ? new Vector3(0.94f, 1.07f, 1f) : Vector3.one;
            _pivot.localScale = Vector3.Lerp(_pivot.localScale, squash, Mathf.Clamp01(dt * 18f));

            if (_custom != null) return;

            // Legs: scissor while running, tuck in the air.
            float stride = Mathf.Sin(_phase) * 42f * run;
            _legF.localRotation = Quaternion.Euler(0f, 0f, air ? 35f : stride);
            _legB.localRotation = Quaternion.Euler(0f, 0f, air ? -20f : -stride);

            // Arms: counter-swing, front arm slashes on attack.
            // 0° hangs down, +90° points forward, 180° points up.
            float from = _attackDir > 0 ? 40f : _attackDir < 0 ? 130f : 170f;
            float to   = _attackDir > 0 ? 200f : _attackDir < 0 ? -30f : 30f;
            float swing = _attack > 0f ? Mathf.Lerp(from, to, 1f - _attack * _attack) : -stride * 0.7f + 8f;
            _armF.localRotation = Quaternion.Euler(0f, 0f, swing);
            _armB.localRotation = Quaternion.Euler(0f, 0f, stride * 0.7f - 8f);

            // Ears stream back when moving fast, sway gently otherwise.
            float streamBack = 14f * run + (air ? 18f : 0f);
            _earB.localRotation = Quaternion.Euler(0f, 0f, 22f + streamBack + Mathf.Sin(t * 2.1f) * 4f);
            _earF.localRotation = Quaternion.Euler(0f, 0f, 9f + streamBack + Mathf.Sin(t * 2.1f + 0.7f) * 4f);
            _head.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 1.3f) * 2f);

            // Tail: a chain of discs curling up and back, flowing behind when running.
            Vector2 pos = new Vector2(-0.085f, 0.27f);
            float baseAngle = Mathf.Lerp(150f, 172f, Mathf.Max(run, air ? 0.7f : 0f));
            for (int i = 0; i < _tail.Length; i++)
            {
                float a = (baseAngle - i * (9f - 6f * run) + Mathf.Sin(t * 3.2f - i * 0.55f) * (4f + i * 2.2f)) * Mathf.Deg2Rad;
                pos += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.052f;
                _tail[i].localPosition = pos;
            }

            var g = GlowBlue;
            g.a = 0.33f + Mathf.Sin(t * 3f) * 0.05f + _attack * 0.15f;
            _glow.color = g;
        }

        static int OrderOf(SpriteRenderer sr)
        {
            switch (sr.gameObject.name)
            {
                case "Ori.Glow": return 0;
                case "Ori.Tail": return 1;
                case "Ori.LegB": case "Ori.ArmB": return 2;
                case "Ori.Body": case "Ori.Custom": return 3;
                case "Ori.LegF": case "Ori.EarB": return 4;
                case "Ori.Head": return 5;
                case "Ori.EarF": case "Ori.ArmF": return 6;
                default: return 7;
            }
        }
    }
}
