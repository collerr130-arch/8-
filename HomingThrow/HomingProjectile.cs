using ThunderRoad;
using UnityEngine;

namespace HomingThrow
{
    /// <summary>
    /// Lives on a thrown item and flies it like a ricochet shield:
    /// - Seeking: heads for the target, locking straight onto it when close.
    /// - Missed: if it flew past the target, it stops turning and flies straight
    ///   until it hits a wall; the wall bounce sends it straight at the target.
    /// - After hitting an enemy it ricochets to the next one.
    /// - Returning: after the last bounce it flies back to the player.
    /// Removes itself when done.
    /// </summary>
    public class HomingProjectile : MonoBehaviour
    {
        private enum State { Seeking, Missed, Returning }

        private const float StartDelay = 0.05f;    // let the throw velocity settle, clear the hand
        private const float MaxFlightTime = 12f;
        private const float ReturnTime = 5f;
        private const float CatchDistance = 1.2f;
        private const float MinThrowSpeed = 3f;
        private const float LockOnDistance = 4f;   // closer than this: fly dead straight at the target
        private const float MissDistance = 6f;     // passing the target within this counts as a miss
        private const float MaxMissedTime = 1.5f;  // no wall in that time: turn back towards the target

        private Item item;
        private Rigidbody body;
        private CollisionDetectionMode originalDetection;
        private Creature target;
        private Transform aimPoint;
        private Creature lastHit;
        private State state;
        private int bounces;
        private float speed;
        private float startTime;
        private float endTime;
        private float missedUntil;
        private float ignoreCollisionsUntil;
        private bool started;
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
                Destroy(this);
                return;
            }

            switch (state)
            {
                case State.Seeking: Seek(); break;
                case State.Missed: FlyStraight(); break;
                case State.Returning: Return(); break;
            }
        }

        private void Seek()
        {
            if (!IsValidTarget(target))
            {
                SetTarget(FindTarget(item.physicBody.velocity, lastHit, 180f));
                if (target == null) { EndRicochet(); return; }
            }

            Vector3 toTarget = AimPosition() - item.transform.position;
            float distance = toTarget.magnitude;
            Vector3 velocity = item.physicBody.velocity;

            // Flew past it: don't circle around, keep going to the next wall.
            if (distance < MissDistance && Vector3.Dot(velocity, toTarget) < 0f)
            {
                state = State.Missed;
                missedUntil = Time.time + MaxMissedTime;
                SetVelocity(velocity.normalized * speed);
                return;
            }

            Vector3 desired = toTarget.normalized * speed;
            if (distance < LockOnDistance)
            {
                SetVelocity(desired);
            }
            else
            {
                float t = Mathf.Clamp01(HomingThrowScript.Steering * Time.fixedDeltaTime);
                SetVelocity(Vector3.Lerp(velocity, desired, t).normalized * speed);
            }
        }

        private void FlyStraight()
        {
            if (Time.time > missedUntil)
            {
                state = State.Seeking; // open space, no wall: just turn back
                return;
            }
            Vector3 velocity = lastVelocity.sqrMagnitude > 0.01f ? lastVelocity : item.physicBody.velocity;
            SetVelocity(velocity.normalized * speed);
        }

        private void Return()
        {
            Creature player = Player.currentCreature;
            if (player == null) { Destroy(this); return; }
            Vector3 goal = player.ragdoll.targetPart.transform.position + Vector3.up * 0.3f;
            Vector3 toGoal = goal - item.transform.position;
            if (toGoal.magnitude < CatchDistance)
            {
                // Slow down right in front of the player so it can be caught.
                item.physicBody.velocity = toGoal.normalized * 1f;
                Destroy(this);
                return;
            }
            SetVelocity(toGoal.normalized * speed);
        }

        private bool Begin()
        {
            Vector3 velocity = item.physicBody.velocity;
            if (HomingThrowScript.IgnoreWeakThrows && velocity.magnitude < MinThrowSpeed) return false;

            Creature first = FindTarget(velocity, null, HomingThrowScript.AimCone);
            if (first == null) return false; // nobody in front: a normal throw

            started = true;
            state = State.Seeking;
            speed = Mathf.Max(HomingThrowScript.Speed, velocity.magnitude);
            originalGravity = item.physicBody.useGravity;
            item.physicBody.useGravity = false;

            // Fast items can tunnel through thin enemies and walls without this.
            body = item.gameObject.GetComponent<Rigidbody>();
            if (body != null)
            {
                originalDetection = body.collisionDetectionMode;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            lastVelocity = velocity;
            SetTarget(first);
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

            if (state == State.Returning)
            {
                SetVelocity(reflected * speed);
                return;
            }

            if (hit != null) Strike(hit, collision, normal);

            bounces++;
            if (bounces > Mathf.RoundToInt(HomingThrowScript.MaxBounces))
            {
                EndRicochet();
                return;
            }

            if (hit != null)
            {
                // Hit an enemy: ricochet on to the next one (or the same one if it's alone).
                lastHit = hit;
                SetTarget(FindTarget(reflected, lastHit, 180f));
                if (target == null) { EndRicochet(); return; }
                SetVelocity(reflected * speed);
            }
            else
            {
                // Hit a wall: bank the shot straight into the target.
                if (!IsValidTarget(target)) SetTarget(FindTarget(reflected, lastHit, 180f));
                else HitEffects.Laser(item.transform.position, AimPosition());
                if (target == null) { EndRicochet(); return; }
                SetVelocity((AimPosition() - item.transform.position).normalized * speed);
            }
            state = State.Seeking;
        }

        /// <summary>Sound, red hitmarker, knock-down and push for an enemy that was hit.</summary>
        private void Strike(Creature hit, Collision collision, Vector3 normal)
        {
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : item.transform.position;
            HitEffects.PlayHit(point);
            HitEffects.HitMarker(point, normal);

            if (HomingThrowScript.KnockDown && hit.ragdoll != null)
                hit.ragdoll.SetState(Ragdoll.State.Destabilized);

            if (HomingThrowScript.KnockForce > 0f && collision.rigidbody != null)
                collision.rigidbody.AddForce(lastVelocity.normalized * HomingThrowScript.KnockForce, ForceMode.Impulse);
        }

        private void SetTarget(Creature creature)
        {
            target = creature;
            aimPoint = creature != null ? ChooseAimPoint(creature) : null;
            if (creature == null) return;
            HitEffects.Laser(item.transform.position, AimPosition());
            HitEffects.PlayLockOn(item.transform.position);
        }

        /// <summary>The closest weak point (head, neck, heart...) or the chest.</summary>
        private Transform ChooseAimPoint(Creature creature)
        {
            Transform best = creature.ragdoll.targetPart.transform;
            if (!HomingThrowScript.AimWeakPoints || creature.weakpoints == null) return best;

            Vector3 position = item.transform.position;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < creature.weakpoints.Count; i++)
            {
                Transform weakpoint = creature.weakpoints[i];
                if (weakpoint == null) continue;
                float distance = (weakpoint.position - position).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = weakpoint; }
            }
            return best;
        }

        private Vector3 AimPosition()
        {
            if (aimPoint == null && target != null) aimPoint = ChooseAimPoint(target);
            return aimPoint != null ? aimPoint.position : TargetPoint(target);
        }

        private void SetVelocity(Vector3 velocity)
        {
            item.physicBody.velocity = velocity;
            lastVelocity = velocity;
        }

        private static Vector3 TargetPoint(Creature creature)
        {
            return creature.ragdoll.targetPart.transform.position;
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

                Vector3 toCreature = TargetPoint(creature) - position;
                float distance = toCreature.magnitude;
                if (distance > range) continue;

                float angle = Vector3.Angle(direction, toCreature);
                if (angle > cone) continue;

                float score = distance * (1f + angle / 45f);
                if (score < bestScore) { bestScore = score; best = creature; }
            }

            // Only one enemy left: keep hitting it.
            if (best == null && IsValidTarget(avoid)
                && (TargetPoint(avoid) - position).magnitude <= range)
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
                state = State.Returning;
                target = null;
                aimPoint = null;
                endTime = Time.time + ReturnTime;
            }
            else
            {
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            if (!started || item == null) return;
            if (item.physicBody != null) item.physicBody.useGravity = originalGravity;
            if (body != null) body.collisionDetectionMode = originalDetection;
        }
    }
}
