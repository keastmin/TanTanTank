using UnityEngine;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    public sealed class TankPreviewVisual : MonoBehaviour
    {
        [SerializeField] private int playerSlot;
        [SerializeField] private TankAppearance appearance;
        [SerializeField] private float rotationSpeed = 25f;

        public void Configure(int slot, TankAppearance targetAppearance)
        {
            playerSlot = slot;
            appearance = targetAppearance;
        }

        private void Update()
        {
            transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.World);
            var player = SessionPlayerState.GetBySlot(playerSlot);
            if (player != null)
            {
                var session = NetworkSessionController.Instance;
                var colorId = player.Object != null && player.Object.HasInputAuthority && session != null
                    ? session.LocalColorId
                    : player.ColorId;
                appearance?.ApplyColor(colorId);
            }
            else if (playerSlot == 0 && NetworkSessionController.Instance != null)
                appearance?.ApplyColor(NetworkSessionController.Instance.LocalColorId);
        }
    }
}
