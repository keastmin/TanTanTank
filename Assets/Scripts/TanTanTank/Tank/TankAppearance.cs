using System.Collections.Generic;
using UnityEngine;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    public sealed class TankAppearance : MonoBehaviour
    {
        [SerializeField] private Renderer[] colorRenderers;
        private TankColorPalette _palette;
        private int _appliedColor = -1;

        private void Awake()
        {
            _palette = Resources.Load<TankColorPalette>("TanTanTank/Tank Color Palette");
            if (colorRenderers == null || colorRenderers.Length == 0)
                CacheRenderers();
        }

        public void ApplyColor(int colorId)
        {
            if (_appliedColor == colorId)
                return;

            _palette ??= Resources.Load<TankColorPalette>("TanTanTank/Tank Color Palette");
            var material = _palette != null ? _palette.Get(colorId) : null;
            if (material == null)
                return;

            _appliedColor = colorId;
            for (var i = 0; i < colorRenderers.Length; i++)
            {
                if (colorRenderers[i] != null)
                    colorRenderers[i].sharedMaterial = material;
            }
        }

        private void CacheRenderers()
        {
            var result = new List<Renderer>();
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var candidate = renderers[i];
                var current = candidate.transform;
                var isTire = false;
                while (current != null && current != transform)
                {
                    if (current.name.Contains("Tire"))
                    {
                        isTire = true;
                        break;
                    }
                    current = current.parent;
                }

                if (!isTire)
                    result.Add(candidate);
            }
            colorRenderers = result.ToArray();
        }
    }
}
