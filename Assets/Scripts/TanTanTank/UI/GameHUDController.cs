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
        private Transform _myInfo;
        private Transform _enemyInfo;
        private TMP_Text _nextResultText;
        private TMP_Text _matchResultText;
        private Slider _myHp;
        private Slider _enemyHp;
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
            _myInfo = _gameUI.FindDeepChild("My Info UI");
            _enemyInfo = _gameUI.FindDeepChild("Enemy Info UI");
            var myHpRoot = _myInfo.FindDeepChild("HP");
            var enemyHpRoot = _enemyInfo.FindDeepChild("HP");
            _myHp = myHpRoot != null ? myHpRoot.GetComponentInChildren<Slider>(true) : null;
            _enemyHp = enemyHpRoot != null ? enemyHpRoot.GetComponentInChildren<Slider>(true) : null;
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
            UpdateInfo(_myInfo, _myHp, local);
            UpdateInfo(_enemyInfo, _enemyHp, enemy);

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

        private static void UpdateInfo(Transform root, Slider hpSlider, TankNetworkController tank)
        {
            if (root == null || tank == null)
                return;
            var nicknameRoot = root.FindDeepChild("Nickname");
            var nicknameText = nicknameRoot != null ? nicknameRoot.GetComponentInChildren<TMP_Text>(true) : null;
            if (nicknameText != null)
                nicknameText.text = tank.Nickname.ToString();
            var scoreRoot = root.FindDeepChild("Score");
            var scoreText = scoreRoot != null ? scoreRoot.GetComponentInChildren<TMP_Text>(true) : null;
            if (scoreText != null)
                scoreText.text = tank.Score.ToString();
            if (hpSlider != null)
            {
                hpSlider.maxValue = 3f;
                hpSlider.value = tank.HP;
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
