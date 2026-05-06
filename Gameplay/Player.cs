using System.Data;
using System.Numerics;
using System.Reflection;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay.Collectables;
using DmitryAndDemid.Gameplay.PlayerWeapons;
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
    public PlayerWeapon Weapon;
    
    public const float FocusedDifference = 8f;
    public const float DefocusedDifference = 32f;
    public const float FocusAnimationChangingLength = 0.25f;

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
        Speed = data.Speed;
        FocusSpeed = data.FocusSpeed;
        var type = Assembly.GetExecutingAssembly().GetTypes().FirstOrDefault(x => x.IsAssignableTo(typeof(PlayerWeapon)) && x.Name == data.WeaponClassName);
        Weapon = (PlayerWeapon)type.GetConstructor([typeof(Player)]).Invoke([this]);
        Weapon.UpdatePower();
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
        Weapon.Update();
        if (!isShooting)
            return;
        Weapon.Shoot();
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
            Weapon.UpdatePower();
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
                Weapon.FocusTimestamp = Game.GetTime() -
                                        MathF.Max(FocusAnimationChangingLength + Weapon.DefocusTimestamp - Game.GetTime(),
                                            0);
                Weapon.DefocusTimestamp = float.MaxValue;
            }
            else
            {
                Weapon.DefocusTimestamp = Game.GetTime() -
                                          MathF.Max(FocusAnimationChangingLength + Weapon.FocusTimestamp - Game.GetTime(),
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

    
    private Vector2 CollisionDotPos;
    
    private const int RestoreInvincibilityLength = 300;
    private const int RestoreAnimationLength = 60;
    private int RestoreTick = 0;
    
    
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
        Weapon.DefocusTimestamp = (float)Raylib.GetTime();
    }
}
