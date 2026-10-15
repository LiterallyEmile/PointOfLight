using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// <summary>
    /// Ori-style sound effects, synthesised at startup. Drop a 16-bit PCM
    /// &lt;name&gt;.wav into the plugin's "sounds" folder to replace any of them.
    /// </summary>
    internal sealed class Sfx
    {
        const int Rate = 44100;
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly AudioSource[] _voices = new AudioSource[6];
        int _next;
        readonly System.Random _rng = new System.Random(7);
        public float Volume = 0.7f;

        public static readonly string[] Names =
        {
            "edge", "smash", "arc", "star", "spear", "burst", "bash_start", "bash_launch",
            "heal", "hit", "empty", "cycle",
        };

        public Sfx(GameObject host, string pluginDir)
        {
            for (int i = 0; i < _voices.Length; i++)
            {
                var src = host.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.ignoreListenerPause = false;
                _voices[i] = src;
            }
            var dir = Path.Combine(pluginDir, "sounds");
            foreach (var n in Names)
            {
                var custom = LoadWav(Path.Combine(dir, n + ".wav"));
                _clips[n] = custom ?? Synth(n);
            }
        }

        public void Play(string name, float vol = 1f, float pitchJitter = 0.06f)
        {
            AudioClip clip;
            if (!_clips.TryGetValue(name, out clip) || clip == null) return;
            var v = _voices[_next];
            _next = (_next + 1) % _voices.Length;
            v.pitch = 1f + ((float)_rng.NextDouble() * 2f - 1f) * pitchJitter;
            v.PlayOneShot(clip, Volume * vol);
        }

        // ── synthesis ─────────────────────────────────────────────────────

        AudioClip Synth(string name)
        {
            switch (name)
            {
                case "edge":        return Whoosh(name, 0.16f, 9000f, 1200f, 0.55f);
                case "smash":       return Smash(name);
                case "arc":         return Pluck(name, 620f, 0.2f);
                case "star":        return Bell(name, new[] { 1240f, 1860f, 2790f }, 0.22f, 18f);
                case "spear":       return Layer(name, Whoosh(null, 0.35f, 2000f, 7000f, 0.5f), Bell(null, new[] { 990f, 1485f }, 0.35f, 9f));
                case "burst":       return Boom(name);
                case "bash_start":  return Bell(name, new[] { 880f, 1320f, 1760f, 2640f }, 0.6f, 5f);
                case "bash_launch": return Whoosh(name, 0.25f, 1500f, 9000f, 0.6f);
                case "heal":        return Arpeggio(name, new[] { 660f, 880f, 1100f, 1320f, 1760f });
                case "hit":         return Bell(name, new[] { 2200f, 3300f }, 0.07f, 45f, 0.35f);
                case "empty":       return Buzz(name);
                case "cycle":       return Bell(name, new[] { 1500f, 2250f }, 0.09f, 30f, 0.3f);
            }
            return null;
        }

        static AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name ?? "tmp", data.Length, 1, Rate, false);
            c.SetData(data, 0);
            c.hideFlags = HideFlags.HideAndDontSave;
            return c;
        }

        float Noise() { return (float)_rng.NextDouble() * 2f - 1f; }

        AudioClip Whoosh(string name, float len, float fromHz, float toHz, float amp)
        {
            int n = (int)(len * Rate);
            var d = new float[n];
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float cutoff = Mathf.Lerp(fromHz, toHz, t);
                float k = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
                lp += (Noise() - lp) * k;
                lp2 += (lp - lp2) * k;
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.6f));
                d[i] = (lp - lp2 * 0.5f) * env * amp * 2.2f;
            }
            return name == null ? Clip("w", d) : Clip(name, d);
        }

        AudioClip Bell(string name, float[] partials, float len, float decay, float amp = 0.45f)
        {
            int n = (int)(len * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float s = 0f;
                for (int p = 0; p < partials.Length; p++)
                    s += Mathf.Sin(2f * Mathf.PI * partials[p] * t) * Mathf.Exp(-t * decay * (1f + p * 0.6f)) / (1f + p);
                float attack = Mathf.Clamp01(t * 400f);
                d[i] = s * amp * attack;
            }
            return Clip(name, d);
        }

        AudioClip Pluck(string name, float hz, float len)
        {
            int n = (int)(len * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float f = hz * (1f + 0.4f * Mathf.Exp(-t * 60f));
                float s = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 22f);
                s += Noise() * Mathf.Exp(-t * 300f) * 0.6f;
                d[i] = s * 0.5f;
            }
            return Clip(name, d);
        }

        AudioClip Smash(string name)
        {
            int n = (int)(0.4f * Rate);
            var d = new float[n];
            float lp = 0f, phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                phase += 2f * Mathf.PI * Mathf.Lerp(95f, 38f, Mathf.Clamp01(t * 4f)) / Rate;
                lp += (Noise() - lp) * 0.08f;
                float body = Mathf.Sin(phase) * Mathf.Exp(-t * 9f);
                float crack = lp * Mathf.Exp(-t * 25f) * 2.5f;
                d[i] = Mathf.Clamp((body + crack) * 0.8f, -1f, 1f);
            }
            return Clip(name, d);
        }

        AudioClip Boom(string name)
        {
            int n = (int)(0.7f * Rate);
            var d = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float k = Mathf.Lerp(0.35f, 0.02f, Mathf.Clamp01(t * 2f));
                lp += (Noise() - lp) * k;
                float s = lp * 2f * Mathf.Exp(-t * 5f) + Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 7f) * 0.7f;
                s += Mathf.Sin(2f * Mathf.PI * 1760f * t) * Mathf.Exp(-t * 12f) * 0.15f;
                d[i] = Mathf.Clamp(s * 0.7f, -1f, 1f);
            }
            return Clip(name, d);
        }

        AudioClip Arpeggio(string name, float[] notes)
        {
            const float step = 0.1f, tail = 0.45f;
            int n = (int)((step * notes.Length + tail) * Rate);
            var d = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = (int)(k * step * Rate);
                for (int i = start; i < n; i++)
                {
                    float t = (float)(i - start) / Rate;
                    d[i] += (Mathf.Sin(2f * Mathf.PI * notes[k] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[k] * t))
                            * Mathf.Exp(-t * 6f) * Mathf.Clamp01(t * 300f) * 0.22f;
                }
            }
            return Clip(name, d);
        }

        AudioClip Buzz(string name)
        {
            int n = (int)(0.14f * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float s = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 180f * t)) * 0.5f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.5f;
                d[i] = s * Mathf.Exp(-t * 18f) * 0.25f;
            }
            return Clip(name, d);
        }

        static AudioClip Layer(string name, AudioClip a, AudioClip b)
        {
            var da = new float[a.samples];
            var db = new float[b.samples];
            a.GetData(da, 0);
            b.GetData(db, 0);
            var d = new float[Mathf.Max(da.Length, db.Length)];
            for (int i = 0; i < d.Length; i++)
                d[i] = (i < da.Length ? da[i] : 0f) + (i < db.Length ? db[i] : 0f);
            return Clip(name, d);
        }

        // ── .wav overrides ─────────────────────────────────────────────────

        static AudioClip LoadWav(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var b = File.ReadAllBytes(path);
                if (b.Length < 44 || b[0] != 'R' || b[8] != 'W') return null;
                int channels = 1, rate = Rate, bits = 16, pos = 12;
                while (pos + 8 <= b.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                    int size = BitConverter.ToInt32(b, pos + 4);
                    int body = pos + 8;
                    if (id == "fmt ")
                    {
                        channels = BitConverter.ToInt16(b, body + 2);
                        rate = BitConverter.ToInt32(b, body + 4);
                        bits = BitConverter.ToInt16(b, body + 14);
                    }
                    else if (id == "data")
                    {
                        if (bits != 16) return null;
                        size = Math.Min(size, b.Length - body);
                        int samples = size / 2;
                        var d = new float[samples];
                        for (int i = 0; i < samples; i++) d[i] = BitConverter.ToInt16(b, body + i * 2) / 32768f;
                        var c = AudioClip.Create(Path.GetFileNameWithoutExtension(path), samples / channels, channels, rate, false);
                        c.SetData(d, 0);
                        c.hideFlags = HideFlags.HideAndDontSave;
                        return c;
                    }
                    pos = body + size + (size & 1);
                }
            }
            catch { }
            return null;
        }
    }
}
