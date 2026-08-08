using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay.Effects;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.PlayerWeapons;

public class AkobPlayerWeapon(Player player) : PlayerWeapon(player)
{
    static AkobPlayerWeapon()
    {
        BulletFileInfo.Header[0] = 0b_0000_0001;
        BulletFileInfo.Header[0] |= RuntimeObject.FlagUseUpdateScript;
        BulletFileInfo.Header[0] |= RuntimeObject.FlagDangerousRelatedToEnemy;
        BulletFileInfo.FloatingPoints[7] = 8f;
        BulletFileInfo.Visual = "akob";
        BulletFileInfo.UpdateScript = "AkobShoot";
    }
    
    private static FileEntityInfo BulletFileInfo = new FileEntityInfo();
    
    /// <summary>What a bullet caught on the fork is worth, as a multiplier on the height-scaled value every
    /// collected bullet pays (see <see cref="RuntimeObject.UpdateCollectableBullet"/>). One catch = one bullet's
    /// worth; the bomb's own clear leaves the rest of the box at 0, so only what the fork actually swept pays.</summary>
    private const int ForkCatchScoreModifier = 1;

    private static Rect PlayerBottomLayerSource = new Rect(0, 64, 64, 64);
    private static Rect PlayerTopLayerSource = new Rect(64, 64, 64, 64);
    private static Rect AkobRectangleSource = new Rect(128, 64, 16, 16);
    
    public Vector2[] BulletSourcePositions = new Vector2[4];
    private int BulletSourcePositionsCount = 0;

    public override void Update()
    {
        UpdateBulletSourcePositions(player.GameBox.CurrentTick);
        if(IsBombActive)
            UpdateBomb();
    }

    public override void UpdatePower()
    {
        BulletSourcePositionsCount = (player.Power / 100);
        UpdateBulletSourcePositions(player.GameBox.CurrentTick);
    }

    void UpdateBomb()
    {
        var tick = Player.GameBox.CurrentTickWithOffset - BombActivationTick;
        if (tick > 240)
        {
            Player.CollisionEnabled = true;
            IsBombActive = false;
            return;
        }
        float forkAngle = Player.GameBox.CurrentTickWithOffset - BombActivationTick;
        float sign = forkAngle / 30 % 2;
        forkAngle /= 30f;
        forkAngle %= 1;
        var t = tick % 30;
        if (t == 15)
        {
            var time = Player.GameBox.GetTime();
            Player.GameBox.AddScreenEffect(new AkobSpellScreenEffect(Player.GameBox, Player.Position, 101, time, time + .6f));
            Player.GameBox.AddScreenEffect(new ShakeScreenEffect(Player.GameBox, 0.1f,  20, 100, 
                time, time+0.3f));
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["akob-bomb"]);
        }
        else if (t == 0)
        {
            // TODO: Play akob fork swap sound
        }
        ForkAngle = -90 + 180 * (1-MathF.Pow(1-forkAngle, 6));
        if (sign > 0.99)
            ForkAngle = -ForkAngle;
        var sR = BombForkTarget with { Position = player.Position };
        foreach (var bObj in Player.GameBox.BoxObjects)
        {
            bool isBullet = (bObj.Header[0] & RuntimeObject.FlagIsBullet) == RuntimeObject.FlagIsBullet;
            // The fork only touches what belongs to the other side, so the player's own shots are skipped —
            // CONTINUE, not break: breaking abandoned the whole sweep at the first player bullet in the list, and
            // the player is firing constantly, so most of the box was never tested at all. The player-shot marker
            // (FlagDangerousRelatedToEnemy) also shares its bit with FlagUseDieScript on entities, hence the
            // IsBullet test alongside it: without it an enemy that has a die script (the mystical toilet) reads
            // as a player shot and the fork passes straight through it.
            if (isBullet && RuntimeObject.FlagDangerousRelatedToEnemy ==
                (bObj.Header[0] & RuntimeObject.FlagDangerousRelatedToEnemy))
                continue;
            if (IsColliding(sR, BombForkOrigin, ForkAngle, bObj.Position, bObj.Collision))
            {
                // An enemy bullet the fork touches is turned into loot: it stops being lethal (the box's damage
                // pass skips a bullet in collectable state), flies to the player like a magneted item, and pays
                // score when it reaches them — Header[5] is the per-bullet score modifier, and a bullet nobody
                // has priced is worth nothing, so it has to be set here for the catch to pay out.
                if (isBullet)
                {
                    bObj.Header[0] |= RuntimeObject.FlagIsCollectableBullet;
                    bObj.Header[2] = 128;   // half-transparent, the way every other converted bullet reads
                    bObj.Header[5] = ForkCatchScoreModifier;
                    bObj.CollectableVelocity = Helper.GetDirection(bObj.Position, Player.Position);
                }
                // Between chapters (the DelayBetweenChapters transition) AND while a dialog is on screen, all
                // gameplay damage is frozen: the box loop skips its damage/health pass on InChapterDelay /
                // IsDialogActive. The bomb deals damage directly here, from the weapon's own update, so without
                // this guard a bomb still burning through its 240-tick life would keep hurting — and could kill —
                // the boss during that transition window or before the fight has started.
                else if (!Player.GameBox.InChapterDelay && !Player.GameBox.IsDialogActive)
                    bObj.Health -= 12f;
            }
        }
    }

    /// <summary>
    /// A bullet (circle) against the swinging fork (a rotated rectangle): undo the fork's rotation about its
    /// pivot to put the bullet in the fork's own space, then clamp to the rectangle and compare the distance.
    /// <paramref name="angleDegrees"/> is the same angle the fork is DRAWN at — DrawTexturePro takes degrees, so
    /// it has to be converted here or the hit region turns about 57x slower than the sprite the player sees.
    /// </summary>
    static bool IsColliding(Rect rect, Vector2 origin, float angleDegrees, Vector2 position, float radius)
    {
        Vector2 diff = position - rect.Position;
        float angle = angleDegrees * MathF.PI / 180f;
        float sin = MathF.Sin(-angle);
        float cos = MathF.Cos(-angle);
        float lX = diff.X * cos - diff.Y * sin;
        float lY = diff.X * sin + diff.Y * cos;   // was diff.Y twice, which sheared the fork's box off its sprite
        Vector2 lCirclePos = new Vector2(lX, lY) + origin;
        float cX = Math.Clamp(lCirclePos.X, 0f, rect.Size.X);
        float cY = Math.Clamp(lCirclePos.Y, 0f, rect.Size.Y);
        return MathF.Pow(lCirclePos.X - cX, 2) + MathF.Pow(lCirclePos.Y - cY, 2) <=
               MathF.Pow(radius, 2);
    }
    
    void UpdateBulletSourcePositions(int time)
    {
        float dif = Player.DefocusedDifference + (Player.FocusedDifference - Player.DefocusedDifference) * Helper.ComputeObjectTime(time, FocusTimestamp, Player.FocusAnimationChangingLength,
            (DefocusTimestamp + Player.FocusAnimationChangingLength), Player.FocusAnimationChangingLength);
        float angleStart = time * 2;
        float angleDif = MathF.PI * 2 / BulletSourcePositionsCount;
        angleStart /= GameBox.TargetTPS;
        for (int i = 0; i < BulletSourcePositionsCount; i++)
            BulletSourcePositions[i] = new Vector2(Player.X, Player.Y) + Helper.GetDirection(angleStart + (angleDif * i)) * dif;
    }

    public override void DrawBottomLayer()
    {
        float time = Player.GameBox.CurrentTick;
        byte transparency = Helper.TimeToTransparency(.5 *
                                                      Helper.ComputeObjectTime(Player.GameBox.CurrentTick, FocusTimestamp, Player.FocusAnimationChangingLength,
                                                          DefocusTimestamp + Player.FocusAnimationChangingLength, Player.FocusAnimationChangingLength));
        DrawTexturePro(player.SourceTexture, PlayerBottomLayerSource, new Rect(new Vector2(Player.X, Player.Y), new Vector2(64)), 
            new Vector2(32), time*64, Rgba.White with {A=transparency} );
        DrawTexturePro(player.SourceTexture, PlayerBottomLayerSource, new Rect(new Vector2(Player.X, Player.Y), new Vector2(64)), 
            new Vector2(32), -time*64, Rgba.White with {A=transparency} );
    }

    public override void DrawTopLayer()
    {
        for(int i = 0; i < BulletSourcePositionsCount; i++)
            DrawTexturePro(player.SourceTexture, AkobRectangleSource, new Rect(BulletSourcePositions[i],new Vector2(16)), new Vector2(8)
                , 0, Rgba.White);
        var objTime = Helper.ComputeObjectTime(Player.GameBox.CurrentTick, FocusTimestamp,
            Player.FocusAnimationChangingLength,
            DefocusTimestamp + Player.FocusAnimationChangingLength, Player.FocusAnimationChangingLength);
        byte transparency = Helper.TimeToTransparency(objTime);
        DrawTexturePro(player.SourceTexture, PlayerTopLayerSource, new Rect(new Vector2(Player.X, Player.Y), new Vector2(64)), 
            new Vector2(32), 0, Rgba.White with {A=transparency} );
        if (IsBombActive)
        {
            DrawTexturePro(Runtime.CurrentRuntime.Textures["vilkaCut.png"],
                BombForkSource, BombForkTarget with { Position = Player.Position }, BombForkOrigin, ForkAngle, Rgba.White);
        }
    }

    private static Rect BombForkSource = new(0, 0, 126, 957);
    private static Rect BombForkTarget = new(0, 0, 32, 239);
    private static Vector2 BombForkOrigin = new Vector2(16, 210);
    private int NextBulletPositionIndex = 0;
    private float ForkAngle = 0;
    
    public override void Shoot()
    {
        if (Player.GameBox.CurrentTick % (20/BulletSourcePositionsCount) != 0)
            return;
        float totalDamage = Player.Power / 100f * 8;
        float singleDamage = totalDamage / BulletSourcePositionsCount;
        
        RuntimeObject reo = RuntimeObject.LoadFromFile(BulletFileInfo, Player.GameBox);
        reo.CreatedAt = Player.GameBox.CurrentTick;
        reo.X = BulletSourcePositions[NextBulletPositionIndex].X;
        reo.Y = BulletSourcePositions[NextBulletPositionIndex].Y;
        reo.FloatingPoints[9] = singleDamage;
        Player.GameBox.AddObject(reo);
        NextBulletPositionIndex = (NextBulletPositionIndex + 1) % BulletSourcePositionsCount;
        //TODO Play shoot sound 
    }

    public override void SpawnDistortionEffect(int x, int y)
    {
        Player.GameBox.AddScreenEffect(new GameplayScreenEffect(Player.GameBox, new Vector2(x,y), 45, "akob_bullet_distortion", Player.GameBox.GetTime(), Player.GameBox.GetTime()+0.25f));
    }

    public override void AddShootTargetScore()
    {
        Player.GameBox.ScoreTarget += 1;
    }
}