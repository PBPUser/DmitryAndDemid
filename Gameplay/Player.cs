using System.Data;
using System.Numerics;
using System.Reflection;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay.Collectables;
using DmitryAndDemid.Utils;
using GLib;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Player : RuntimeObject
{
    Action<Player, bool> ShootAction;
    Action<Player, bool> BombAction;
    public ProtogonistData ProtogonistData;
    public PlayerControllerBase Controller;

    public float PointMagnetRadius => 
        PositionTo.Y < 100 || !CollisionEnabled ? 6000f : 24f;

    public Player(Game game, ProtogonistData data, PlayerControllerBase controller) : base(game, new Vector2(192, 400), new Vector2(32, 32), Helper.GetSize(Runtime.CurrentRuntime.Textures[data.Sprite]), new Vector2(8), 0)
    {
        Controller = controller;
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

    public int Speed = 0;
    public int FocusSpeed = 0;
    
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
        Controller.Update(this, Game.CurrentTick);
        float time = (float)Raylib.GetTime();
        
        BulletSourcePositionsCount = (Power / 100);
        float dif = DefocusedDifference + (FocusedDifference - DefocusedDifference) * (float)Helper.ComputeObjectTime(Raylib.GetTime(), FocusTimestamp, FocusAnimationChangingLength,
            DefocusTimestamp + FocusAnimationChangingLength, FocusAnimationChangingLength);
        float angleStart = time * 2;
        float angleDif = MathF.PI * 2 / BulletSourcePositionsCount;
        for (int i = 0; i < BulletSourcePositionsCount; i++)
            BulletSourcePositions[i] = PositionTo + (Helper.GetDirection(angleStart + (angleDif * i)) * dif);
        
        if (Game.CurrentTick % 20 != 0)
            return;
        if (!isShooting)
            return;
        Bullet b;
        float totalDamage = Power / 100f;
        float singleDamage = totalDamage / BulletSourcePositionsCount;
        for (int i = 0; i < BulletSourcePositionsCount; i++)
        {
            b = new Bullet(Game,new BulletSpawnInfo()
            {
                Damage = singleDamage,
                Speed = 6f,
                BulletVisual = "akob",
                Rotation = MathF.PI,
                Position = BulletSourcePositions[i],
                BulletActionClass = "MoveByDirection",
                Args = ["UseRotation"]
            },0, false);
            b.PlayerShoot = true;
            Game.AddObject(b);
        }
    }

    public int HeartPoints
    {
        get => heartPoints;
        set
        {
            if (heartPoints == value)
                return;
            heartPoints = value;
            if (value < 0)
            {
                Game.Playing = false;
                Game.ForcedPause = true;
            }
            Game.UpdateUI();
        }
    }

    public int HeartSpices
    {
        get => heartSpices;
        set
        {
            if (value == heartSpices)
                return;
            if (value > 4)
                HeartPoints += value / 4;
            heartSpices = value % 4;
            Game.UpdateUI();
        }
    }

    public int Bombs
    {
        get => bombs;
        set
        {
            if (bombs == value)
                return;
            bombs = value;
            Game.UpdateUI();
        }
    }

    public int BombsSpices
    {
        get => bombsSpices;
        set
        {
            if (bombsSpices == value)
                return;
            if (bombsSpices > 4)
                Bombs += bombsSpices / 4;
            bombsSpices = value % 4;
        }
    }

    private int bombs = 3;
    private int bombsSpices = 0;
    private int heartSpices = 0;
    private int heartPoints = 2;
    
    public int Power
    {
        get => power;
        set
        {
            int newValue = Math.Clamp(value, 100, 400);
            if (power == newValue)
                return;
            if (newValue / 100 > power / 100)
            {
                if (newValue > 399)
                {
                    // TODO: Play full power sound
                    Game.SetFullPower();
                }
                //TODO: Play next power level sound
            }
            power = newValue;
            Game.UpdateUI();
        }
    }

    private int power = 300;
    
    public bool IsFocused
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
    
    public bool IsBombing
    {
        get => isBombing;
        set
        {
            if (isBombing == value)
                return;
            isBombing = value;
        }
    }

    public bool IsShooting
    {
        get => isShooting;
        set
        {
            if (isShooting == value)
                return;
            isShooting = value;
            
        }
    }

    public int Graze
    {
        get => graze;
        set
        {
            if (graze == value)
                return;
            graze = value;
            Game.UpdateUI();
        }
    }

    private int graze;
    
    private bool isFocused = false;
    private bool isBombing = false;
    private bool isShooting = false;

    private int BulletSourcePositionsCount = 0;
    
    private const float FocusedDifference = 8f;
    private const float DefocusedDifference = 32f;
    
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
        
    }

    private static Rectangle AkobRectangleSource = new Rectangle(128, 64, 16, 16);
    
    public void RenderTopLayer()
    {
        for(int i = 0; i < BulletSourcePositionsCount; i++)
            Raylib.DrawTexturePro(SourceTexture, AkobRectangleSource, new Rectangle(BulletSourcePositions[i],new Vector2(16)), new Vector2(8)
                , 0, Color.White);
        byte transparency = Helper.TimeToTransparency(
            Helper.ComputeObjectTime(Raylib.GetTime(), FocusTimestamp, FocusAnimationChangingLength,
                DefocusTimestamp + FocusAnimationChangingLength, FocusAnimationChangingLength));
        Raylib.DrawTexturePro(SourceTexture, PlayerTopLayerSource, new Rectangle(PositionTo, new Vector2(64)), 
            new Vector2(32), 0, Color.White with {A=transparency} );
    }

    private const int RestoreInvincibilityLength = 300;
    private const int RestoreAnimationLength = 60;
    private int RestoreTick = 0;
    public Vector2[] BulletSourcePositions = new Vector2[4];
    
    
    public void Die()
    {
        float angle = -MathF.PI / 7;
        for (int i = 0; i < 7; i++)
        {
            Game.AddObject(new PowerCollectable(Game, PositionTo,
                Helper.GetDirection(angle * i)));
        }
        Power -= 50;
        HeartPoints -= 1;
        Game.SetDied();
        CollisionEnabled = false;
        RestoreTick = Game.CurrentTick + RestoreInvincibilityLength;
        DefocusTimestamp = (float)Raylib.GetTime();
    }
}
