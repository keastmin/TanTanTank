using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    public sealed class NoticeWindowController : MonoBehaviour
    {
        private TMP_Text _description;
        private Button _okButton;

        private void Awake()
        {
            _description = transform.FindDeepComponent<TMP_Text>("Description Text");
            _description ??= transform.FindDeepComponent<TMP_Text>("Description");
            _okButton = transform.FindDeepComponent<Button>("OK Button");
            if (_okButton != null)
                _okButton.onClick.AddListener(Close);
        }

        public void Show(string message)
        {
            if (_description != null)
                _description.text = message;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        private void Close()
        {
            Destroy(gameObject);
        }
    }
}
