using Fusion;
using UnityEngine;

namespace TanTanTank
{
    public enum GameRoundState : byte
    {
        Loading,
        RoundActive,
        RoundResult,
        ChangingWorld,
        MatchResult,
        Terminating
    }

    public enum LocalRoundResult : byte
    {
        None,
        Win,
        Lose,
        Draw
    }

    public enum TankInputButton : byte
    {
        Fire
    }

    public struct TankNetworkInput : INetworkInput
    {
        // The input owner converts WASD to a world-space direction with its own
        // camera. State authority must never reinterpret input with Camera.main.
        public Vector3 MoveDirection;
        public Vector3 AimDirection;
        public NetworkButtons Buttons;
    }

    public static class ProjectileTrajectory
    {
        public static Vector3 FlattenDirection(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;
        }

        public static Vector3 Reflect(Vector3 incomingDirection, Vector3 hitNormal)
        {
            return FlattenDirection(Vector3.Reflect(FlattenDirection(incomingDirection), hitNormal));
        }
    }
}
