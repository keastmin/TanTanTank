using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    public sealed class GameHUDController : MonoBehaviour
    {
        private Transform _gameUI;
        private Transform _nextStageUI;
        private Transform _resultUI;
        private Transform _systemUI;
        private TankInfoView _myInfo;
        private TankInfoView _enemyInfo;
        private TMP_Text _nextResultText;
        private TMP_Text _matchResultText;
        private Image _voteOne;
        private Image _voteTwo;
        private Button _nextButton;
        private bool _systemOpen;

        private void Awake()
        {
            _gameUI = transform.FindDirectChild("Game UI");
            _nextStageUI = transform.FindDirectChild("Next Stage UI");
            _resultUI = transform.FindDirectChild("Result UI");
            _systemUI = transform.FindDirectChild("System UI");
            _myInfo = new TankInfoView(_gameUI.FindDeepChild("My Info UI"));
            _enemyInfo = new TankInfoView(_gameUI.FindDeepChild("Enemy Info UI"));
            _nextResultText = _nextStageUI.FindDeepComponent<TMP_Text>("Result Text");
            _matchResultText = _resultUI.FindDeepComponent<TMP_Text>("Result Text");
            _voteOne = _nextStageUI.FindDeepComponent<Image>("Vote Image 1");
            _voteTwo = _nextStageUI.FindDeepComponent<Image>("Vote Image 2");
            _nextButton = _nextStageUI.FindDeepComponent<Button>("Next Stage Button");
            _nextButton?.onClick.AddListener(VoteNextStage);
            _resultUI.FindDeepComponent<Button>("Quit Button")?.onClick.AddListener(QuitMatch);
            _systemUI.FindDeepComponent<Button>("Quit Button")?.onClick.AddListener(QuitMatch);
            SetSystemOpen(false);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetSystemOpen(!_systemOpen);

            var match = MatchController.Instance;
            var local = TankNetworkController.Local;
            if (match == null || local == null)
                return;
            var enemy = TankNetworkController.GetBySlot(local.PlayerSlot == 0 ? 1 : 0);
            _myInfo.Refresh(local);
            _enemyInfo.Refresh(enemy);

            var isRoundResult = match.State == GameRoundState.RoundResult;
            var isMatchResult = match.State == GameRoundState.MatchResult;
            if (_nextStageUI != null)
                _nextStageUI.gameObject.SetActive(isRoundResult);
            if (_resultUI != null)
                _resultUI.gameObject.SetActive(isMatchResult);

            var localResult = match.GetLocalRoundResult();
            if (_nextResultText != null)
                _nextResultText.text = ResultText(localResult);
            if (_matchResultText != null && isMatchResult)
                _matchResultText.text = ResultText(localResult);

            if (_voteOne != null)
                _voteOne.color = match.PlayerOneVote ? Color.black : Color.white;
            if (_voteTwo != null)
                _voteTwo.color = match.PlayerTwoVote ? Color.black : Color.white;
            if (_nextButton != null)
            {
                var localVoted = local.PlayerSlot == 0 ? match.PlayerOneVote : match.PlayerTwoVote;
                _nextButton.interactable = isRoundResult && !localVoted;
            }
        }

        private sealed class TankInfoView
        {
            private readonly TMP_Text _nickname;
            private readonly TMP_Text _score;
            private readonly Slider _hp;
            private NetworkString<_16> _lastNickname;
            private int _lastScore = int.MinValue;
            private int _lastHp = int.MinValue;
            private bool _hasNickname;

            public TankInfoView(Transform root)
            {
                var nicknameRoot = root != null ? root.FindDeepChild("Nickname") : null;
                var scoreRoot = root != null ? root.FindDeepChild("Score") : null;
                var hpRoot = root != null ? root.FindDeepChild("HP") : null;
                _nickname = nicknameRoot != null ? nicknameRoot.GetComponentInChildren<TMP_Text>(true) : null;
                _score = scoreRoot != null ? scoreRoot.GetComponentInChildren<TMP_Text>(true) : null;
                _hp = hpRoot != null ? hpRoot.GetComponentInChildren<Slider>(true) : null;
                if (_hp != null)
                    _hp.maxValue = 3f;
            }

            public void Refresh(TankNetworkController tank)
            {
                if (tank == null)
                    return;

                var nickname = tank.Nickname;
                if (!_hasNickname || !nickname.Equals(_lastNickname))
                {
                    if (_nickname != null)
                        _nickname.text = nickname.ToString();
                    _lastNickname = nickname;
                    _hasNickname = true;
                }

                if (tank.Score != _lastScore)
                {
                    if (_score != null)
                        _score.text = tank.Score.ToString();
                    _lastScore = tank.Score;
                }

                if (tank.HP != _lastHp)
                {
                    if (_hp != null)
                        _hp.value = tank.HP;
                    _lastHp = tank.HP;
                }
            }
        }

        private static string ResultText(LocalRoundResult result)
        {
            return result switch
            {
                LocalRoundResult.Win => "WIN",
                LocalRoundResult.Lose => "LOSE",
                LocalRoundResult.Draw => "DRAW",
                _ => string.Empty
            };
        }

        private void VoteNextStage()
        {
            MatchController.Instance?.RequestNextStageVote();
        }

        private static void QuitMatch()
        {
            NetworkSessionController.Instance?.EndMatchForEveryone();
        }

        private void SetSystemOpen(bool open)
        {
            _systemOpen = open;
            if (_systemUI != null)
                _systemUI.gameObject.SetActive(open);
        }
    }
}
