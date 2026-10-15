using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// <summary>
    /// Procedurally drawn sprites — no Ori artwork is shipped or extracted.
    /// Every sprite is 1 world unit wide at scale 1, so callers size them in
    /// "player heights" and the art works whatever unit scale Cuphead uses.
    /// </summary>
    internal static class Art
    {
        public static Sprite Glow, Disc, Ear, Leg, Slash, Ring, Star, Spear, Pixel, Custom;

        public static void Build(string pluginDir)
        {
            if (Glow != null) return;
            Glow  = Make(Draw(64, 64, (x, y) => { float r = Len(x, y); return Mathf.Pow(Mathf.Clamp01(1f - r), 2f); }), 0.5f, 0.5f);
            Disc  = Make(Draw(64, 64, (x, y) => Edge(1f - Len(x, y), 64)), 0.5f, 0.5f);
            // Ear: rounded base, long pointed tip. Pivot at the base.
            Ear   = Make(Draw(32, 96, (x, y) =>
            {
                float v = (y + 1f) * 0.5f;                      // 0 base .. 1 tip
                float w = Mathf.Pow(1f - v, 0.75f) * Mathf.Sqrt(Mathf.Clamp01(v * 6f));
                return Edge(w - Mathf.Abs(x), 32);
            }), 0.5f, 0.02f);
            // Leg/arm: capsule pivoted at the top.
            Leg   = Make(Draw(24, 64, (x, y) =>
            {
                float v = (y + 1f) * 0.5f;
                float w = 0.8f * Mathf.Sqrt(Mathf.Clamp01(1f - Mathf.Pow(2f * v - 1f, 8f)));
                return Edge(w - Mathf.Abs(x), 24);
            }), 0.5f, 0.98f);
            // Crescent slash facing +x, thick in the middle, tapering at the ends.
            Slash = Make(Draw(128, 128, (x, y) =>
            {
                float r = Len(x, y);
                float a = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
                if (Mathf.Abs(a) > 80f) return 0f;
                float taper = Mathf.Cos(a / 80f * Mathf.PI * 0.5f);
                float half = 0.13f * taper;
                float band = half - Mathf.Abs(r - 0.78f);
                float core = Mathf.Clamp01(band / Mathf.Max(half, 1e-4f));
                return Edge(band, 128) * (0.45f + 0.55f * core) * Mathf.Clamp01(x * 3f + 0.6f);
            }), 0.5f, 0.5f);
            Ring  = Make(Draw(96, 96, (x, y) => Edge(0.07f - Mathf.Abs(Len(x, y) - 0.88f), 96)), 0.5f, 0.5f);
            // Four-pointed Spirit Star with a soft centre.
            Star  = Make(Draw(64, 64, (x, y) =>
            {
                float ax = Mathf.Abs(x), ay = Mathf.Abs(y);
                float d = Mathf.Sqrt(ax) + Mathf.Sqrt(ay);
                return Mathf.Max(Edge(0.95f - d, 64), Mathf.Pow(Mathf.Clamp01(1f - Len(x, y) * 2.2f), 2f));
            }), 0.5f, 0.5f);
            // Spear / arrow: long diamond with a pointed head at +x.
            Spear = Make(Draw(128, 24, (x, y) =>
            {
                float v = (x + 1f) * 0.5f;
                float w = v < 0.75f ? 0.35f + 0.3f * v : (1f - v) / 0.25f * 0.9f;
                return Edge(w - Mathf.Abs(y), 24) * Mathf.Clamp01(v * 4f);
            }), 0.5f, 0.5f);
            var px = NewTex(4, 4);
            var white = new Color32[16];
            for (int i = 0; i < 16; i++) white[i] = new Color32(255, 255, 255, 255);
            px.SetPixels32(white);
            px.Apply();
            Pixel = Make(px, 0.5f, 0.5f);

            Custom = LoadPng(Path.Combine(Path.Combine(pluginDir, "skin"), "ori.png"));
        }

        /// Every GameObject the mod creates is named "Ori.*".
        public static bool IsOurs(Component c) { return c.gameObject.name.StartsWith("Ori.", StringComparison.Ordinal); }

        static float Len(float x, float y) { return Mathf.Sqrt(x * x + y * y); }

        /// Anti-aliased edge: signed distance (in -1..1 texture space) to alpha.
        static float Edge(float d, int size) { return Mathf.Clamp01(d * size * 0.5f + 0.5f); }

        static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.hideFlags = HideFlags.HideAndDontSave;   // survive Resources.UnloadUnusedAssets
            return t;
        }

        static Texture2D Draw(int w, int h, Func<float, float, float> alpha)
        {
            var t = NewTex(w, h);
            var px = new Color32[w * h];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    float x = (i + 0.5f) / w * 2f - 1f, y = (j + 0.5f) / h * 2f - 1f;
                    byte a = (byte)(Mathf.Clamp01(alpha(x, y)) * 255f);
                    px[j * w + i] = new Color32(255, 255, 255, a);
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static Sprite Make(Texture2D t, float px, float py)
        {
            var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(px, py), t.width);
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        /// Optional user-supplied skin. Texture2D.LoadImage moved to ImageConversion
        /// in Unity 2017, so it's found by reflection to work on both builds of Cuphead.
        static Sprite LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = NewTex(2, 2);
                MethodInfo load = typeof(Texture2D).GetMethod("LoadImage", new[] { typeof(byte[]) });
                object target = tex;
                object[] args = { bytes };
                if (load == null)
                {
                    var conv = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule") ??
                               Type.GetType("UnityEngine.ImageConversion, UnityEngine");
                    if (conv != null) load = conv.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                    target = null;
                    args = new object[] { tex, bytes };
                }
                if (load == null || !(bool)load.Invoke(target, args)) return null;
                return Make(tex, 0.5f, 0f);
            }
            catch { return null; }
        }
    }
}
