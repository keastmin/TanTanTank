using UnityEngine;
using UnityEngine.UI;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class LobbyColorButton : MonoBehaviour
    {
        [SerializeField, Min(0)] private int colorId;

        public int ColorId => colorId;

        public void Configure(int id)
        {
            colorId = Mathf.Max(0, id);
            ApplyButtonColor();
        }

        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(SelectColor);
            ApplyButtonColor();
        }

        private void SelectColor()
        {
            NetworkSessionController.EnsureExists().SetLocalColor(colorId);
        }

        private void ApplyButtonColor()
        {
            var palette = Resources.Load<TankColorPalette>("TanTanTank/Tank Color Palette");
            var material = palette != null ? palette.Get(colorId) : null;
            var image = GetComponent<Image>();
            if (image == null || material == null)
                return;

            if (material.HasProperty("_BaseColor"))
                image.color = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color"))
                image.color = material.GetColor("_Color");
        }
    }
}
