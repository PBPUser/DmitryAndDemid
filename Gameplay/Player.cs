using System.Numerics;
using System.Reflection;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Player : RuntimeObject
{
    Action<Player, bool> ShootAction;
    Action<Player, bool> BombAction;
    public ProtogonistData ProtogonistData;

    public Player(Game game, ProtogonistData data) : base(game, new Vector2(192, 400), new Vector2(32, 32), new Vector2(8), 0)
    {
        ClearProtected = true;
        ProtogonistData = data;
        CollisionEnabled = false;
        if (Runtime.CurrentRuntime.Textures.ContainsKey(data.Sprite))
            SourceTexture = Runtime.CurrentRuntime.Textures[data.Sprite];
        else
            Console.WriteLine($"Player sprite ({data.Sprite}) not found!");
        SourceRect = new Rectangle(0, 0, 32, 32);
        // FieldInfo? field = typeof(ShootBombScripts).GetField(data.WeaponScriptName);
        // if (field == null)
        //     throw new Exception();
        // ShootAction = (Action<Player, bool>)field.GetRawConstantValue()!;
        // field = typeof(ShootBombScripts).GetField(data.BombScriptName);
        // if (field == null)
        //     throw new Exception();
        // BombAction = (Action<Player, bool>)field.GetRawConstantValue()!;
        Speed = data.Speed;
        FocusSpeed = data.FocusSpeed;
    }

    private int Speed = 0;
    private int FocusSpeed = 0;
    
    public override void Update()
    {
        if (RestoreTick > Game.CurrentTick)
        {
            int j = Game.CurrentTick - RestoreTick + RestoreInvincibilityLength;
            if (j < RestoreAnimationLength)
            {
                UpdateCollisionRender(new Vector2(192, 400) + new Vector2(0, 128) * (1-((float)j/(float)RestoreInvincibilityLength)), 0);
                return;
            }
        }
        else
        {
            CollisionDotPos = PositionTo;
            CollisionEnabled = true;
        }
        int speed = Raylib.IsKeyDown(KeyboardKey.LeftShift) ? FocusSpeed : Speed;
        Vector2 PositionChange = Vector2.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.Left))
            PositionChange.X -= speed;
        if (Raylib.IsKeyDown(KeyboardKey.Right))
            PositionChange.X += speed;
        if (Raylib.IsKeyDown(KeyboardKey.Up))
            PositionChange.Y -= speed;
        if (Raylib.IsKeyDown(KeyboardKey.Down))
            PositionChange.Y += speed;
        IsFocused = Raylib.IsKeyDown(KeyboardKey.LeftShift);
        UpdateCollisionRender(PositionTo + PositionChange, 0);
    }

    private bool IsFocused
    {
        get => isFocused;
        set
        {
            if (isFocused == value)
                return;
            isFocused = value;
            if (value)
            {
                FocusTimestamp = (float)Raylib.GetTime() -
                                 MathF.Max(FocusAnimationChangingLength + DefocusTimestamp - (float)Raylib.GetTime(),
                                     0);
                DefocusTimestamp = float.MaxValue;
            }
            else
            {
                DefocusTimestamp = (float)Raylib.GetTime() -
                                   MathF.Max(FocusAnimationChangingLength + FocusTimestamp - (float)Raylib.GetTime(),
                                       0);
            }
        }
    }

    private bool isFocused = false;
    
    private float FocusTimestamp = 0;
    private float DefocusTimestamp = 0;
    private const float FocusAnimationChangingLength = 0.25f;
    
    private Vector2 CollisionDotPos;
    private static Rectangle PlayerBottomLayerSource = new Rectangle(0, 64, 64, 64);
    private static Rectangle PlayerTopLayerSource = new Rectangle(64, 64, 64, 64);
    
    public void RenderBottomLayer()
    {
        float time = (float)Raylib.GetTime();
        byte transparency = Helper.TimeToTransparency(.5 *
            Helper.ComputeObjectTime(time, FocusTimestamp, FocusAnimationChangingLength,
                DefocusTimestamp + FocusAnimationChangingLength, FocusAnimationChangingLength));
        Raylib.DrawTexturePro(SourceTexture, PlayerBottomLayerSource, new Rectangle(PositionTo, new Vector2(64)), 
            new Vector2(32), time*64, Color.White with {A=transparency} );
        Raylib.DrawTexturePro(SourceTexture, PlayerBottomLayerSource, new Rectangle(PositionTo, new Vector2(64)), 
            new Vector2(32), -time*64, Color.White with {A=transparency} );
        Raylib.DrawText($"Transparency: {transparency}", 0, 64, 32, Color.White);
    }

    public void RenderTopLayer()
    {
        byte transparency = Helper.TimeToTransparency(
            Helper.ComputeObjectTime(Raylib.GetTime(), FocusTimestamp, FocusAnimationChangingLength,
                DefocusTimestamp + FocusAnimationChangingLength, FocusAnimationChangingLength));
        Raylib.DrawTexturePro(SourceTexture, PlayerTopLayerSource, new Rectangle(PositionTo, new Vector2(64)), 
            new Vector2(32), 0, Color.White with {A=transparency} );
        Raylib.DrawText($"Transparency: {transparency}", 0, 96, 32, Color.White);
    }

    private const int RestoreInvincibilityLength = 300;
    private const int RestoreAnimationLength = 60;
    private int RestoreTick = 0;

    public void Die()
    {
        Game.SetDied();
        CollisionEnabled = false;
        RestoreTick = Game.CurrentTick + RestoreInvincibilityLength;
        DefocusTimestamp = (float)Raylib.GetTime();
    }
}
