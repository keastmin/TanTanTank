using System.Collections.Generic;
using UnityEngine;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LineRenderer))]
    public sealed class AimPrediction : MonoBehaviour
    {
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly List<Vector3> _points = new(10);
        private TankNetworkController _tank;
        private LineRenderer _line;
        private GameBalanceConfig _balance;
        private int _wallMask;
        private float _radius = 0.4f;

        private void Awake()
        {
            _tank = GetComponent<TankNetworkController>();
            _line = GetComponent<LineRenderer>();
            _balance = Resources.Load<GameBalanceConfig>("TanTanTank/Game Balance");
            _wallMask = LayerMask.GetMask("Wall");
            var projectile = Resources.Load<GameObject>("TanTanTank/Network/Projectile");
            var sphere = projectile != null ? projectile.GetComponent<SphereCollider>() : null;
            if (sphere != null)
                _radius = sphere.radius * Mathf.Max(projectile.transform.localScale.x, projectile.transform.localScale.z);
            if (_line.sharedMaterial == null)
                _line.sharedMaterial = Resources.Load<Material>("TanTanTank/Aim Preview");
            _line.useWorldSpace = true;
            _line.startWidth = Mathf.Max(_line.startWidth, 0.3f);
            _line.endWidth = Mathf.Max(_line.endWidth, 0.3f);
            _line.enabled = false;
        }

        private void LateUpdate()
        {
            var match = MatchController.Instance;
            var shouldShow = _tank != null && _tank.Object != null && _tank.HasLocalControl &&
                             match != null && match.State == GameRoundState.RoundActive && _tank.HP > 0;
            if (!shouldShow || _balance == null)
            {
                _line.enabled = false;
                return;
            }

            _points.Clear();
            var position = _tank.FirePosition + Vector3.up * 0.03f;
            var direction = _tank.FireDirection;
            var remaining = _balance.aimPreviewMaxDistance;
            _points.Add(position);

            for (var iteration = 0; iteration < _balance.maxCollisionIterationsPerTick && remaining > 0.0001f; iteration++)
            {
                var hitCount = Physics.SphereCastNonAlloc(position, _radius, direction, _hits, remaining, _wallMask,
                    QueryTriggerInteraction.Ignore);
                var closestDistance = float.MaxValue;
                var closest = default(RaycastHit);
                for (var i = 0; i < hitCount; i++)
                {
                    if (_hits[i].distance < closestDistance)
                    {
                        closestDistance = _hits[i].distance;
                        closest = _hits[i];
                    }
                }

                if (closestDistance == float.MaxValue)
                {
                    _points.Add(position + direction * remaining);
                    remaining = 0f;
                    break;
                }

                position += direction * closestDistance;
                remaining -= closestDistance;
                _points.Add(position);
                direction = ProjectileTrajectory.Reflect(direction, closest.normal);
                position += direction * _balance.projectileSurfaceEpsilon;
                remaining = Mathf.Max(0f, remaining - _balance.projectileSurfaceEpsilon);
            }

            _line.positionCount = _points.Count;
            for (var i = 0; i < _points.Count; i++)
                _line.SetPosition(i, _points[i]);
            _line.enabled = _points.Count > 1;
        }
    }
}
