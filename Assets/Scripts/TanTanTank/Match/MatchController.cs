using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class MatchController : NetworkBehaviour
    {
        public static MatchController Instance { get; private set; }

        [Networked] public GameRoundState State { get; private set; }
        [Networked] public int CurrentWorldIndex { get; private set; }
        [Networked] public int RoundWinnerSlot { get; private set; }
        [Networked] public NetworkBool PlayerOneVote { get; private set; }
        [Networked] public NetworkBool PlayerTwoVote { get; private set; }
        [Networked] private TickTimer RoundResolutionTimer { get; set; }

        private GameBalanceConfig _balance;
        private NetworkObject _tankPrefab;
        private NetworkObject _projectilePrefab;
        private NetworkObject[] _worldPrefabs;
        private NetworkObject _currentWorld;
        private bool _roundResultCommitted;

        private void Awake()
        {
            _balance = Resources.Load<GameBalanceConfig>("TanTanTank/Game Balance");
            _tankPrefab = Resources.Load<NetworkObject>("TanTanTank/Network/Tank");
            _projectilePrefab = Resources.Load<NetworkObject>("TanTanTank/Network/Projectile");
            _worldPrefabs = new[]
            {
                Resources.Load<NetworkObject>("TanTanTank/Network/World 1"),
                Resources.Load<NetworkObject>("TanTanTank/Network/World 2")
            };
        }

        public override void Spawned()
        {
            Instance = this;
            if (Object.HasStateAuthority)
            {
                State = GameRoundState.Loading;
                CurrentWorldIndex = -1;
                RoundWinnerSlot = -2;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
                Instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority)
                return;

            switch (State)
            {
                case GameRoundState.Loading:
                    TryInitializeMatch();
                    break;
                case GameRoundState.RoundActive:
                    EvaluateRoundEnd();
                    break;
                case GameRoundState.RoundResult:
                    if (PlayerOneVote && PlayerTwoVote)
                        BeginNextRound();
                    break;
            }
        }

        private void TryInitializeMatch()
        {
            var playerOne = SessionPlayerState.GetBySlot(0);
            var playerTwo = SessionPlayerState.GetBySlot(1);
            if (playerOne == null || playerTwo == null || playerOne.Nickname.Length == 0 || playerTwo.Nickname.Length == 0 ||
                _tankPrefab == null || _worldPrefabs[0] == null || _worldPrefabs[1] == null)
                return;

            CurrentWorldIndex = Random.Range(0, _worldPrefabs.Length);
            var spawnPoints = SpawnWorldAndGetPoints(CurrentWorldIndex);
            if (spawnPoints.Count < 2)
                return;

            PickTwoDifferent(spawnPoints.Count, out var firstPoint, out var secondPoint);
            SpawnTank(playerOne, spawnPoints[firstPoint]);
            SpawnTank(playerTwo, spawnPoints[secondPoint]);
            PlayerOneVote = false;
            PlayerTwoVote = false;
            RoundWinnerSlot = -2;
            _roundResultCommitted = false;
            RoundResolutionTimer = TickTimer.None;
            State = GameRoundState.RoundActive;
        }

        private void SpawnTank(SessionPlayerState player, Transform spawnPoint)
        {
            Runner.Spawn(_tankPrefab, spawnPoint.position, spawnPoint.rotation, player.Player,
                (_, spawnedObject) => spawnedObject.GetComponent<TankNetworkController>()
                    .Initialize(player.PlayerSlot, player.ColorId, player.Nickname.ToString()));
        }

        public void TryFire(TankNetworkController owner, Vector3 position, Vector3 direction)
        {
            if (!Object.HasStateAuthority || State != GameRoundState.RoundActive || owner == null || owner.HP <= 0 ||
                _projectilePrefab == null)
                return;

            direction = ProjectileTrajectory.FlattenDirection(direction);
            position.y = owner.FirePosition.y;
            Runner.Spawn(_projectilePrefab, position, Quaternion.LookRotation(direction, Vector3.up), PlayerRef.None,
                (_, spawnedObject) => spawnedObject.GetComponent<NetworkProjectile>()
                    .Initialize(owner.Object.InputAuthority, direction, position.y));
        }

        private void EvaluateRoundEnd()
        {
            var playerOne = TankNetworkController.GetBySlot(0);
            var playerTwo = TankNetworkController.GetBySlot(1);
            if (playerOne == null || playerTwo == null || _roundResultCommitted)
                return;

            if (playerOne.HP > 0 && playerTwo.HP > 0)
            {
                RoundResolutionTimer = TickTimer.None;
                return;
            }

            // Wait one complete simulation tick so every projectile from the death tick
            // can apply damage before deciding whether the round is a draw.
            if (!RoundResolutionTimer.IsRunning)
            {
                RoundResolutionTimer = TickTimer.CreateFromTicks(Runner, 1);
                return;
            }
            if (!RoundResolutionTimer.Expired(Runner))
                return;

            _roundResultCommitted = true;
            if (playerOne.HP <= 0 && playerTwo.HP <= 0)
            {
                RoundWinnerSlot = -1;
            }
            else
            {
                var winner = playerOne.HP > 0 ? playerOne : playerTwo;
                RoundWinnerSlot = winner.PlayerSlot;
                winner.AddScore();
            }

            DespawnAllProjectiles();
            var winningTank = RoundWinnerSlot >= 0 ? TankNetworkController.GetBySlot(RoundWinnerSlot) : null;
            State = winningTank != null && winningTank.Score >= _balance.matchWinScore
                ? GameRoundState.MatchResult
                : GameRoundState.RoundResult;
        }

        public void RequestNextStageVote()
        {
            if (State != GameRoundState.RoundResult)
                return;

            if (Object.HasStateAuthority)
                SetVote(Runner.LocalPlayer);
            else
                RPC_RequestVote();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestVote(RpcInfo info = default)
        {
            SetVote(info.Source);
        }

        private void SetVote(PlayerRef player)
        {
            if (!Object.HasStateAuthority || State != GameRoundState.RoundResult)
                return;

            var sessionPlayer = FindSessionPlayer(player);
            if (sessionPlayer == null)
                return;
            if (sessionPlayer.PlayerSlot == 0)
                PlayerOneVote = true;
            else if (sessionPlayer.PlayerSlot == 1)
                PlayerTwoVote = true;
        }

        private static SessionPlayerState FindSessionPlayer(PlayerRef player)
        {
            for (var i = 0; i < SessionPlayerState.Instances.Count; i++)
            {
                var state = SessionPlayerState.Instances[i];
                if (state != null && state.Player == player)
                    return state;
            }
            return null;
        }

        private void BeginNextRound()
        {
            State = GameRoundState.ChangingWorld;
            DespawnAllProjectiles();
            if (_currentWorld != null && _currentWorld.IsValid)
                Runner.Despawn(_currentWorld);

            var nextWorld = CurrentWorldIndex;
            if (_worldPrefabs.Length > 1)
            {
                while (nextWorld == CurrentWorldIndex)
                    nextWorld = Random.Range(0, _worldPrefabs.Length);
            }
            CurrentWorldIndex = nextWorld;

            var spawnPoints = SpawnWorldAndGetPoints(CurrentWorldIndex);
            if (spawnPoints.Count < 2)
                return;
            PickTwoDifferent(spawnPoints.Count, out var firstPoint, out var secondPoint);
            TankNetworkController.GetBySlot(0)?.ResetForRound(spawnPoints[firstPoint].position, spawnPoints[firstPoint].rotation);
            TankNetworkController.GetBySlot(1)?.ResetForRound(spawnPoints[secondPoint].position, spawnPoints[secondPoint].rotation);

            PlayerOneVote = false;
            PlayerTwoVote = false;
            RoundWinnerSlot = -2;
            _roundResultCommitted = false;
            RoundResolutionTimer = TickTimer.None;
            State = GameRoundState.RoundActive;
        }

        private List<Transform> SpawnWorldAndGetPoints(int worldIndex)
        {
            var points = new List<Transform>();
            _currentWorld = Runner.Spawn(_worldPrefabs[worldIndex], Vector3.zero, Quaternion.identity);
            var root = _currentWorld.transform.FindDeepChild("Spawn Points");
            if (root == null)
                return points;
            for (var i = 0; i < root.childCount; i++)
                points.Add(root.GetChild(i));
            return points;
        }

        private static void PickTwoDifferent(int count, out int first, out int second)
        {
            first = Random.Range(0, count);
            second = Random.Range(0, count - 1);
            if (second >= first)
                second++;
        }

        private void DespawnAllProjectiles()
        {
            for (var i = NetworkProjectile.Instances.Count - 1; i >= 0; i--)
            {
                var projectile = NetworkProjectile.Instances[i];
                if (projectile != null && projectile.Object != null && projectile.Object.IsValid)
                    Runner.Despawn(projectile.Object);
            }
        }

        public LocalRoundResult GetLocalRoundResult()
        {
            var local = TankNetworkController.Local;
            if (local == null || RoundWinnerSlot == -2)
                return LocalRoundResult.None;
            if (RoundWinnerSlot == -1)
                return LocalRoundResult.Draw;
            return RoundWinnerSlot == local.PlayerSlot ? LocalRoundResult.Win : LocalRoundResult.Lose;
        }
    }
}
