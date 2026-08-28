using System.Collections.Generic;
using UnityEngine;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    public sealed class TankHurtbox : MonoBehaviour
    {
        public static readonly List<TankHurtbox> Instances = new();

        public TankNetworkController Owner { get; private set; }
        public Collider HitCollider { get; private set; }

        private void Awake()
        {
            Owner = GetComponentInParent<TankNetworkController>();
            var hurtboxLayer = LayerMask.NameToLayer("Hurtbox");
            if (hurtboxLayer >= 0)
                gameObject.layer = hurtboxLayer;

            HitCollider = GetComponent<Collider>();
            if (HitCollider != null)
                HitCollider.isTrigger = true;
        }

        private void OnEnable()
        {
            if (!Instances.Contains(this))
                Instances.Add(this);
        }

        private void OnDisable()
        {
            Instances.Remove(this);
        }
    }
}
