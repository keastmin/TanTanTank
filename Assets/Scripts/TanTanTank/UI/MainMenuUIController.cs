using System;
using System.Text;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    public sealed class MainMenuUIController : MonoBehaviour
    {
        private Transform _mainUI;
        private Transform _joinUI;
        private Transform _lobbyUI;
        private TMP_InputField _nicknameInput;
        private TMP_InputField _roomCodeInput;
        private Button _createButton;
        private Button _openJoinButton;
        private Button _quitButton;
        private Button _joinButton;
        private Button _joinPrevButton;
        private Button _lobbyPrevButton;
        private Button _startButton;
        private TMP_Text _roomCodeText;
        private Transform _playerOneSlot;
        private Transform _playerTwoSlot;
        private GameObject _noticePrefab;
        private NetworkSessionController _session;
        private bool _editingText;
        private bool _leavingLobby;

        private void Awake()
        {
            _session = NetworkSessionController.EnsureExists();
            _noticePrefab = Resources.Load<GameObject>("TanTanTank/UI/Notice Window");

            _mainUI = transform.FindDirectChild("Main UI");
            _joinUI = transform.FindDirectChild("Join Room UI");
            _lobbyUI = transform.FindDirectChild("Lobby UI");

            _nicknameInput = _mainUI.FindDeepComponent<TMP_InputField>("Nickname Input Field");
            _createButton = _mainUI.FindDeepComponent<Button>("Create Room Button");
            _openJoinButton = _mainUI.FindDeepComponent<Button>("Join Room Button");
            _quitButton = _mainUI.FindDeepComponent<Button>("Quit Button");

            _roomCodeInput = _joinUI.FindDeepComponent<TMP_InputField>("Room Code Input Field");
            _joinButton = _joinUI.FindDeepComponent<Button>("Join Button");
            _joinPrevButton = _joinUI.FindDeepComponent<Button>("Prev Button");

            _startButton = _lobbyUI.FindDeepComponent<Button>("Start Button");
            _lobbyPrevButton = _lobbyUI.FindDeepComponent<Button>("Prev Button");
            _roomCodeText = _lobbyUI.FindDeepComponent<TMP_Text>("Room Code Text");
            _playerOneSlot = _lobbyUI.FindDeepChild("Player1 Slot");
            _playerTwoSlot = _lobbyUI.FindDeepChild("Player2 Slot");

            var colorRoot = _lobbyUI.FindDeepChild("Color Select Buttons");
            if (colorRoot != null)
            {
                for (var i = 0; i < colorRoot.childCount; i++)
                {
                    var colorButton = colorRoot.GetChild(i).GetComponent<Button>();
                    if (colorButton == null)
                        continue;
                    var handler = colorButton.GetComponent<LobbyColorButton>() ??
                                  colorButton.gameObject.AddComponent<LobbyColorButton>();
                    handler.Configure(i);
                }
            }

            _nicknameInput?.onValueChanged.AddListener(SanitizeNickname);
            _roomCodeInput?.onValueChanged.AddListener(SanitizeRoomCode);
            _createButton?.onClick.AddListener(CreateRoom);
            _openJoinButton?.onClick.AddListener(() => SetPanel(_joinUI));
            _quitButton?.onClick.AddListener(Quit);
            _joinButton?.onClick.AddListener(JoinRoom);
            _joinPrevButton?.onClick.AddListener(() => SetPanel(_mainUI));
            _lobbyPrevButton?.onClick.AddListener(LeaveLobby);
            _startButton?.onClick.AddListener(() => _session.StartMatch());
            SetPanel(_mainUI);
        }

        private void OnEnable()
        {
            NetworkSessionController.SessionChanged += Refresh;
            NetworkSessionController.NoticeRequested += ShowNotice;
        }

        private void OnDisable()
        {
            NetworkSessionController.SessionChanged -= Refresh;
            NetworkSessionController.NoticeRequested -= ShowNotice;
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_session == null)
                return;

            var connected = _session.Runner != null && _session.Runner.IsRunning;
            if (!connected)
                _leavingLobby = false;
            if (connected && !_leavingLobby && _lobbyUI != null && !_lobbyUI.gameObject.activeSelf)
                SetPanel(_lobbyUI);

            if (_createButton != null)
                _createButton.interactable = !_session.IsBusy && !connected;
            if (_openJoinButton != null)
                _openJoinButton.interactable = !_session.IsBusy && !connected;
            if (_joinButton != null)
                _joinButton.interactable = !_session.IsBusy && !connected && _roomCodeInput != null &&
                                             _roomCodeInput.text.Length == 6;

            var playerOne = SessionPlayerState.GetBySlot(0);
            var playerTwo = SessionPlayerState.GetBySlot(1);
            if (_roomCodeText != null)
                _roomCodeText.text = connected ? _session.RoomCode : string.Empty;
            SetSlot(_playerOneSlot, playerOne, false);
            SetSlot(_playerTwoSlot, playerTwo, true);

            if (_startButton != null)
            {
                var isHost = connected && _session.Runner.IsServer;
                _startButton.gameObject.SetActive(isHost);
                _startButton.interactable = isHost && playerOne != null && playerTwo != null;
            }
        }

        private static void SetSlot(Transform slot, SessionPlayerState player, bool hidePreviewWhenEmpty)
        {
            if (slot == null)
                return;
            var nicknameRoot = slot.FindDeepChild("Nickname");
            var nicknameText = nicknameRoot != null ? nicknameRoot.GetComponentInChildren<TMP_Text>(true) : null;
            if (nicknameText != null)
                nicknameText.text = player != null ? player.Nickname.ToString() : "None";
            var preview = slot.FindDeepChild("Player Tank View Section");
            if (preview != null && hidePreviewWhenEmpty)
                preview.gameObject.SetActive(player != null);
        }

        private void CreateRoom()
        {
            var nickname = FinalizeNickname();
            _session.CreateRoom(nickname, _session.LocalColorId);
        }

        private void JoinRoom()
        {
            if (_roomCodeInput == null || _roomCodeInput.text.Length != 6)
                return;
            var nickname = FinalizeNickname();
            _session.JoinRoom(_roomCodeInput.text, nickname, _session.LocalColorId);
        }

        private void LeaveLobby()
        {
            if (_session == null || _leavingLobby)
                return;

            _leavingLobby = true;
            SetPanel(_mainUI);
            _session.LeaveSession();
        }

        private string FinalizeNickname()
        {
            var value = _nicknameInput != null ? _nicknameInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                value = NetworkSessionController.GenerateNickname();
                if (_nicknameInput != null)
                    _nicknameInput.SetTextWithoutNotify(value);
            }
            return value;
        }

        private void SanitizeNickname(string value)
        {
            if (_editingText || _nicknameInput == null)
                return;
            var sanitized = Filter(value, true, 8);
            if (sanitized == value)
                return;
            _editingText = true;
            _nicknameInput.SetTextWithoutNotify(sanitized);
            _editingText = false;
        }

        private void SanitizeRoomCode(string value)
        {
            if (_editingText || _roomCodeInput == null)
                return;
            var sanitized = Filter(value.ToUpperInvariant(), false, 6);
            if (sanitized == value)
                return;
            _editingText = true;
            _roomCodeInput.SetTextWithoutNotify(sanitized);
            _editingText = false;
            Refresh();
        }

        private static string Filter(string value, bool allowDigits, int maxLength)
        {
            var builder = new StringBuilder(maxLength);
            for (var i = 0; i < value.Length && builder.Length < maxLength; i++)
            {
                var character = value[i];
                if ((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
                    (allowDigits && character >= '0' && character <= '9'))
                    builder.Append(character);
            }
            return builder.ToString();
        }

        private void SetPanel(Transform active)
        {
            if (_mainUI != null)
                _mainUI.gameObject.SetActive(active == _mainUI);
            if (_joinUI != null)
                _joinUI.gameObject.SetActive(active == _joinUI);
            if (_lobbyUI != null)
                _lobbyUI.gameObject.SetActive(active == _lobbyUI);
        }

        private void ShowNotice(string message)
        {
            if (_noticePrefab == null || GetComponentInChildren<NoticeWindowController>(true) != null)
                return;
            var notice = Instantiate(_noticePrefab, transform);
            var controller = notice.GetComponent<NoticeWindowController>() ?? notice.AddComponent<NoticeWindowController>();
            controller.Show(message);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
