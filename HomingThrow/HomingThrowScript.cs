using ThunderRoad;
using UnityEngine;

namespace HomingThrow
{
    /// <summary>
    /// Watches the player's hands. Whenever an item leaves a hand as a throw,
    /// a <see cref="HomingProjectile"/> is attached to it.
    /// </summary>
    public class HomingThrowScript : ThunderScript
    {
        [ModOption("Enabled", "Thrown items home in on enemies and ricochet", category = "Homing Throw")]
        public static bool Enabled = true;

        [ModOptionSlider, ModOptionFloatValues(5f, 50f, 1f)]
        [ModOption("Flight Speed", "Speed of a homing item, m/s", category = "Homing Throw")]
        public static float Speed = 20f;

        [ModOptionSlider, ModOptionFloatValues(1f, 30f, 1f)]
        [ModOption("Steering", "How hard the item turns towards its target", category = "Homing Throw")]
        public static float Steering = 15f;

        [ModOptionSlider, ModOptionFloatValues(0f, 20f, 1f)]
        [ModOption("Max Bounces", "How many hits/walls before the item stops ricocheting", category = "Homing Throw")]
        public static float MaxBounces = 6f;

        [ModOptionSlider, ModOptionFloatValues(5f, 100f, 5f)]
        [ModOption("Search Range", "How far away an enemy can be targeted, m", category = "Homing Throw")]
        public static float Range = 40f;

        [ModOptionSlider, ModOptionFloatValues(10f, 180f, 10f)]
        [ModOption("Aim Cone", "First target must be within this angle of the throw, degrees", category = "Homing Throw")]
        public static float AimCone = 60f;

        [ModOption("Return To Me", "After the last bounce the item flies back to you, like a shield", category = "Homing Throw")]
        public static bool ReturnToPlayer = true;

        [ModOption("Ignore Weak Throws", "Only fast throws home in, so simply dropping items stays normal", category = "Homing Throw")]
        public static bool IgnoreWeakThrows = true;

        [ModOption("Aim Weak Points", "Fly at weak points (head, neck, heart) instead of the chest", category = "Homing Throw")]
        public static bool AimWeakPoints = true;

        [ModOption("Knock Down", "Enemies hit by a homing item fall over", category = "Homing Throw")]
        public static bool KnockDown = true;

        [ModOptionSlider, ModOptionFloatValues(0f, 100f, 5f)]
        [ModOption("Knock Force", "Extra push given to the body part that was hit", category = "Homing Throw")]
        public static float KnockForce = 35f;

        [ModOption("Hit Sound", "Play a sound on every hit and lock-on", category = "Homing Throw")]
        public static bool HitSound = true;

        [ModOptionSlider, ModOptionFloatValues(0f, 1f, 0.1f)]
        [ModOption("Sound Volume", "Volume of the hit sound", category = "Homing Throw")]
        public static float SoundVolume = 0.8f;

        [ModOption("Red Laser", "Red laser to the target and red hitmarker on impact", category = "Homing Throw")]
        public static bool RedLaser = true;

        private Creature hooked;

        public override void ScriptEnable()
        {
            base.ScriptEnable();
            EventManager.onPossess += OnPossess;
            if (Player.currentCreature != null) Hook(Player.currentCreature);
        }

        public override void ScriptDisable()
        {
            EventManager.onPossess -= OnPossess;
            Unhook();
            base.ScriptDisable();
        }

        private void OnPossess(Creature creature, EventTime time)
        {
            if (time != EventTime.OnEnd || creature == null) return;
            Hook(creature);
        }

        private void Hook(Creature creature)
        {
            if (creature == hooked) return;
            Unhook();
            hooked = creature;
            creature.handLeft.OnUnGrabEvent += OnUnGrab;
            creature.handRight.OnUnGrabEvent += OnUnGrab;
        }

        private void Unhook()
        {
            if (hooked == null) return;
            if (hooked.handLeft != null) hooked.handLeft.OnUnGrabEvent -= OnUnGrab;
            if (hooked.handRight != null) hooked.handRight.OnUnGrabEvent -= OnUnGrab;
            hooked = null;
        }

        private void OnUnGrab(Side side, Handle handle, bool throwing, EventTime eventTime)
        {
            if (!Enabled || !throwing || eventTime != EventTime.OnEnd) return;
            Item item = handle != null ? handle.item : null;
            if (item == null || item.handlers.Count > 0) return; // still held by the other hand
            HomingProjectile.Launch(item);
        }
    }
}
