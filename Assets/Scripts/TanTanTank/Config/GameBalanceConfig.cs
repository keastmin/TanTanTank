using UnityEngine;

namespace TanTanTank
{
    [CreateAssetMenu(menuName = "TanTanTank/Game Balance", fileName = "Game Balance")]
    public sealed class GameBalanceConfig : ScriptableObject
    {
        [Header("Tank")]
        [Min(1)] public int playerMaxHp = 3;
        [Min(0f)] public float tankMoveSpeed = 5f;
        [Min(0f)] public float tankTurnSpeed = 360f;
        [Min(0f)] public float fireCooldown = 2f;

        [Header("Projectile")]
        [Min(1)] public int projectileDamage = 1;
        [Min(0f)] public float projectileSpeed = 12f;
        [Min(0f)] public float ownerIgnoreTime = 0.2f;
        [Min(1)] public int projectileMaxWallHits = 50;
        [Min(0.0001f)] public float projectileSurfaceEpsilon = 0.01f;
        [Range(1, 32)] public int maxCollisionIterationsPerTick = 8;

        [Header("Aim / Match")]
        [Min(0f)] public float aimPreviewMaxDistance = 30f;
        [Min(1)] public int matchWinScore = 3;
    }
}
