using System.Collections.Generic;
using UnityEngine;

namespace HomingThrow
{
    /// <summary>
    /// Hit sound and red laser lines, generated in code so the mod needs no asset bundle.
    /// </summary>
    public static class HitEffects
    {
        private const int SampleRate = 44100;
        private static readonly Color Red = new Color(1f, 0.05f, 0.05f, 1f);

        private static AudioClip hitClip;
        private static AudioClip lockClip;
        private static Material lineMaterial;
        private static bool materialSearched;

        public static void PlayHit(Vector3 position)
        {
            if (!HomingThrowScript.HitSound) return;
            if (hitClip == null) hitClip = MakeHitClip();
            AudioSource.PlayClipAtPoint(hitClip, position, HomingThrowScript.SoundVolume);
        }

        public static void PlayLockOn(Vector3 position)
        {
            if (!HomingThrowScript.HitSound) return;
            if (lockClip == null) lockClip = MakeLockClip();
            AudioSource.PlayClipAtPoint(lockClip, position, HomingThrowScript.SoundVolume * 0.5f);
        }

        /// <summary>A red laser from the item to where it is about to strike.</summary>
        public static void Laser(Vector3 from, Vector3 to)
        {
            if (!HomingThrowScript.RedLaser) return;
            Line(new[] { from, to }, 0.02f, 0.25f);
        }

        /// <summary>A red hitmarker burst at the point of impact.</summary>
        public static void HitMarker(Vector3 point, Vector3 normal)
        {
            if (!HomingThrowScript.RedLaser) return;
            Vector3 a = Vector3.Cross(normal, Vector3.up);
            if (a.sqrMagnitude < 0.01f) a = Vector3.Cross(normal, Vector3.right);
            a = a.normalized;
            Vector3 b = Vector3.Cross(normal, a).normalized;
            const float inner = 0.06f, outer = 0.22f;
            Vector3[] dirs = { (a + b).normalized, (a - b).normalized, (-a + b).normalized, (-a - b).normalized };
            foreach (Vector3 d in dirs)
                Line(new[] { point + d * inner, point + d * outer }, 0.025f, 0.35f);
            Line(new[] { point, point + normal * 0.4f }, 0.04f, 0.2f);
        }

        private static void Line(Vector3[] points, float width, float life)
        {
            Material material = GetMaterial();
            if (material == null) return;

            GameObject go = new GameObject("HomingThrowLaser");
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.material = material;
            line.startColor = Red;
            line.endColor = Red;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            go.AddComponent<LineFader>().Init(line, width, life);
        }

        private static Material GetMaterial()
        {
            if (materialSearched) return lineMaterial;
            materialSearched = true;
            string[] shaders =
            {
                "Universal Render Pipeline/Unlit",
                "Sprites/Default",
                "Unlit/Color",
                "Hidden/Internal-Colored",
            };
            foreach (string name in shaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null) continue;
                lineMaterial = new Material(shader);
                if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", Red);
                if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", Red);
                Debug.Log("[HomingThrow] Laser shader: " + name);
                return lineMaterial;
            }
            Debug.LogWarning("[HomingThrow] No unlit shader found, red laser disabled.");
            return null;
        }

        // Punchy impact: low thump + metallic ping + a click of noise.
        private static AudioClip MakeHitClip()
        {
            const float length = 0.35f;
            int count = (int)(SampleRate * length);
            float[] data = new float[count];
            System.Random random = new System.Random(7);
            float phaseLow = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float lowFreq = Mathf.Lerp(150f, 55f, t / length);
                phaseLow += 2f * Mathf.PI * lowFreq / SampleRate;
                float thump = Mathf.Sin(phaseLow) * Mathf.Exp(-t * 18f);
                float ping = (Mathf.Sin(2f * Mathf.PI * 1850f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 2730f * t))
                             * Mathf.Exp(-t * 14f) * 0.35f;
                float noise = ((float)random.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 90f) * 0.6f;
                data[i] = Mathf.Clamp((thump + ping + noise) * 0.8f, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("HomingThrowHit", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Short rising blip when the item locks onto the next target.
        private static AudioClip MakeLockClip()
        {
            const float length = 0.12f;
            int count = (int)(SampleRate * length);
            float[] data = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                phase += 2f * Mathf.PI * Mathf.Lerp(900f, 1600f, t / length) / SampleRate;
                data[i] = Mathf.Sin(phase) * Mathf.Exp(-t * 25f) * 0.5f;
            }
            AudioClip clip = AudioClip.Create("HomingThrowLock", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private class LineFader : MonoBehaviour
        {
            private LineRenderer line;
            private float width;
            private float life;
            private float born;

            public void Init(LineRenderer renderer, float startWidth, float lifetime)
            {
                line = renderer;
                width = startWidth;
                life = lifetime;
                born = Time.time;
            }

            private void Update()
            {
                float k = 1f - (Time.time - born) / life;
                if (k <= 0f) { Destroy(gameObject); return; }
                line.startWidth = width * k;
                line.endWidth = width * k;
            }
        }
    }
}
