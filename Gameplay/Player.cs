using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Data;
using System.Numerics;
using System.Reflection;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay.GameplayOverlays;
using DmitryAndDemid.Utils;

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
    public TextureHandle SourceTexture;
    public Rect SourceRect;
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
        SourceRect = new Rect(0, 0, 32, 32);
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
        if ((GameBox.CurrentTick - GameBox.TickOffset) % 100 == 0)
            Signal--;
        Controller.Update(this, GameBox.CurrentTick);
        X = Math.Clamp(X, 8, 376);
        Y = Math.Clamp(Y, 8, 440);
        if (IsInDeathCooldown)
        {
            CollisionEnabled = false;
            X = 192;
            Y = 400 + 112 * ((RestoreTick - GameBox.CurrentTick) / 60f);
        }
        else
        {
            CollisionEnabled = true;
        }
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

            if (value < heartPoints)
            {
                if (bombs < 3)
                {
                    bombsSpices = 0;
                    bombs = 3;
                }
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
            if (value is > 36 or < 0)
                return;
            if(value > 24 && value > signal)
                GameBox.SpawnMysticalToilet();
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
                // TODO: Play Extend SoundHandle
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
                else
                {
                    //TODO: Play next power level sound
                }
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
            Weapon.Bomb();
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
            Signal += 1;
            // TODO: Play Graze SFX
        }
    }

    public Rect Collision => new Rect(X-CollisionRadius/2, Y-CollisionRadius/2, CollisionRadius, CollisionRadius);

    public bool IsInDeathCooldown
    {
        get => RestoreTick > GameBox.CurrentTick;
        set => RestoreTick = GameBox.CurrentTick + 120;
    }

    public Vector2 Position => new Vector2(X, Y);

    private int graze;
    private bool isFocused = false;
    private bool isBombing = false;
    private bool isShooting = false;
    private Vector2 CollisionDotPos;
    private const int RestoreInvincibilityLength = 300;
    private const int RestoreAnimationLength = 60;
    public int RestoreTick = 0;
    
    /// <summary>
    /// Overwrites the life / bomb stock outright, bypassing the property setters' side effects (extend jingle,
    /// bomb top-up, the &lt;0 game-over). Used to seed a run for a mode — full practice starts maxed, spell
    /// practice starts empty.
    /// </summary>
    public void SetLivesAndBombs(int lives, int bombCount)
    {
        heartPoints = lives;
        bombs = bombCount;
        heartSpices = 0;
        bombsSpices = 0;
        GameBox.UpdateUI();
    }

    /// <summary>
    /// Brings the player back after a continue: a fresh default life / bomb stock, re-centred, and eased back in
    /// through the usual death-cooldown entrance. Writes the backing fields directly so reviving from a negative
    /// life count doesn't re-trigger game-over through the HeartPoints setter.
    /// </summary>
    public void Revive()
    {
        heartPoints = 2;
        bombs = 3;
        heartSpices = 0;
        bombsSpices = 0;
        power = 300;
        X = 192;
        Y = 400;
        CollisionEnabled = false;
        IsInDeathCooldown = true;   // brief entrance + invulnerability, like a normal respawn
        Weapon.DefocusTimestamp = GameBox.CurrentTick;
        Weapon.UpdatePower();
        GameBox.UpdateUI();
    }

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
        GameBox.ClearBullets();
        IsInDeathCooldown = true;
        //RestoreTick = Game.CurrentTick + RestoreInvincibilityLength;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["dead"]);
        Weapon.DefocusTimestamp = GameBox.CurrentTick;
    }

    public void Draw()
    {
        Weapon.DrawBottomLayer();
        DrawTexturePro(
            SourceTexture, SourceRect, new Rect(X-16, Y-16,32,32), 
            Vector2.Zero, 0, Rgba.White
            );
    }
}
