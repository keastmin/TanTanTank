using Fusion;
using UnityEngine;
using UnityEngine.UI;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    public sealed class TankWorldSpaceCooldownUI : MonoBehaviour
    {
        [SerializeField] private Image cooldownImage;
        private TankNetworkController _tank;
        private Camera _camera;

        private void Awake()
        {
            _tank = GetComponentInParent<TankNetworkController>();
            cooldownImage ??= transform.FindDeepComponent<Image>("Cooldown Image");
            if (cooldownImage != null)
                cooldownImage.fillAmount = 0f;
            SetVisible(false);
        }

        private void LateUpdate()
        {
            _camera ??= Camera.main;
            if (_camera != null)
                transform.rotation = _camera.transform.rotation;

            if (_tank == null || _tank.Object == null || !_tank.Object.IsValid || _tank.IsFireReady)
            {
                if (cooldownImage != null)
                    cooldownImage.fillAmount = 0f;
                SetVisible(false);
                return;
            }

            SetVisible(true);
            if (cooldownImage != null)
                cooldownImage.fillAmount = _tank.NormalizedCooldown;
        }

        private void SetVisible(bool visible)
        {
            var canvas = GetComponent<Canvas>();
            if (canvas != null)
                canvas.enabled = visible;
            if (cooldownImage != null)
                cooldownImage.enabled = visible;
        }
    }
}
