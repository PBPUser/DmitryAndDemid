using System.Numerics;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.PlayerWeapons;

public class AkobPlayerWeapon(Player player) : PlayerWeapon(player)
{
    private static Rectangle PlayerBottomLayerSource = new Rectangle(0, 64, 64, 64);
    private static Rectangle PlayerTopLayerSource = new Rectangle(64, 64, 64, 64);
    private static Rectangle AkobRectangleSource = new Rectangle(128, 64, 16, 16);
    
    public Vector2[] BulletSourcePositions = new Vector2[4];
    private int BulletSourcePositionsCount = 0;

    public override void Update()
    {
        float time = player.GameBox.CurrentTick / 60f;
        UpdateBulletSourcePositions(time);
    }

    public override void UpdatePower()
    {
        float time = player.GameBox.CurrentTick / 60f;
        BulletSourcePositionsCount = (player.Power / 100);
        UpdateBulletSourcePositions(time);
    }

    void UpdateBulletSourcePositions(float time)
    {
        float dif = Player.DefocusedDifference + (Player.FocusedDifference - Player.DefocusedDifference) * (float)Helper.ComputeObjectTime(Raylib.GetTime(), FocusTimestamp, Player.FocusAnimationChangingLength,
            DefocusTimestamp + Player.FocusAnimationChangingLength, Player.FocusAnimationChangingLength);
        float angleStart = time * 2;
        float angleDif = MathF.PI * 2 / BulletSourcePositionsCount;
        for (int i = 0; i < BulletSourcePositionsCount; i++)
            BulletSourcePositions[i] = new Vector2(Player.X, Player.Y) + Helper.GetDirection(angleStart + (angleDif * i)) * dif;
    }

    public override void DrawBottomLayer()
    {
        float time = (float)Raylib.GetTime();
        byte transparency = Helper.TimeToTransparency(.5 *
                                                      Helper.ComputeObjectTime(time, FocusTimestamp, Player.FocusAnimationChangingLength,
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
        byte transparency = Helper.TimeToTransparency(
            Helper.ComputeObjectTime(Raylib.GetTime(), FocusTimestamp, Player.FocusAnimationChangingLength,
                DefocusTimestamp + Player.FocusAnimationChangingLength, Player.FocusAnimationChangingLength));
        Raylib.DrawTexturePro(player.SourceTexture, PlayerTopLayerSource, new Rectangle(new Vector2(Player.X, Player.Y), new Vector2(64)), 
            new Vector2(32), 0, Color.White with {A=transparency} );
    }
    
    public override void Shoot()
    {
        if (Player.GameBox.CurrentTick % 20 != 0)
            return;
        Bullet b;
        float totalDamage = Player.Power / 100f;
        float singleDamage = totalDamage / BulletSourcePositionsCount;
        for (int i = 0; i < BulletSourcePositionsCount; i++)
        {
            //b = new Bullet(Player.GameBox, new BulletSpawnInfo()
            //{
            //    Damage = singleDamage,
            //    Speed = 6f,
            //    BulletVisual = "akob",
            //    Rotation = MathF.PI,
            //    Position = BulletSourcePositions[i],
            //    BulletActionClass = "MoveByDirection",
            //    Args = ["UseRotation"]
            //},0, false);
            //b.PlayerShoot = true;
            //Player.GameBox.AddObject(b);
        }
    }
}