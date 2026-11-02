using System.Collections;
using System.Collections.Generic;
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
        private float autoReloadAt = -1f;

        // Gunslinger spin during reload: visual copies of the meshes turn around the trigger finger.
        private Transform spinPivot;
        private readonly List<MeshRenderer> hiddenRenderers = new List<MeshRenderer>();

        // Dead eye: while time is slowed, the trigger marks targets; they are all shot when it ends.
        private readonly List<Mark> marks = new List<Mark>();
        private bool deadEye;
        private bool executing;

        private struct Mark
        {
            public Transform anchor;     // follows the body part / object that was marked
            public Vector3 localPoint;
            public GameObject marker;
        }

        // Borrowed models have no muzzle: build one from the colliders once the item is in the world.
        private Transform Muzzle => muzzle != null ? muzzle : (muzzle = CreateAutoMuzzle());

        public void Init(ItemModuleRevolver revolverModule, Item revolverItem)
        {
            if (item != null) item.OnHeldActionEvent -= OnHeldAction;
            module = revolverModule;
            item = revolverItem;
            body = GetComponent<Rigidbody>();
            muzzle = FindChild(transform, module.muzzleName) ?? FindChild(transform, "Aim"); // else made on first use
            drum = FindChild(transform, module.drumName);
            ammo = module.maxAmmo;
            item.OnHeldActionEvent += OnHeldAction;
        }

        private void OnDestroy()
        {
            if (item != null) item.OnHeldActionEvent -= OnHeldAction;
            ClearMarks();
            EndSpin();
            DeadEyeFilter.Hide(this);
        }

        private void OnHeldAction(RagdollHand hand, Handle handle, Interactable.Action action)
        {
            if (action == Interactable.Action.UseStart)
            {
                if (deadEye) TryMark(hand);
                else TryFire(hand);
            }
            else if (action == Interactable.Action.AlternateUseStart) StartReload(hand);
        }

        private void Update()
        {
            if (item == null) return;
            UpdateDeadEye();

            if (autoReloadAt > 0f && Time.time >= autoReloadAt && !executing && !deadEye)
            {
                autoReloadAt = -1f;
                StartReload(item.mainHandler);
            }

            if (reloading && Time.time >= reloadDoneTime)
            {
                reloading = false;
                ammo = module.maxAmmo;
                RevolverFx.PlayReloadDone(Muzzle.position, module.volume);
                Haptic(item.mainHandler, 0.6f);
            }

            // Flick the wrist around the barrel to snap the cylinder out and reload.
            if (!reloading && ammo < module.maxAmmo && body != null && item.handlers.Count > 0)
            {
                float roll = Mathf.Abs(Vector3.Dot(body.angularVelocity, Muzzle.forward));
                if (roll > module.flickReloadSpeed) StartReload(item.mainHandler);
            }
        }

        private void TryFire(RagdollHand hand)
        {
            if (reloading || executing || Time.time < nextShotTime) return;
            nextShotTime = Time.time + module.fireCooldown;

            if (ammo <= 0)
            {
                RevolverFx.PlayEmpty(Muzzle.position, module.volume);
                Haptic(hand, 0.2f);
                if (module.autoReload) StartReload(hand);
                return;
            }
            Shoot(AimAssist(Muzzle.position, Muzzle.forward), hand);
        }

        /// <summary>One bullet along <paramref name="direction"/> from the muzzle.</summary>
        private void Shoot(Vector3 direction, RagdollHand hand)
        {
            ammo--;
            Vector3 origin = Muzzle.position;
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
            if (drum != null) drum.Rotate(Muzzle.forward, 60f, Space.World);
            if (ammo <= 0 && module.autoReload && !executing) autoReloadAt = Time.time + module.autoReloadDelay;
        }

        // ------------------------------------------------------------ dead eye

        private void UpdateDeadEye()
        {
            bool held = item.handlers.Count > 0 && item.mainHandler != null
                        && item.mainHandler.creature != null && item.mainHandler.creature.isPlayer;
            bool slow = module.deadEye && Time.timeScale < module.deadEyeTimeScale;

            if (!deadEye && slow && held && !executing)
            {
                deadEye = true;
                DeadEyeFilter.Show(this);
                RevolverFx.PlayMark(Muzzle.position, module.volume);
            }
            else if (deadEye && (!slow || !held))
            {
                deadEye = false;
                DeadEyeFilter.Hide(this);
                if (held && marks.Count > 0) StartCoroutine(ExecuteMarks());
                else ClearMarks();
            }
        }

        private void TryMark(RagdollHand hand)
        {
            if (reloading || executing) return;
            if (marks.Count >= ammo)
            {
                RevolverFx.PlayEmpty(Muzzle.position, module.volume);
                Haptic(hand, 0.2f);
                return;
            }

            Vector3 origin = Muzzle.position;
            Vector3 direction = AimAssist(origin, Muzzle.forward);
            if (!Cast(origin, direction, out RaycastHit hit)) return;

            Transform anchor = hit.collider.transform;
            Mark mark = new Mark
            {
                anchor = anchor,
                localPoint = anchor.InverseTransformPoint(hit.point),
                marker = RevolverFx.MarkCross(anchor, hit.point, hit.normal),
            };
            marks.Add(mark);
            RevolverFx.PlayMark(hit.point, module.volume);
            Haptic(hand, 0.3f);
        }

        /// <summary>Slow motion is over: fire at every mark, one after another.</summary>
        private IEnumerator ExecuteMarks()
        {
            executing = true;
            List<Mark> queue = new List<Mark>(marks);
            marks.Clear();
            foreach (Mark mark in queue)
            {
                if (mark.marker != null) Destroy(mark.marker);
                if (mark.anchor == null || ammo <= 0) continue;
                Vector3 target = mark.anchor.TransformPoint(mark.localPoint);
                Shoot((target - Muzzle.position).normalized, item.mainHandler);
                yield return new WaitForSecondsRealtime(module.deadEyeShotInterval);
            }
            executing = false;
            if (ammo <= 0 && module.autoReload) autoReloadAt = Time.time + module.autoReloadDelay;
        }

        private void ClearMarks()
        {
            foreach (Mark mark in marks)
                if (mark.marker != null) Destroy(mark.marker);
            marks.Clear();
        }

        private void StartReload(RagdollHand hand)
        {
            if (reloading || executing || deadEye || ammo >= module.maxAmmo) return;
            reloading = true;
            reloadDoneTime = Time.time + module.reloadTime;
            autoReloadAt = -1f;
            if (module.spinOnReload) StartCoroutine(Spin(module.reloadTime));
            RevolverFx.PlayReloadStart(Muzzle.position, module.volume);
            Haptic(hand, 0.4f);
        }

        /// <summary>
        /// Twirl the revolver around the trigger finger. Only copies of the visible meshes
        /// rotate; colliders and the hand grip stay put, so physics and the hand don't jerk.
        /// </summary>
        private IEnumerator Spin(float duration)
        {
            EndSpin();
            Transform muzzleTransform = Muzzle;
            Handle handle = item.mainHandleRight != null ? item.mainHandleRight : GetComponentInChildren<Handle>();
            Vector3 pivotPoint = handle != null
                ? handle.transform.position + muzzleTransform.forward * 0.03f
                : transform.position;

            spinPivot = new GameObject("RevolverSpin").transform;
            spinPivot.SetParent(transform, false);
            spinPivot.position = pivotPoint;
            spinPivot.rotation = transform.rotation;
            Vector3 axis = transform.InverseTransformDirection(muzzleTransform.right);

            foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!renderer.enabled) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                GameObject copy = new GameObject(renderer.name + "_Spin");
                copy.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                copy.transform.localScale = renderer.transform.lossyScale;
                copy.transform.SetParent(spinPivot, true);
                copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;

                renderer.enabled = false;
                hiddenRenderers.Add(renderer);
            }

            float elapsed = 0f;
            while (elapsed < duration && spinPivot != null)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                k = 1f - (1f - k) * (1f - k);                        // fast start, settles at the end
                spinPivot.localRotation = Quaternion.AngleAxis(-360f * module.spinTurns * k, axis);
                yield return null;
            }
            EndSpin();
        }

        private void EndSpin()
        {
            foreach (MeshRenderer renderer in hiddenRenderers)
                if (renderer != null) renderer.enabled = true;
            hiddenRenderers.Clear();
            if (spinPivot != null) Destroy(spinPivot.gameObject);
            spinPivot = null;
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
            body.AddTorque(-Muzzle.right * module.recoilAngle, ForceMode.VelocityChange);
            body.AddForce(-Muzzle.forward * module.recoilForce, ForceMode.VelocityChange);
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

        /// <summary>
        /// For a borrowed model (a dagger, a sword...) there is no muzzle: shoot from the
        /// far end of the item, in the direction from the handle to that end (the blade tip).
        /// </summary>
        private Transform CreateAutoMuzzle()
        {
            Transform existing = FindChild(transform, "AutoMuzzle");
            if (existing != null) return existing;

            Handle handle = item.mainHandleRight != null ? item.mainHandleRight : GetComponentInChildren<Handle>();
            Vector3 grip = handle != null ? handle.transform.position : transform.position;

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (Collider c in colliders)
            {
                if (c.isTrigger) continue;
                if (!any) { bounds = c.bounds; any = true; }
                else bounds.Encapsulate(c.bounds);
            }

            Vector3 direction = (bounds.center - grip).sqrMagnitude > 0.0001f
                ? (bounds.center - grip).normalized
                : transform.forward;
            float reach = 0.1f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                reach = Mathf.Max(reach, Vector3.Dot(corner - grip, direction));
            }

            Transform muzzleTransform = new GameObject("AutoMuzzle").transform;
            muzzleTransform.SetParent(transform, true);
            muzzleTransform.position = grip + direction * (reach + 0.02f);
            muzzleTransform.rotation = Quaternion.LookRotation(direction, transform.up);
            return muzzleTransform;
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
