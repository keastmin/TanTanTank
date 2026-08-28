using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    public sealed class NetworkSessionController : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static NetworkSessionController Instance { get; private set; }

        public static event Action SessionChanged;
        public static event Action<string> NoticeRequested;

        public NetworkRunner Runner { get; private set; }
        public string LocalNickname { get; private set; } = "PLAYER";
        public int LocalColorId { get; private set; }
        public string RoomCode { get; private set; } = string.Empty;
        public bool IsBusy { get; private set; }

        private NetworkObject _sessionPlayerPrefab;
        private NetworkObject _matchRuntimePrefab;
        private bool _returningToMain;
        private bool _gameSceneWasActive;

        public static NetworkSessionController EnsureExists()
        {
            if (Instance != null)
                return Instance;

            var root = new GameObject("Network Runtime");
            return root.AddComponent<NetworkSessionController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            _sessionPlayerPrefab = Resources.Load<NetworkObject>("TanTanTank/Network/Session Player State");
            _matchRuntimePrefab = Resources.Load<NetworkObject>("TanTanTank/Network/Match Runtime");
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public async void CreateRoom(string nickname, int colorId)
        {
            if (IsBusy || Runner != null)
                return;

            SetLocalProfile(nickname, colorId);
            IsBusy = true;
            SessionChanged?.Invoke();

            StartGameResult lastResult = default;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                RoomCode = GenerateRoomCode();
                lastResult = await StartRunner(GameMode.Host, RoomCode);
                if (lastResult.Ok)
                {
                    IsBusy = false;
                    SessionChanged?.Invoke();
                    return;
                }

                await DisposeFailedRunner();
            }

            IsBusy = false;
            SessionChanged?.Invoke();
            NoticeRequested?.Invoke($"방을 만들지 못했습니다.\n{lastResult.ShutdownReason}");
        }

        public async void JoinRoom(string roomCode, string nickname, int colorId)
        {
            if (IsBusy || Runner != null)
                return;

            SetLocalProfile(nickname, colorId);
            RoomCode = roomCode != null ? roomCode.Trim().ToUpperInvariant() : string.Empty;
            IsBusy = true;
            SessionChanged?.Invoke();

            var result = await StartRunner(GameMode.Client, RoomCode);
            IsBusy = false;
            SessionChanged?.Invoke();
            if (!result.Ok)
            {
                await DisposeFailedRunner();
                NoticeRequested?.Invoke($"방에 참가하지 못했습니다.\n{result.ShutdownReason}");
            }
        }

        public void SetLocalColor(int colorId)
        {
            LocalColorId = Mathf.Max(0, colorId);
            var localState = SessionPlayerState.GetLocal();
            localState?.RequestColor(LocalColorId);
            SessionChanged?.Invoke();
        }

        public void StartMatch()
        {
            if (Runner == null || !Runner.IsServer || SessionPlayerState.Instances.Count < 2)
                return;

            Runner.LoadScene(SceneRef.FromIndex(1), LoadSceneMode.Single);
        }

        public async void LeaveSession()
        {
            if (_returningToMain)
                return;

            _returningToMain = true;
            if (Runner != null)
                await Runner.Shutdown();

            ReturnToMainScene(false, null);
        }

        public async void EndMatchForEveryone()
        {
            if (_returningToMain)
                return;

            _returningToMain = true;
            if (Runner != null)
                await Runner.Shutdown();

            ReturnToMainScene(false, null);
        }

        private void SetLocalProfile(string nickname, int colorId)
        {
            LocalNickname = string.IsNullOrWhiteSpace(nickname) ? GenerateNickname() : nickname;
            LocalColorId = Mathf.Max(0, colorId);
        }

        private async Task<StartGameResult> StartRunner(GameMode mode, string roomCode)
        {
            var runnerObject = new GameObject("NetworkRunner");
            runnerObject.transform.SetParent(transform, false);
            Runner = runnerObject.AddComponent<NetworkRunner>();
            runnerObject.AddComponent<NetworkSceneManagerDefault>();
            runnerObject.AddComponent<NetworkObjectProviderDefault>();
            Runner.ProvideInput = true;
            Runner.AddCallbacks(this);

            var sceneInfo = new NetworkSceneInfo();
            sceneInfo.AddSceneRef(SceneRef.FromIndex(0), LoadSceneMode.Single);

            return await Runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = roomCode,
                PlayerCount = 2,
                Scene = sceneInfo,
                SceneManager = runnerObject.GetComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = runnerObject.GetComponent<NetworkObjectProviderDefault>()
            });
        }

        private async Task DisposeFailedRunner()
        {
            if (Runner != null)
            {
                Runner.RemoveCallbacks(this);
                if (Runner.IsRunning)
                    await Runner.Shutdown();
                if (Runner != null)
                    Destroy(Runner.gameObject);
            }

            Runner = null;
            SessionPlayerState.Instances.Clear();
        }

        private static string GenerateRoomCode()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
            return new string(chars);
        }

        public static string GenerateNickname()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var chars = new char[8];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
            return new string(chars);
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer || _sessionPlayerPrefab == null)
                return;

            var usedSlot = SessionPlayerState.GetBySlot(0) != null ? 1 : 0;
            var playerObject = runner.Spawn(_sessionPlayerPrefab, Vector3.zero, Quaternion.identity, player,
                (_, spawnedObject) => spawnedObject.GetComponent<SessionPlayerState>().PlayerSlot = usedSlot);
            runner.SetPlayerObject(player, playerObject);
            runner.MakeDontDestroyOnLoad(playerObject.gameObject);
            SessionChanged?.Invoke();
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (_gameSceneWasActive)
            {
                EndMatchForEveryone();
                return;
            }

            if (runner.IsServer && runner.TryGetPlayerObject(player, out var playerObject))
                runner.Despawn(playerObject);
            SessionChanged?.Invoke();
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var data = new TankNetworkInput();
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                data.MoveInput = new Vector2(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
                if (data.MoveInput.sqrMagnitude > 1f)
                    data.MoveInput.Normalize();
            }

            var mouse = Mouse.current;
            data.Buttons.Set((int)TankInputButton.Fire, mouse != null && mouse.leftButton.isPressed);

            var camera = Camera.main;
            if (camera != null && mouse != null)
            {
                var localTank = TankNetworkController.GetLocalForRunner(runner);
                if (localTank != null)
                {
                    localTank.UpdateLocalAim(camera, mouse.position.ReadValue());
                    data.AimDirection = localTank.FireDirection;
                }
            }

            input.Set(data);
        }

        public void OnSceneLoadStart(NetworkRunner runner)
        {
            SessionChanged?.Invoke();
        }

        public void OnSceneLoadDone(NetworkRunner runner)
        {
            _gameSceneWasActive = SceneManager.GetActiveScene().buildIndex == 1;
            if (_gameSceneWasActive && runner.IsServer && _matchRuntimePrefab != null && MatchController.Instance == null)
                runner.Spawn(_matchRuntimePrefab);
            SessionChanged?.Invoke();
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            if (_returningToMain)
                return;

            _returningToMain = true;
            var showNotice = shutdownReason != ShutdownReason.Ok && shutdownReason != ShutdownReason.GameClosed;
            ReturnToMainScene(showNotice, showNotice ? $"네트워크 연결이 종료되었습니다.\n{shutdownReason}" : null);
        }

        private void ReturnToMainScene(bool showNotice, string message)
        {
            SessionPlayerState.Instances.Clear();
            Runner = null;
            RoomCode = string.Empty;
            var shouldLoad = SceneManager.GetActiveScene().buildIndex != 0;
            _returningToMain = false;
            _gameSceneWasActive = false;

            if (shouldLoad)
                SceneManager.LoadScene(0);
            if (showNotice)
                NoticeRequested?.Invoke(message);
            SessionChanged?.Invoke();
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
