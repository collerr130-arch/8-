using ThunderRoad;
using UnityEngine;

namespace HomingThrow
{
    /// <summary>
    /// Lives on a thrown item: steers it at the nearest enemy, reflects it off
    /// walls and enemies, picks the next enemy after each hit, and optionally
    /// flies back to the player at the end. Removes itself when done.
    /// </summary>
    public class HomingProjectile : MonoBehaviour
    {
        private const float StartDelay = 0.05f;   // let the throw velocity settle, clear the hand
        private const float MaxFlightTime = 10f;
        private const float ReturnTime = 5f;
        private const float CatchDistance = 1.2f;
        private const float MinThrowSpeed = 3f;

        private Item item;
        private Creature target;
        private Creature lastHit;
        private int bounces;
        private float speed;
        private float startTime;
        private float endTime;
        private float ignoreCollisionsUntil;
        private bool started;
        private bool returning;
        private bool originalGravity;
        private Vector3 lastVelocity;

        public static void Launch(Item item)
        {
            HomingProjectile existing = item.gameObject.GetComponent<HomingProjectile>();
            if (existing != null) Destroy(existing);
            item.gameObject.AddComponent<HomingProjectile>().Init(item);
        }

        private void Init(Item thrown)
        {
            item = thrown;
            startTime = Time.time + StartDelay;
            endTime = Time.time + MaxFlightTime;
        }

        private void FixedUpdate()
        {
            if (item == null) { Destroy(this); return; }
            if (!started)
            {
                if (Time.time < startTime) return;
                if (!Begin()) { Destroy(this); return; }
            }

            if (!HomingThrowScript.Enabled || Time.time > endTime
                || item.handlers.Count > 0 || item.isTelekinesisGrabbed || item.isPenetrating)
            {
                Stop();
                return;
            }

            Vector3 position = item.transform.position;
            Vector3 goal;

            if (returning)
            {
                Creature player = Player.currentCreature;
                if (player == null) { Stop(); return; }
                goal = player.ragdoll.targetPart.transform.position + Vector3.up * 0.3f;
                if ((goal - position).sqrMagnitude < CatchDistance * CatchDistance)
                {
                    // Slow down right in front of the player so it can be caught.
                    item.physicBody.velocity *= 0.15f;
                    Stop();
                    return;
                }
            }
            else
            {
                if (!IsValidTarget(target))
                {
                    target = FindTarget(item.physicBody.velocity, lastHit, 180f);
                    if (target == null) { EndRicochet(); return; }
                }
                goal = target.ragdoll.targetPart.transform.position;
            }

            Vector3 desired = (goal - position).normalized * speed;
            float t = Mathf.Clamp01(HomingThrowScript.Steering * Time.fixedDeltaTime);
            item.physicBody.velocity = Vector3.Lerp(item.physicBody.velocity, desired, t).normalized * speed;
            lastVelocity = item.physicBody.velocity;
        }

        private bool Begin()
        {
            Vector3 velocity = item.physicBody.velocity;
            if (HomingThrowScript.IgnoreWeakThrows && velocity.magnitude < MinThrowSpeed) return false;

            target = FindTarget(velocity, null, HomingThrowScript.AimCone);
            if (target == null) return false; // nobody in front: a normal throw

            started = true;
            speed = Mathf.Max(HomingThrowScript.Speed, velocity.magnitude);
            originalGravity = item.physicBody.useGravity;
            item.physicBody.useGravity = false;
            lastVelocity = velocity;
            return true;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!started || Time.time < ignoreCollisionsUntil) return;

            Creature hit = collision.collider.GetComponentInParent<Creature>();
            if (hit == null)
            {
                RagdollPart part = collision.collider.GetComponentInParent<RagdollPart>();
                if (part != null && part.ragdoll != null) hit = part.ragdoll.creature;
            }
            if (hit != null && hit.isPlayer) return; // never bounce off ourselves

            ignoreCollisionsUntil = Time.time + 0.08f;

            Vector3 normal = collision.contactCount > 0
                ? collision.GetContact(0).normal
                : -lastVelocity.normalized;
            Vector3 reflected = Vector3.Reflect(lastVelocity, normal).normalized;
            if (reflected == Vector3.zero) reflected = normal;

            if (returning)
            {
                item.physicBody.velocity = reflected * speed;
                lastVelocity = item.physicBody.velocity;
                return;
            }

            bounces++;
            if (hit != null) lastHit = hit;

            if (bounces > Mathf.RoundToInt(HomingThrowScript.MaxBounces))
            {
                EndRicochet();
                return;
            }

            // Any direction is fine after a ricochet; prefer someone we did not just hit.
            target = FindTarget(reflected, lastHit, 180f);
            item.physicBody.velocity = reflected * speed;
            lastVelocity = item.physicBody.velocity;
            if (target == null) EndRicochet();
        }

        private Creature FindTarget(Vector3 direction, Creature avoid, float cone)
        {
            Vector3 position = item.transform.position;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
            float range = HomingThrowScript.Range;

            Creature best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < Creature.allActive.Count; i++)
            {
                Creature creature = Creature.allActive[i];
                if (!IsValidTarget(creature) || creature == avoid) continue;

                Vector3 toCreature = creature.ragdoll.targetPart.transform.position - position;
                float distance = toCreature.magnitude;
                if (distance > range) continue;

                float angle = Vector3.Angle(direction, toCreature);
                if (angle > cone) continue;

                float score = distance * (1f + angle / 45f);
                if (score < bestScore) { bestScore = score; best = creature; }
            }

            // Only one enemy left: keep hitting it.
            if (best == null && IsValidTarget(avoid)
                && (avoid.ragdoll.targetPart.transform.position - position).magnitude <= range)
                best = avoid;
            return best;
        }

        private static bool IsValidTarget(Creature creature)
        {
            return creature != null && !creature.isPlayer && !creature.isKilled
                && creature.ragdoll != null && creature.ragdoll.targetPart != null;
        }

        private void EndRicochet()
        {
            if (HomingThrowScript.ReturnToPlayer && Player.currentCreature != null)
            {
                returning = true;
                target = null;
                endTime = Time.time + ReturnTime;
            }
            else
            {
                Stop();
            }
        }

        private void Stop()
        {
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (started && item != null && item.physicBody != null)
                item.physicBody.useGravity = originalGravity;
        }
    }
}
