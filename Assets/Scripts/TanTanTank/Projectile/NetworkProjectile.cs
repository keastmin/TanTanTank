using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(SphereCollider))]
    public sealed class NetworkProjectile : NetworkBehaviour
    {
        public static readonly List<NetworkProjectile> Instances = new();

        [Networked] public Vector3 Direction { get; private set; }
        [Networked] public PlayerRef OwnerPlayer { get; private set; }
        [Networked] public int WallHitCount { get; private set; }
        [Networked] public float FlightHeight { get; private set; }
        [Networked] private TickTimer OwnerIgnoreTimer { get; set; }

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private GameBalanceConfig _balance;
        private SphereCollider _sphere;
        private int _wallMask;
        private float _radius;
        private bool _hasClearedOwnerHurtbox;

        private void Awake()
        {
            _balance = Resources.Load<GameBalanceConfig>("TanTanTank/Game Balance");
            _sphere = GetComponent<SphereCollider>();
            _wallMask = LayerMask.GetMask("Wall");
        }

        public override void Spawned()
        {
            if (!Instances.Contains(this))
                Instances.Add(this);
            _radius = _sphere != null
                ? _sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z)
                : 0.4f;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            Instances.Remove(this);
        }

        public void Initialize(PlayerRef owner, Vector3 direction, float flightHeight)
        {
            if (!Object.HasStateAuthority)
                return;
            OwnerPlayer = owner;
            Direction = ProjectileTrajectory.FlattenDirection(direction);
            FlightHeight = flightHeight;
            WallHitCount = 0;
            _hasClearedOwnerHurtbox = false;
            OwnerIgnoreTimer = TickTimer.CreateFromSeconds(Runner,
                _balance != null ? _balance.ownerIgnoreTime : 0.2f);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || _balance == null || MatchController.Instance == null ||
                MatchController.Instance.State != GameRoundState.RoundActive)
                return;

            var remainingDistance = _balance.projectileSpeed * Runner.DeltaTime;
            var position = transform.position;
            position.y = FlightHeight;
            Physics.SyncTransforms();
            UpdateOwnerClearance(position);

            for (var iteration = 0;
                 iteration < _balance.maxCollisionIterationsPerTick && remainingDistance > 0.0001f;
                 iteration++)
            {
                var hitCount = Physics.SphereCastNonAlloc(position, _radius, Direction, _hits,
                    remainingDistance, _wallMask, QueryTriggerInteraction.Ignore);
                var hasWallHit = TryGetClosestWallHit(hitCount, out var closestWall);
                var hasHurtboxHit = TryGetClosestHurtboxHit(position, Direction, remainingDistance,
                    out var hurtboxDistance, out var hurtbox);

                if (hasHurtboxHit && (!hasWallHit || hurtboxDistance <= closestWall.distance))
                {
                    position += Direction * hurtboxDistance;
                    DamageAndDespawn(hurtbox, position);
                    return;
                }

                if (!hasWallHit)
                {
                    position += Direction * remainingDistance;
                    remainingDistance = 0f;
                    break;
                }

                position += Direction * closestWall.distance;
                remainingDistance -= closestWall.distance;

                WallHitCount++;
                if (WallHitCount >= _balance.projectileMaxWallHits)
                {
                    transform.position = position;
                    Runner.Despawn(Object);
                    return;
                }

                Direction = ProjectileTrajectory.Reflect(Direction, closestWall.normal);
                position += Direction * _balance.projectileSurfaceEpsilon;
                remainingDistance = Mathf.Max(0f, remainingDistance - _balance.projectileSurfaceEpsilon);
            }

            transform.position = position;
        }

        private bool TryGetClosestHurtboxHit(Vector3 position, Vector3 direction, float maxDistance,
            out float closestDistance, out TankHurtbox closestHurtbox)
        {
            closestDistance = float.MaxValue;
            closestHurtbox = null;
            for (var i = 0; i < TankHurtbox.Instances.Count; i++)
            {
                var candidate = TankHurtbox.Instances[i];
                if (!CanDamage(candidate) || candidate.HitCollider == null || !candidate.HitCollider.enabled)
                    continue;

                var bounds = candidate.HitCollider.bounds;
                bounds.Expand(new Vector3(_radius * 2f, 0f, _radius * 2f));
                var horizontalOrigin = new Vector3(position.x, bounds.center.y, position.z);
                var ray = new Ray(horizontalOrigin, direction);
                var distance = bounds.Contains(horizontalOrigin) ? 0f : float.MaxValue;
                if (distance == float.MaxValue && !bounds.IntersectRay(ray, out distance))
                    continue;
                if (distance > maxDistance || distance >= closestDistance)
                    continue;

                closestDistance = Mathf.Max(0f, distance);
                closestHurtbox = candidate;
            }

            return closestHurtbox != null;
        }

        private void DamageAndDespawn(TankHurtbox hurtbox, Vector3 position)
        {
            hurtbox.Owner.ApplyDamage(_balance.projectileDamage);
            transform.position = position;
            Runner.Despawn(Object);
        }

        private bool TryGetClosestWallHit(int hitCount, out RaycastHit closest)
        {
            closest = default;
            var closestDistance = float.MaxValue;

            for (var i = 0; i < hitCount; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || hit.distance >= closestDistance)
                    continue;

                closestDistance = hit.distance;
                closest = hit;
            }

            return closestDistance < float.MaxValue;
        }

        private bool CanDamage(TankHurtbox hurtbox)
        {
            if (hurtbox == null || hurtbox.Owner == null || hurtbox.Owner.Object == null ||
                !hurtbox.Owner.Object.IsValid)
                return false;

            if (hurtbox.Owner.Object.InputAuthority != OwnerPlayer)
                return true;

            return _hasClearedOwnerHurtbox && OwnerIgnoreTimer.ExpiredOrNotRunning(Runner);
        }

        private void UpdateOwnerClearance(Vector3 position)
        {
            if (_hasClearedOwnerHurtbox)
                return;

            for (var i = 0; i < TankHurtbox.Instances.Count; i++)
            {
                var candidate = TankHurtbox.Instances[i];
                if (candidate == null || candidate.Owner == null || candidate.Owner.Object == null ||
                    !candidate.Owner.Object.IsValid || candidate.Owner.Object.InputAuthority != OwnerPlayer ||
                    candidate.HitCollider == null)
                    continue;

                var bounds = candidate.HitCollider.bounds;
                bounds.Expand(new Vector3(_radius * 2f, 0f, _radius * 2f));
                var horizontalPosition = new Vector3(position.x, bounds.center.y, position.z);
                _hasClearedOwnerHurtbox = !bounds.Contains(horizontalPosition);
                return;
            }

            _hasClearedOwnerHurtbox = true;
        }
    }
}
