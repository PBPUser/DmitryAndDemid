using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Utils;
using Raylib_cs;

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
    
    private static Rectangle PlayerBottomLayerSource = new Rectangle(0, 64, 64, 64);
    private static Rectangle PlayerTopLayerSource = new Rectangle(64, 64, 64, 64);
    private static Rectangle AkobRectangleSource = new Rectangle(128, 64, 16, 16);
    
    public Vector2[] BulletSourcePositions = new Vector2[4];
    private int BulletSourcePositionsCount = 0;

    public override void Update()
    {
        UpdateBulletSourcePositions(player.GameBox.CurrentTick);
    }

    public override void UpdatePower()
    {
        BulletSourcePositionsCount = (player.Power / 100);
        UpdateBulletSourcePositions(player.GameBox.CurrentTick);
    }

    void UpdateBulletSourcePositions(int time)
    {
        float dif = Player.DefocusedDifference + (Player.FocusedDifference - Player.DefocusedDifference) * Helper.ComputeObjectTime(time, FocusTimestamp, Player.FocusAnimationChangingLength,
            (DefocusTimestamp + Player.FocusAnimationChangingLength), Player.FocusAnimationChangingLength);
        if (Player.IsFocused)
        {
            
        }
        float angleStart = time * 2;
        angleStart /= GameBox.TargetTPS;
        float angleDif = MathF.PI * 2 / BulletSourcePositionsCount;
        for (int i = 0; i < BulletSourcePositionsCount; i++)
            BulletSourcePositions[i] = new Vector2(Player.X, Player.Y) + Helper.GetDirection(angleStart + (angleDif * i)) * dif;
    }

    public override void DrawBottomLayer()
    {
        float time = Player.GameBox.CurrentTick;
        byte transparency = Helper.TimeToTransparency(.5 *
                                                      Helper.ComputeObjectTime(Player.GameBox.CurrentTick, FocusTimestamp, Player.FocusAnimationChangingLength,
                                                          DefocusTimestamp + Player.FocusAnimationChangingLength, Player.FocusAnimationChangingLength));
        Raylib.DrawTexturePro(player.SourceTexture, PlayerBottomLayerSource, new Rectangle(new Vector2(Player.X, Player.Y), new Vector2(64)), 
            new Vector2(32), time*64, Color.White with {A=transparency} );
        Raylib.DrawTexturePro(player.SourceTexture, PlayerBottomLayerSource, new Rectangle(new Vector2(Player.X, Player.Y), new Vector2(64)), 
            new Vector2(32), -time*64, Color.White with {A=transparency} );
    }

    public override void DrawTopLayer()
    {
        for(int i = 0; i < BulletSourcePositionsCount; i++)
            Raylib.DrawTexturePro(player.SourceTexture, AkobRectangleSource, new Rectangle(BulletSourcePositions[i],new Vector2(16)), new Vector2(8)
                , 0, Color.White);
        var objTime = Helper.ComputeObjectTime(Player.GameBox.CurrentTick, FocusTimestamp,
            Player.FocusAnimationChangingLength,
            DefocusTimestamp + Player.FocusAnimationChangingLength, Player.FocusAnimationChangingLength);
        byte transparency = Helper.TimeToTransparency(objTime);
        Raylib.DrawTexturePro(player.SourceTexture, PlayerTopLayerSource, new Rectangle(new Vector2(Player.X, Player.Y), new Vector2(64)), 
            new Vector2(32), 0, Color.White with {A=transparency} );
    }

    private int NextBulletPositionIndex = 0;
    
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
        Player.GameBox.Score += 1;
    }
}