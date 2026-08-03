using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.PlayerWeapons;

/// <summary>
/// Qaw cycles through four shot patterns by tapping shoot (Z) while not focused — focus is reserved for
/// precision aiming, so holding Shift+Z keeps firing the current mode without cycling it. The rising edge is
/// read off <see cref="Player.IsShooting"/> itself (already latched per-tick, live or replayed) rather than
/// new input plumbing, so mode switches replay deterministically for free.
/// </summary>
public class QawPlayerWeapon(Player player) : PlayerWeapon(player)
{
    public enum Mode
    {
        RotatingCross = 0,
        PowerSources = 1,
        FixedCross = 2,
        SideAttack = 3,
    }

    static QawPlayerWeapon()
    {
        BulletFileInfo.Header[0] = 0b_0000_0001;
        BulletFileInfo.Header[0] |= RuntimeObject.FlagUseUpdateScript;
        BulletFileInfo.Header[0] |= RuntimeObject.FlagDangerousRelatedToEnemy;
        BulletFileInfo.FloatingPoints[7] = 9f;
        BulletFileInfo.Visual = "akob";
        BulletFileInfo.UpdateScript = "MoveLinearByDirection";
    }

    private static FileEntityInfo BulletFileInfo = new FileEntityInfo();
    private const int MaxSources = 4;

    public Mode CurrentMode = Mode.RotatingCross;
    public Vector2[] BulletSourcePositions = new Vector2[MaxSources];
    private float[] BulletSourceAngles = new float[MaxSources];
    private int SourceCount = 1;
    private int NextBulletPositionIndex = 0;
    private bool WasShootingLastTick = false;

    public override void Update()
    {
        bool isShooting = Player.IsShooting;
        if (isShooting && !WasShootingLastTick && !Player.IsFocused)
            CycleMode();
        WasShootingLastTick = isShooting;

        UpdateBulletSourcePositions(Player.GameBox.CurrentTick);
    }

    void CycleMode()
    {
        CurrentMode = (Mode)(((int)CurrentMode + 1) % 4);
        NextBulletPositionIndex = 0;
        UpdatePower();
    }

    public override void UpdatePower()
    {
        SourceCount = CurrentMode switch
        {
            Mode.PowerSources => Math.Clamp(Player.Power / 100, 1, 4),
            Mode.SideAttack => 2,
            _ => 4, // RotatingCross / FixedCross: always a full four-point cross
        };
        UpdateBulletSourcePositions(Player.GameBox.CurrentTick);
    }

    void UpdateBulletSourcePositions(int time)
    {
        float dif = Player.DefocusedDifference + (Player.FocusedDifference - Player.DefocusedDifference) *
            Helper.ComputeObjectTime(time, FocusTimestamp, Player.FocusAnimationChangingLength,
                DefocusTimestamp + Player.FocusAnimationChangingLength, Player.FocusAnimationChangingLength);
        Vector2 center = new Vector2(Player.X, Player.Y);

        switch (CurrentMode)
        {
            case Mode.RotatingCross:
            {
                float angleStart = time * 2f / GameBox.TargetTPS;
                for (int i = 0; i < SourceCount; i++)
                {
                    float angle = angleStart + i * (MathF.PI * 2f / SourceCount);
                    BulletSourceAngles[i] = angle;
                    BulletSourcePositions[i] = center + Helper.GetDirection(angle) * dif;
                }
                break;
            }
            case Mode.FixedCross:
            {
                const float angleStart = -MathF.PI / 2f; // first arm points up, doesn't spin
                for (int i = 0; i < SourceCount; i++)
                {
                    float angle = angleStart + i * (MathF.PI * 2f / SourceCount);
                    BulletSourceAngles[i] = angle;
                    BulletSourcePositions[i] = center + Helper.GetDirection(angle) * dif;
                }
                break;
            }
            case Mode.PowerSources:
            {
                float spacing = dif / 2f;
                for (int i = 0; i < SourceCount; i++)
                {
                    float offset = (i - (SourceCount - 1) / 2f) * spacing;
                    BulletSourceAngles[i] = -MathF.PI / 2f; // straight up
                    BulletSourcePositions[i] = center + new Vector2(offset, 0);
                }
                break;
            }
            case Mode.SideAttack:
            {
                BulletSourceAngles[0] = MathF.PI; // left
                BulletSourceAngles[1] = 0f;        // right
                BulletSourcePositions[0] = center;
                BulletSourcePositions[1] = center;
                break;
            }
        }
    }

    public override void Shoot()
    {
        int divisor = Math.Max(1, 20 / SourceCount);
        if (Player.GameBox.CurrentTick % divisor != 0)
            return;

        float totalDamage = Player.Power / 100f * 8;
        float singleDamage = totalDamage / SourceCount;

        if (CurrentMode == Mode.SideAttack)
        {
            for (int i = 0; i < SourceCount; i++)
                FireBullet(i, singleDamage);
            return;
        }

        FireBullet(NextBulletPositionIndex, singleDamage);
        NextBulletPositionIndex = (NextBulletPositionIndex + 1) % SourceCount;
    }

    void FireBullet(int sourceIndex, float damage)
    {
        RuntimeObject reo = RuntimeObject.LoadFromFile(BulletFileInfo, Player.GameBox);
        reo.CreatedAt = Player.GameBox.CurrentTick;
        reo.X = BulletSourcePositions[sourceIndex].X;
        reo.Y = BulletSourcePositions[sourceIndex].Y;
        reo.FacingRotation = BulletSourceAngles[sourceIndex];
        reo.FloatingPoints[9] = damage;
        Player.GameBox.AddObject(reo);
    }

    public override void SpawnDistortionEffect(int x, int y)
    {
        Player.GameBox.AddScreenEffect(new GameplayScreenEffect(Player.GameBox, new Vector2(x, y), 45,
            "akob_bullet_distortion", Player.GameBox.GetTime(), Player.GameBox.GetTime() + 0.25f));
    }

    public override void AddShootTargetScore()
    {
        Player.GameBox.ScoreTarget += 1;
    }
}
