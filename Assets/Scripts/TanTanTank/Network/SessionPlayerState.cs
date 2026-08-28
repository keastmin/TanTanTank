using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace TanTanTank
{
    [DisallowMultipleComponent]
    public sealed class SessionPlayerState : NetworkBehaviour
    {
        public static readonly List<SessionPlayerState> Instances = new();

        [Networked] public NetworkString<_16> Nickname { get; private set; }
        [Networked] public int ColorId { get; private set; }
        [Networked] public int PlayerSlot { get; set; }

        public PlayerRef Player => Object != null ? Object.InputAuthority : PlayerRef.None;

        public override void Spawned()
        {
            if (!Instances.Contains(this))
                Instances.Add(this);

            if (!Object.HasInputAuthority)
                return;

            var session = NetworkSessionController.Instance;
            var nickname = session != null ? session.LocalNickname : "PLAYER";
            var colorId = session != null ? session.LocalColorId : 0;

            if (Object.HasStateAuthority)
                SetProfile(nickname, colorId);
            else
                RPC_SetProfile(nickname, colorId);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            Instances.Remove(this);
        }

        public void RequestColor(int colorId)
        {
            if (!Object.HasInputAuthority)
                return;

            if (Object.HasStateAuthority)
                SetColor(colorId);
            else
                RPC_SetColor(colorId);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_SetProfile(NetworkString<_16> nickname, int colorId)
        {
            SetProfile(nickname.ToString(), colorId);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_SetColor(int colorId)
        {
            SetColor(colorId);
        }

        private void SetProfile(string nickname, int colorId)
        {
            if (!Object.HasStateAuthority)
                return;

            Nickname = string.IsNullOrWhiteSpace(nickname) ? "PLAYER" : nickname;
            SetColor(colorId);
        }

        private void SetColor(int colorId)
        {
            if (!Object.HasStateAuthority)
                return;

            var palette = Resources.Load<TankColorPalette>("TanTanTank/Tank Color Palette");
            var count = palette != null && palette.colors != null ? palette.colors.Length : 1;
            ColorId = Mathf.Clamp(colorId, 0, Mathf.Max(0, count - 1));
        }

        public static SessionPlayerState GetBySlot(int slot)
        {
            for (var i = 0; i < Instances.Count; i++)
            {
                var state = Instances[i];
                if (state != null && state.Object != null && state.Object.IsValid && state.PlayerSlot == slot)
                    return state;
            }

            return null;
        }

        public static SessionPlayerState GetLocal()
        {
            var runner = NetworkSessionController.Instance != null ? NetworkSessionController.Instance.Runner : null;
            for (var i = 0; i < Instances.Count; i++)
            {
                var state = Instances[i];
                if (state != null && state.Object != null && state.Object.IsValid &&
                    (state.Object.HasInputAuthority || runner != null && state.Object.InputAuthority == runner.LocalPlayer))
                    return state;
            }

            return null;
        }
    }
}
