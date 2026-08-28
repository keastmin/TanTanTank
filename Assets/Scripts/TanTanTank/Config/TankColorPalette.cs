using UnityEngine;

namespace TanTanTank
{
    [CreateAssetMenu(menuName = "TanTanTank/Tank Color Palette", fileName = "Tank Color Palette")]
    public sealed class TankColorPalette : ScriptableObject
    {
        public Material[] colors;

        public Material Get(int colorId)
        {
            if (colors == null || colors.Length == 0)
                return null;

            return colors[Mathf.Clamp(colorId, 0, colors.Length - 1)];
        }
    }
}
