using System.Collections.Generic;
using UnityEngine;

namespace Revolver
{
    /// <summary>
    /// Gunshot, click and reload sounds, muzzle flash, red tracer and hitmarker,
    /// all generated in code so the asset bundle only needs the model.
    /// </summary>
    public static class RevolverFx
    {
        private const int Rate = 44100;
        private static readonly Color Red = new Color(1f, 0.05f, 0.05f, 1f);
        private static readonly Color SparkColor = new Color(1f, 0.6f, 0.2f, 1f);

        private static AudioClip shot, empty, reloadStart, reloadDone, hit;
        private static Shader lineShader;
        private static bool shaderSearched;
        private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();

        public static void PlayShot(Vector3 at, float volume) => Play(shot ?? (shot = MakeShot()), at, volume);
        public static void PlayEmpty(Vector3 at, float volume) => Play(empty ?? (empty = MakeClick(3200f, 0.04f)), at, volume * 0.6f);
        public static void PlayReloadStart(Vector3 at, float volume) => Play(reloadStart ?? (reloadStart = MakeReloadStart()), at, volume * 0.7f);
        public static void PlayReloadDone(Vector3 at, float volume) => Play(reloadDone ?? (reloadDone = MakeClick(1800f, 0.07f)), at, volume * 0.8f);
        public static void PlayHit(Vector3 at, float volume) => Play(hit ?? (hit = MakeHit()), at, volume * 0.7f);

        private static void Play(AudioClip clip, Vector3 at, float volume)
        {
            if (clip != null && volume > 0f) AudioSource.PlayClipAtPoint(clip, at, volume);
        }

        public static void Tracer(Vector3 from, Vector3 to)
        {
            Line(new[] { from, to }, Red, 0.012f, 0.12f);
        }

        public static void HitMarker(Vector3 point, Vector3 normal)
        {
            Basis(normal, out Vector3 a, out Vector3 b);
            Vector3[] dirs = { (a + b).normalized, (a - b).normalized, (-a + b).normalized, (-a - b).normalized };
            foreach (Vector3 d in dirs) Line(new[] { point + d * 0.05f, point + d * 0.2f }, Red, 0.02f, 0.3f);
        }

        public static void Spark(Vector3 point, Vector3 normal)
        {
            Basis(normal, out Vector3 a, out Vector3 b);
            for (int i = 0; i < 5; i++)
            {
                Vector3 d = (normal + a * Random.Range(-0.8f, 0.8f) + b * Random.Range(-0.8f, 0.8f)).normalized;
                Line(new[] { point, point + d * Random.Range(0.08f, 0.2f) }, SparkColor, 0.01f, 0.15f);
            }
        }

        public static void MuzzleFlash(Vector3 at, Vector3 direction)
        {
            GameObject go = new GameObject("RevolverFlash");
            go.transform.position = at + direction * 0.05f;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.7f, 0.35f);
            light.range = 4f;
            light.intensity = 4f;
            go.AddComponent<Fader>().Init(null, light, 0f, 0.06f);
            Line(new[] { at, at + direction * 0.18f }, new Color(1f, 0.8f, 0.4f), 0.05f, 0.05f);
        }

        private static void Basis(Vector3 normal, out Vector3 a, out Vector3 b)
        {
            a = Vector3.Cross(normal, Vector3.up);
            if (a.sqrMagnitude < 0.01f) a = Vector3.Cross(normal, Vector3.right);
            a = a.normalized;
            b = Vector3.Cross(normal, a).normalized;
        }

        private static void Line(Vector3[] points, Color color, float width, float life)
        {
            Material material = GetMaterial(color);
            if (material == null) return;
            GameObject go = new GameObject("RevolverLine");
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.sharedMaterial = material;
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            go.AddComponent<Fader>().Init(line, null, width, life);
        }

        private static Material GetMaterial(Color color)
        {
            if (materials.TryGetValue(color, out Material cached)) return cached;
            if (!shaderSearched)
            {
                shaderSearched = true;
                foreach (string name in new[] { "Universal Render Pipeline/Unlit", "Sprites/Default", "Unlit/Color", "Hidden/Internal-Colored" })
                {
                    lineShader = Shader.Find(name);
                    if (lineShader != null) break;
                }
                if (lineShader == null) Debug.LogWarning("[Revolver] No unlit shader found, tracer disabled.");
            }
            if (lineShader == null) return null;

            Material material = new Material(lineShader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            materials[color] = material;
            return material;
        }

        // ---- procedural sounds ----

        private static AudioClip Clip(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip MakeShot()
        {
            const float length = 0.6f;
            int n = (int)(Rate * length);
            float[] d = new float[n];
            System.Random r = new System.Random(11);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float noise = (float)r.NextDouble() * 2f - 1f;
                lp += (noise - lp) * 0.25f;                                   // darker noise body
                float crack = noise * Mathf.Exp(-t * 60f);
                float body = lp * Mathf.Exp(-t * 9f) * 1.4f;
                phase += 2f * Mathf.PI * Mathf.Lerp(110f, 40f, t / length) / Rate;
                float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 7f);
                d[i] = Mathf.Clamp((crack + body + boom) * 0.7f, -1f, 1f);
            }
            return Clip("RevolverShot", d);
        }

        private static AudioClip MakeClick(float freq, float length)
        {
            int n = (int)(Rate * length);
            float[] d = new float[n];
            System.Random r = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float noise = ((float)r.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 300f);
                d[i] = (Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 120f) + noise) * 0.6f;
            }
            return Clip("RevolverClick", d);
        }

        // Cylinder swings out: a click followed by a short ratcheting spin.
        private static AudioClip MakeReloadStart()
        {
            const float length = 0.45f;
            int n = (int)(Rate * length);
            float[] d = new float[n];
            System.Random r = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float click = Mathf.Sin(2f * Mathf.PI * 2400f * t) * Mathf.Exp(-t * 150f);
                float tickT = (t * 22f) % 1f;                                 // ratchet ticks
                float ticks = t > 0.08f ? ((float)r.NextDouble() * 2f - 1f) * Mathf.Exp(-tickT * 40f) * 0.4f * Mathf.Exp(-(t - 0.08f) * 4f) : 0f;
                d[i] = Mathf.Clamp((click + ticks) * 0.6f, -1f, 1f);
            }
            return Clip("RevolverReload", d);
        }

        private static AudioClip MakeHit()
        {
            const float length = 0.2f;
            int n = (int)(Rate * length);
            float[] d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                phase += 2f * Mathf.PI * Mathf.Lerp(180f, 70f, t / length) / Rate;
                d[i] = (Mathf.Sin(phase) * Mathf.Exp(-t * 25f) + Mathf.Sin(2f * Mathf.PI * 1400f * t) * Mathf.Exp(-t * 40f) * 0.3f) * 0.7f;
            }
            return Clip("RevolverHit", d);
        }

        private class Fader : MonoBehaviour
        {
            private LineRenderer line;
            private Light light;
            private float width, life, born, intensity;

            public void Init(LineRenderer lineRenderer, Light flash, float startWidth, float lifetime)
            {
                line = lineRenderer;
                light = flash;
                width = startWidth;
                life = lifetime;
                born = Time.time;
                if (light != null) intensity = light.intensity;
            }

            private void Update()
            {
                float k = 1f - (Time.time - born) / life;
                if (k <= 0f) { Destroy(gameObject); return; }
                if (line != null) { line.startWidth = width * k; line.endWidth = width * k; }
                if (light != null) light.intensity = intensity * k;
            }
        }
    }
}
