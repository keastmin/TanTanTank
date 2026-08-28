using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanTanTank
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(NetworkTransform), typeof(Rigidbody))]
    public sealed class TankNetworkController : NetworkBehaviour
    {
        public static readonly List<TankNetworkController> Instances = new();
        public static TankNetworkController Local { get; private set; }

        [SerializeField] private Transform turret;
        [SerializeField] private Transform fireTransform;
        [SerializeField] private TankAppearance appearance;

        [Networked] public int PlayerSlot { get; private set; }
        [Networked] public int HP { get; private set; }
        [Networked] public int Score { get; private set; }
        [Networked] public int ColorId { get; private set; }
        [Networked] public NetworkString<_16> Nickname { get; private set; }
        [Networked] public Vector3 AimDirection { get; private set; }
        [Networked] public TickTimer FireCooldown { get; private set; }
        [Networked] private NetworkButtons PreviousButtons { get; set; }

        private GameBalanceConfig _balance;
        private Rigidbody _body;
        private NetworkTransform _networkTransform;
        private Camera _camera;
        private Vector3 _localAimDirection;
        private float _fireHeightFromRoot;

        public Vector3 TurretPosition => turret != null ? turret.position : transform.position;
        public Vector3 FirePosition
        {
            get
            {
                var position = fireTransform != null ? fireTransform.position : TurretPosition;
                position.y = transform.position.y + _fireHeightFromRoot;
                return position;
            }
        }
        public Vector3 FireDirection
        {
            get
            {
                var direction = IsLocalPlayer() && _localAimDirection.sqrMagnitude > 0.001f
                    ? _localAimDirection
                    : AimDirection;
                return ProjectileTrajectory.FlattenDirection(direction);
            }
        }
        public bool IsFireReady => Runner == null || FireCooldown.ExpiredOrNotRunning(Runner);
        public bool HasLocalControl => IsLocalPlayer();
        public float RemainingCooldown => Runner != null ? FireCooldown.RemainingTime(Runner) ?? 0f : 0f;
        public float NormalizedCooldown => _balance == null || _balance.fireCooldown <= 0f
            ? 0f
            : Mathf.Clamp01(1f - RemainingCooldown / _balance.fireCooldown);

        private void Awake()
        {
            turret ??= transform.FindDeepChild("Turret");
            fireTransform ??= transform.FindDeepChild("Fire Transform");
            appearance ??= GetComponent<TankAppearance>();
            _body = GetComponent<Rigidbody>();
            _networkTransform = GetComponent<NetworkTransform>();
            _balance = Resources.Load<GameBalanceConfig>("TanTanTank/Game Balance");
            _fireHeightFromRoot = fireTransform != null
                ? Mathf.Max(0.1f, fireTransform.position.y - transform.position.y)
                : Mathf.Max(0.8f, TurretPosition.y - transform.position.y);
        }

        private void OnEnable()
        {
            Application.onBeforeRender += ApplyCurrentVisualAim;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyCurrentVisualAim;
        }

        public void ConfigureReferences(Transform turretTransform, Transform muzzleFireTransform,
            TankAppearance tankAppearance)
        {
            turret = turretTransform;
            fireTransform = muzzleFireTransform;
            appearance = tankAppearance;
        }

        public override void Spawned()
        {
            if (!Instances.Contains(this))
                Instances.Add(this);
            if (IsLocalPlayer())
            {
                Local = this;
                _localAimDirection = AimDirection.sqrMagnitude > 0.001f ? AimDirection : transform.forward;
            }
            appearance?.ApplyColor(ColorId);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            Instances.Remove(this);
            if (Local == this)
                Local = null;
        }

        public void Initialize(int slot, int colorId, string nickname)
        {
            if (!Object.HasStateAuthority)
                return;
            PlayerSlot = slot;
            ColorId = colorId;
            Nickname = nickname;
            HP = _balance != null ? _balance.playerMaxHp : 3;
            Score = 0;
            AimDirection = transform.forward;
            FireCooldown = TickTimer.None;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || _balance == null)
                return;

            var match = MatchController.Instance;
            var canAct = match != null && match.State == GameRoundState.RoundActive && HP > 0;
            if (!GetInput(out TankNetworkInput input))
                return;

            if (input.AimDirection.sqrMagnitude > 0.001f)
                AimDirection = ProjectileTrajectory.FlattenDirection(input.AimDirection);

            ApplyTurretRotation(AimDirection);

            if (canAct)
            {
                SimulateMovement(input.MoveInput);
                if (input.Buttons.WasPressed(PreviousButtons, (int)TankInputButton.Fire) && IsFireReady)
                {
                    FireCooldown = TickTimer.CreateFromSeconds(Runner, _balance.fireCooldown);
                    match.TryFire(this, FirePosition, FireDirection);
                }
            }

            PreviousButtons = input.Buttons;
        }

        public override void Render()
        {
            appearance?.ApplyColor(ColorId);
            ApplyCurrentVisualAim();
        }

        private void LateUpdate()
        {
            ApplyCurrentVisualAim();
        }

        private void ApplyCurrentVisualAim()
        {
            var direction = IsLocalPlayer() && _localAimDirection.sqrMagnitude > 0.001f
                ? _localAimDirection
                : AimDirection;
            ApplyTurretRotation(direction);
        }

        private void Update()
        {
            if (!IsLocalPlayer())
                return;

            Local = this;

            _camera ??= Camera.main;
            var mouse = Mouse.current;
            if (_camera == null || mouse == null)
                return;

            UpdateLocalAim(_camera, mouse.position.ReadValue());
        }

        public bool UpdateLocalAim(Camera aimCamera, Vector2 screenPosition)
        {
            if (aimCamera == null)
                return false;

            var origin = TurretPosition;
            var ray = aimCamera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, origin);
            if (!plane.Raycast(ray, out var distance))
                return false;

            var direction = ProjectileTrajectory.FlattenDirection(ray.GetPoint(distance) - origin);
            SetLocalAimDirection(direction);
            return true;
        }

        public void SetLocalAimDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f)
                return;

            _localAimDirection = ProjectileTrajectory.FlattenDirection(direction);
            ApplyTurretRotation(_localAimDirection);
        }

        private void ApplyTurretRotation(Vector3 direction)
        {
            if (turret == null || direction.sqrMagnitude < 0.001f)
                return;

            direction = ProjectileTrajectory.FlattenDirection(direction);
            var localDirection = turret.parent != null
                ? turret.parent.InverseTransformDirection(direction)
                : direction;
            localDirection.y = 0f;
            if (localDirection.sqrMagnitude < 0.001f)
                return;

            var yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
            turret.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private bool HasLocalInputAuthority()
        {
            return Object != null && (Object.HasInputAuthority ||
                                      Runner != null && Object.InputAuthority == Runner.LocalPlayer);
        }

        private bool IsLocalPlayer()
        {
            if (HasLocalInputAuthority())
                return true;

            var localState = SessionPlayerState.GetLocal();
            return localState != null && localState.PlayerSlot == PlayerSlot;
        }

        public static TankNetworkController GetLocalForRunner(NetworkRunner runner)
        {
            if (runner != null)
            {
                for (var i = 0; i < Instances.Count; i++)
                {
                    var tank = Instances[i];
                    if (tank != null && tank.Object != null && tank.Object.IsValid &&
                        tank.Object.InputAuthority == runner.LocalPlayer)
                    {
                        Local = tank;
                        return tank;
                    }
                }
            }

            var localState = SessionPlayerState.GetLocal();
            if (localState != null)
            {
                var tank = GetBySlot(localState.PlayerSlot);
                if (tank != null)
                {
                    Local = tank;
                    return tank;
                }
            }

            return Local;
        }

        private void SimulateMovement(Vector2 moveInput)
        {
            if (moveInput.sqrMagnitude < 0.0001f)
                return;

            _camera ??= Camera.main;
            var cameraForward = _camera != null ? _camera.transform.forward : Vector3.forward;
            var cameraRight = _camera != null ? _camera.transform.right : Vector3.right;
            cameraForward = ProjectileTrajectory.FlattenDirection(cameraForward);
            cameraRight = ProjectileTrajectory.FlattenDirection(cameraRight);

            var desiredDirection = cameraForward * moveInput.y + cameraRight * moveInput.x;
            if (desiredDirection.sqrMagnitude < 0.0001f)
                return;
            desiredDirection.Normalize();

            var currentForward = ProjectileTrajectory.FlattenDirection(transform.forward);
            var moveSign = Vector3.Dot(currentForward, desiredDirection) >= 0f ? 1f : -1f;
            var targetFacing = desiredDirection * moveSign;
            var targetRotation = Quaternion.LookRotation(targetFacing, Vector3.up);
            var nextRotation = Quaternion.RotateTowards(_body.rotation, targetRotation,
                _balance.tankTurnSpeed * Runner.DeltaTime);
            var nextForward = nextRotation * Vector3.forward;
            var nextPosition = _body.position + nextForward * (moveSign * _balance.tankMoveSpeed * Runner.DeltaTime);
            nextPosition.y = _body.position.y;
            _body.MoveRotation(nextRotation);
            _body.MovePosition(nextPosition);
        }

        public void ApplyDamage(int damage)
        {
            if (!Object.HasStateAuthority || HP <= 0 || MatchController.Instance == null ||
                MatchController.Instance.State != GameRoundState.RoundActive)
                return;
            var maxHp = _balance != null ? _balance.playerMaxHp : 3;
            HP = Mathf.Clamp(HP - Mathf.Max(0, damage), 0, maxHp);
        }

        public void AddScore()
        {
            if (Object.HasStateAuthority)
                Score++;
        }

        public void ResetForRound(Vector3 position, Quaternion rotation)
        {
            if (!Object.HasStateAuthority)
                return;
            HP = _balance.playerMaxHp;
            FireCooldown = TickTimer.None;
            AimDirection = rotation * Vector3.forward;
            PreviousButtons = default;
            _body.position = position;
            _body.rotation = rotation;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _networkTransform.Teleport(position, rotation);
        }

        public static TankNetworkController GetBySlot(int slot)
        {
            for (var i = 0; i < Instances.Count; i++)
            {
                var tank = Instances[i];
                if (tank != null && tank.Object != null && tank.Object.IsValid && tank.PlayerSlot == slot)
                    return tank;
            }
            return null;
        }
    }
}
