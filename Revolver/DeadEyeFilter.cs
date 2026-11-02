using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Revolver
{
    /// <summary>Yellow "dead eye" screen tint, done with a global URP post-processing volume.</summary>
    public static class DeadEyeFilter
    {
        private static readonly HashSet<object> users = new HashSet<object>();
        private static Volume volume;
        private static FilterFader fader;

        public static void Show(object user)
        {
            users.Add(user);
            Ensure();
            if (fader != null) fader.target = 1f;
        }

        public static void Hide(object user)
        {
            users.Remove(user);
            if (users.Count == 0 && fader != null) fader.target = 0f;
        }

        private static void Ensure()
        {
            if (volume != null) return;

            GameObject go = new GameObject("RevolverDeadEyeFilter");
            Object.DontDestroyOnLoad(go);

            // Use the same layer as the game's own volumes so the camera picks it up.
            Volume existing = Object.FindObjectOfType<Volume>();
            if (existing != null) go.layer = existing.gameObject.layer;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.colorFilter.Override(new Color(1f, 0.83f, 0.38f));
            color.saturation.Override(-30f);
            color.contrast.Override(15f);
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.4f);
            vignette.color.Override(new Color(0.25f, 0.15f, 0f));

            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            volume.weight = 0f;
            volume.sharedProfile = profile;

            fader = go.AddComponent<FilterFader>();
            fader.volume = volume;
        }

        private class FilterFader : MonoBehaviour
        {
            public Volume volume;
            public float target;

            private void Update()
            {
                // Real time, so the fade speed doesn't depend on the slow motion.
                volume.weight = Mathf.MoveTowards(volume.weight, target, Time.unscaledDeltaTime * 5f);
            }
        }
    }
}
