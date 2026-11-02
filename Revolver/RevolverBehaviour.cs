using ThunderRoad;
using UnityEngine;

namespace Revolver
{
    /// <summary>
    /// Trigger fires a hitscan shot from the muzzle; the alternate-use button or
    /// a fast wrist flick reloads the six-shot cylinder.
    /// </summary>
    public class RevolverBehaviour : MonoBehaviour
    {
        private ItemModuleRevolver module;
        private Item item;
        private Rigidbody body;
        private Transform muzzle;
        private Transform drum;

        private int ammo;
        private float nextShotTime;
        private float reloadDoneTime;
        private bool reloading;

        public void Init(ItemModuleRevolver revolverModule, Item revolverItem)
        {
            if (item != null) item.OnHeldActionEvent -= OnHeldAction;
            module = revolverModule;
            item = revolverItem;
            body = GetComponent<Rigidbody>();
            muzzle = FindChild(transform, module.muzzleName) ?? FindChild(transform, "Aim") ?? transform;
            drum = FindChild(transform, module.drumName);
            ammo = module.maxAmmo;
            item.OnHeldActionEvent += OnHeldAction;
        }

        private void OnDestroy()
        {
            if (item != null) item.OnHeldActionEvent -= OnHeldAction;
        }

        private void OnHeldAction(RagdollHand hand, Handle handle, Interactable.Action action)
        {
            if (action == Interactable.Action.UseStart) TryFire(hand);
            else if (action == Interactable.Action.AlternateUseStart) StartReload(hand);
        }

        private void Update()
        {
            if (item == null) return;

            if (reloading && Time.time >= reloadDoneTime)
            {
                reloading = false;
                ammo = module.maxAmmo;
                RevolverFx.PlayReloadDone(muzzle.position, module.volume);
                Haptic(item.mainHandler, 0.6f);
            }

            // Flick the wrist around the barrel to snap the cylinder out and reload.
            if (!reloading && ammo < module.maxAmmo && body != null && item.handlers.Count > 0)
            {
                float roll = Mathf.Abs(Vector3.Dot(body.angularVelocity, muzzle.forward));
                if (roll > module.flickReloadSpeed) StartReload(item.mainHandler);
            }
        }

        private void TryFire(RagdollHand hand)
        {
            if (reloading || Time.time < nextShotTime) return;
            nextShotTime = Time.time + module.fireCooldown;

            if (ammo <= 0)
            {
                RevolverFx.PlayEmpty(muzzle.position, module.volume);
                Haptic(hand, 0.2f);
                return;
            }
            ammo--;

            Vector3 origin = muzzle.position;
            Vector3 direction = AimAssist(origin, muzzle.forward);
            Vector3 end = origin + direction * module.range;

            if (Cast(origin, direction, out RaycastHit hit))
            {
                end = hit.point;
                HitTarget(hit, direction);
            }

            RevolverFx.PlayShot(origin, module.volume);
            RevolverFx.MuzzleFlash(origin, direction);
            if (module.redTracer) RevolverFx.Tracer(origin, end);
            Haptic(hand, 1f);
            Recoil();
            if (drum != null) drum.Rotate(muzzle.forward, 60f, Space.World);
        }

        private void StartReload(RagdollHand hand)
        {
            if (reloading || ammo >= module.maxAmmo) return;
            reloading = true;
            reloadDoneTime = Time.time + module.reloadTime;
            RevolverFx.PlayReloadStart(muzzle.position, module.volume);
            Haptic(hand, 0.4f);
        }

        /// <summary>Nearest hit along the ray, ignoring the gun itself and the player.</summary>
        private bool Cast(Vector3 origin, Vector3 direction, out RaycastHit best)
        {
            best = default(RaycastHit);
            RaycastHit[] hits = Physics.RaycastAll(origin, direction, module.range, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.distance >= bestDistance || hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;
                Creature creature = CreatureOf(hit.collider);
                if (creature != null && creature.isPlayer) continue;
                best = hit;
                bestDistance = hit.distance;
                found = true;
            }
            return found;
        }

        private void HitTarget(RaycastHit hit, Vector3 direction)
        {
            RagdollPart part = hit.collider.GetComponentInParent<RagdollPart>();
            Creature creature = CreatureOf(hit.collider);

            if (creature != null && !creature.isKilled)
            {
                float damage = module.damage;
                if (part != null && part.type == RagdollPart.Type.Head) damage *= module.headshotMultiplier;
                else if (part != null && part.type == RagdollPart.Type.Neck) damage *= module.neckMultiplier;

                if (module.knockDown && creature.ragdoll != null)
                    creature.ragdoll.SetState(Ragdoll.State.Destabilized);
                creature.Damage(damage);
                RevolverFx.HitMarker(hit.point, hit.normal);
                RevolverFx.PlayHit(hit.point, module.volume);
            }
            else
            {
                RevolverFx.Spark(hit.point, hit.normal);
            }

            if (hit.rigidbody != null)
                hit.rigidbody.AddForceAtPosition(direction * module.impactForce, hit.point, ForceMode.Impulse);
        }

        /// <summary>Bend the shot onto the closest weak point or head inside a small cone.</summary>
        private Vector3 AimAssist(Vector3 origin, Vector3 forward)
        {
            if (module.aimAssistAngle <= 0f) return forward;
            Vector3 best = forward;
            float bestAngle = module.aimAssistAngle;
            for (int i = 0; i < Creature.allActive.Count; i++)
            {
                Creature creature = Creature.allActive[i];
                if (creature == null || creature.isPlayer || creature.isKilled || creature.ragdoll == null) continue;

                if (creature.weakpoints != null)
                {
                    for (int j = 0; j < creature.weakpoints.Count; j++)
                    {
                        Transform weakpoint = creature.weakpoints[j];
                        if (weakpoint != null) Consider(origin, forward, weakpoint.position, ref best, ref bestAngle);
                    }
                }
                if (creature.ragdoll.headPart != null)
                    Consider(origin, forward, creature.ragdoll.headPart.transform.position, ref best, ref bestAngle);
            }
            return best;
        }

        private static void Consider(Vector3 origin, Vector3 forward, Vector3 point, ref Vector3 best, ref float bestAngle)
        {
            Vector3 to = point - origin;
            float angle = Vector3.Angle(forward, to);
            if (angle < bestAngle) { bestAngle = angle; best = to.normalized; }
        }

        private void Recoil()
        {
            if (body == null) return;
            body.AddTorque(-muzzle.right * module.recoilAngle, ForceMode.VelocityChange);
            body.AddForce(-muzzle.forward * module.recoilForce, ForceMode.VelocityChange);
        }

        private static void Haptic(RagdollHand hand, float intensity)
        {
            if (hand == null || hand.creature == null || !hand.creature.isPlayer) return;
            PlayerControl.GetHand(hand.side).HapticShort(intensity);
        }

        private static Creature CreatureOf(Collider collider)
        {
            RagdollPart part = collider.GetComponentInParent<RagdollPart>();
            if (part != null && part.ragdoll != null) return part.ragdoll.creature;
            return collider.GetComponentInParent<Creature>();
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
