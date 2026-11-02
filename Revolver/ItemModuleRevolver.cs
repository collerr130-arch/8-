using ThunderRoad;

namespace Revolver
{
    /// <summary>
    /// Item module referenced from Item_Revolver.json. Every number here can be
    /// changed in the JSON without rebuilding the DLL.
    /// </summary>
    public class ItemModuleRevolver : ItemModule
    {
        public float damage = 35f;
        public float headshotMultiplier = 3f;
        public float neckMultiplier = 2f;
        public float range = 150f;
        public int maxAmmo = 6;
        public float fireCooldown = 0.25f;
        public float reloadTime = 0.6f;

        // Muzzle kicks up (rad/s) and the gun is pushed back (m/s).
        public float recoilAngle = 18f;
        public float recoilForce = 3.5f;

        // Push given to the body part that was hit.
        public float impactForce = 45f;
        public bool knockDown = true;

        // Snap the shot to a weak point/head within this angle (degrees). 0 = off.
        public float aimAssistAngle = 4f;

        // Flick the wrist this fast (rad/s, around the barrel) to reload.
        public float flickReloadSpeed = 14f;

        // Dead eye: with slow motion active and the revolver in hand, the trigger
        // marks targets (red crosses); when slow motion ends they are shot in turn.
        public bool deadEye = true;
        public float deadEyeTimeScale = 0.9f;     // slower than this counts as slow motion
        public float deadEyeShotInterval = 0.15f; // seconds between the queued shots

        public bool redTracer = true;
        public float volume = 1f;

        // Names of child objects in the prefab.
        public string muzzleName = "Muzzle";   // falls back to "Aim" (ProtoGun)
        public string drumName = "Drum";       // optional: rotates 60 degrees per shot

        public override void OnItemLoaded(Item item)
        {
            base.OnItemLoaded(item);
            RevolverBehaviour behaviour = item.gameObject.GetComponent<RevolverBehaviour>();
            if (behaviour == null) behaviour = item.gameObject.AddComponent<RevolverBehaviour>();
            behaviour.Init(this, item);
        }
    }
}
