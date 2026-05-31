using System.Data;
using System.Numerics;
using System.Reflection;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay.GameplayOverlays;
using DmitryAndDemid.Utils;
using Gtk;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Player
{
    public float X = 192;
    public float Y = 400;
    public float CollisionRadius = 2f;
    
    Action<Player, bool> ShootAction;
    Action<Player, bool> BombAction;
    public ProtogonistData ProtogonistData;
    public PlayerControllerBase Controller;
    public PlayerWeapon Weapon;
    
    public const float FocusedDifference = 8f;
    public const float DefocusedDifference = 32f;
    public const int FocusAnimationChangingLength = 15;
    public bool CollisionEnabled = true;
    public Texture2D SourceTexture;
    public Rectangle SourceRect;
    public GameBox GameBox;
    
    public float PointMagnetRadius => 
        Y < 100 || !CollisionEnabled ? 6000f : 24f;

    public Player(GameBox game, ProtogonistData data, PlayerControllerBase controller) 
    {
        GameBox = game;
        Controller = controller;
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
    
    public void Update()
    {
        if (RestoreTick > GameBox.CurrentTick)
        {
            int j = GameBox.CurrentTick - RestoreTick + RestoreInvincibilityLength;
            if (j < RestoreAnimationLength)
            {
                //UpdateCollisionRender(new Vector2(192, 400) + new Vector2(0, 128) * (1-((float)j/(float)RestoreInvincibilityLength)), 0);
                X = 192;
                Y = (int)(400 + 128 * (1 - ((float)j / (float)RestoreInvincibilityLength)));
                return;
            }
        }
        else
        {
            //CollisionDotPos = PositionTo;
            CollisionEnabled = true;
        }
        Controller.Update(this, GameBox.CurrentTick);
        X = Math.Clamp(X, 8, 376);
        Y = Math.Clamp(Y, 8, 440);
        
        if (GameBox.CurrentTick % 4 == 0)
            SourceRect.X += 32;
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
            if (value > heartPoints)
            {
                //TODO: Play extend sound
                GameBox.AddOverlay(new BasicGameplayOverlay(GameBox, "extend.png", .5f, 3));
            }
            heartPoints = value;
            if (value < 0)
            {
                GameBox.IsPaused = true;
                GameBox.IsGameOver = true;
                // TODO: Play Game Over Song
            }
            GameBox.UpdateUI();
        }
    }

    public int Signal
    {
        get => signal;
        set
        {
            if (signal.Equals(value))
                return;
            signal = value;
        }
    }

    public int HeartSpices
    {
        get => heartSpices;
        set
        {
            if (value == heartSpices)
                return;
            if (value < 0)
                return;
            if (value > 3)
                HeartPoints += value / 4;
            heartSpices = value % 4;
            GameBox.UpdateUI();
        }
    }

    public int Bombs
    {
        get => bombs;
        set
        {
            if (bombs == value)
                return;
            if (bombs < value)
            {
                // TODO: Play Extend Sound
            }
            bombs = value;
            GameBox.UpdateUI();
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
            GameBox.UpdateUI();
        }
    }

    private int bombs = 3;
    private int bombsSpices = 0;
    private int heartSpices = 0;
    private int heartPoints = 2;
    private int signal = 0;
    
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
                    GameBox.AddOverlay(new BasicGameplayOverlay(GameBox, "full-power.png", .5f, 3));
                }
                //TODO: Play next power level sound
            }
            power = newValue;
            GameBox.UpdateUI();
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
                Weapon.FocusTimestamp = GameBox.CurrentTick -
                                        Math.Max(FocusAnimationChangingLength + Weapon.DefocusTimestamp - GameBox.CurrentTick,
                                            0);
                Weapon.DefocusTimestamp = int.MaxValue;
            }
            else
            {
                Weapon.DefocusTimestamp = GameBox.CurrentTick -
                                          Math.Max(FocusAnimationChangingLength + Weapon.FocusTimestamp - GameBox.CurrentTick,
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
            GameBox.UpdateUI();
            // TODO: Play Graze SFX
        }
    }

    public Rectangle Collision => new Rectangle(X-CollisionRadius/2, Y-CollisionRadius/2, CollisionRadius, CollisionRadius);
    
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
            //GameBox.AddObject(new PowerCollectable(Game, PositionTo,
                //Helper.GetDirection(angle * i)));
        }
        Power -= 50;
        HeartPoints -= 1;
        GameBox.AddScreenEffect(new GameplayScreenEffect(
                GameBox, new Vector2(GameBox.Player.X, GameBox.Player.Y), 0, "die", GameBox.GetTime(), GameBox.GetTime() + 1.5f
            ));
        CollisionEnabled = false;
        //RestoreTick = Game.CurrentTick + RestoreInvincibilityLength;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["dead"]);
        Weapon.DefocusTimestamp = GameBox.CurrentTick;
    }

    public void Draw()
    {
        Weapon.DrawBottomLayer();
        Raylib.DrawTexturePro(
            SourceTexture, SourceRect, new Rectangle(X-16, Y-16,32,32), 
            Vector2.Zero, 0, Color.White
            );
    }
}
