using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay.Effects;
using DmitryAndDemid.Gameplay.GameplayOverlays;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Utils;
using Microsoft.CodeAnalysis.Scripting;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class RuntimeObject
{
    public const int
        FlagIsBullet = 0x0001,
        FlagIsGroupChild = 0x0002,
        FlagIsGroupParent = 0x0004,
        FlagOverrideColor = 0x0004,
        FlagUseCreateScript = 0x0008,
        FlagUseRemoveScript = 0x0010,
        FlagClearProtected = 0x0020,
        FlagDangerousRelatedToPlayer = 0x0040,
        FlagUseBadDropScenario = 0x0080,
        FlagDropWhenCleared = 0x0100,
        FlagIsBoss = 0x0200,
        FlagIsGrazed = 0x0201,
        FlagUseDieScript = 0x0400,
        FlagDangerousRelatedToEnemy = 0x0400,
        FlagIsCollectableBullet = 0x100001,
        FlagIsUsed = 0x0800,
        FlagApplyShader = 0x8000,
        FlagIsDied = 0x1000,
        FlagUseRenderRotation = 0x2000,
        FlagUseUpdateScript = 0x4000,
        FlagIsFinalBossChapter = 0x100000,
        FlagIsCollectable = 0x10000;

    public static FileEntityInfo[] CollectableFEIs = new FileEntityInfo[8];

    static RuntimeObject()
    {
        for (int i = 0; i < 8; i++)
        {
            CollectableFEIs[i] = new FileEntityInfo();
            CollectableFEIs[i].Header[0] = 0b0000_0000;
            CollectableFEIs[i].Header[0] |= 0x10000;
            CollectableFEIs[i].Header[4] = i;
        }
    }
    
    private Rectangle Source = new();
    private Rectangle Target = new();
    public Vector2 Origin = new();
    public int[] Header = new int[128];
    public float[] FloatingPoints = new float[128];
    public Texture2D Texture;
    public GameBox Box;
    public RuntimeObjectReferenceAction? CreateAction;
    public RuntimeObjectReferenceAction? UpdateAction;
    public RuntimeObjectReferenceAction? DieAction;
    public RuntimeObjectReferenceAction? RemoveAction;
    public Shader Shader;
    public Vector2 TexturePosition, TextureSize, TotalTextureSize;
    public GameplayScreenEffect? BackgroundDistortionEffect;
    public BossCircleScreenEffect? BossCircleEffect;

    RuntimeObject()
    {
    }
    
    public static RuntimeObject LoadFromFile(FileEntityInfo info, GameBox box)
    {
        RuntimeObject entity = new RuntimeObject();
        entity.Box = box;
        Array.Copy(info.Header, entity.Header, info.Header.Length);
        Array.Copy(info.FloatingPoints, entity.FloatingPoints, info.FloatingPoints.Length);
        entity.Header[0] |= FlagUseUpdateScript;
        if ((entity.Header[0] & FlagUseUpdateScript) == FlagUseUpdateScript)
            entity.UpdateAction = ActionsScope.ObjectActions[info.UpdateScript];
        if ((entity.Header[0] & FlagIsCollectable) == FlagIsCollectable)
        {
            
        }
        else if ((entity.Header[0] & FlagIsBullet) == FlagIsBullet)
        {
            BulletRenderingInfo bulletRenderInfo = Runtime.CurrentRuntime.BulletVisualPresets[info.Visual];
            var shader = Runtime.CurrentRuntime.Shaders[bulletRenderInfo.Effect == "" ? "basic_bullet_shader" : bulletRenderInfo.Effect];
            entity.Texture = bulletRenderInfo.GetTexture(info.Header[4]);
            var spPos = bulletRenderInfo.GetSpritePosition(info.Header[4]);
            entity.Source = new(
                spPos - new Vector2(32),
                bulletRenderInfo.SourceSize + new Vector2(64)
            );
            entity.Target.Size = entity.Source.Size;
            entity.Origin = entity.Source.Size / 2;
            entity.Header[0] |= FlagApplyShader;
            entity.Shader = shader;
            entity.FloatingPoints[0x13] = bulletRenderInfo.Collision;
            if (bulletRenderInfo.Effect == "")
            {
                entity.Header[0x40] = Raylib.GetShaderLocation(shader, "created_at");
                entity.Header[0x41] = Raylib.GetShaderLocation(shader, "time");
                entity.Header[0x42] = Raylib.GetShaderLocation(shader, "position");
                entity.Header[0x43] = Raylib.GetShaderLocation(shader, "resolution");
                entity.Header[0x44] = Raylib.GetShaderLocation(shader, "output_resolution");
            }
            else
            {
                entity.Header[0x40] = bulletRenderInfo.LocFXCreatedAt;
                entity.Header[0x41] = bulletRenderInfo.LocFXTime;
                entity.Header[0x42] = bulletRenderInfo.LocFXPosition;
                entity.Header[0x43] = bulletRenderInfo.LocFXResolution;
                entity.Header[0x44] = bulletRenderInfo.LocFXOutputResolution;
            }
            entity.TexturePosition = spPos;
            entity.TextureSize = bulletRenderInfo.SourceSize;
            entity.TotalTextureSize = Helper.GetSize(entity.Texture);
        }
        else
        {
            EntityVisual visual = EntityVisual.Visuals[info.Visual];
            if (!info.OverrideDeathColor)
            {
                entity.Header[0xB] = visual.DeathCircleColor;
                entity.Header[0xC] = visual.DeathParticleGlowColor;
            }
            entity.Texture = Runtime.CurrentRuntime.Textures[visual.Texture];
            entity.Source = new(
                visual.SourcePosition,
                visual.RenderSize
            );
            entity.Target.Size = entity.Source.Size;
            entity.Origin = entity.Source.Size / 2;
            entity.FloatingPoints[0x13] = visual.Collision;
            
            if (info.IsBoss)
            {
                entity.BackgroundDistortionEffect = new GameplayScreenEffect(box, 
                    new Vector2(192, 192), 
                    4, 
                    "boss_background_distortion", 
                    box.GetTime(), 
                    float.MaxValue)
                {
                    Layer = GameplayScreenEffect.EffectLayer.BackgroundOnly,
                    StepLength = 1,
                    UseSteps = true
                };
                entity.BossCircleEffect = new BossCircleScreenEffect(box, Vector2.Zero, 0, box.GetTime(), float.MaxValue);
                box.AddScreenEffect(entity.BackgroundDistortionEffect);
                box.AddScreenEffect(entity.BossCircleEffect);
                box.AddOverlay(new BossHealthOverlay(box, entity));
                entity.FloatingPoints[0xa] = entity.FloatingPoints[0];
            }
        }

        
        return entity;
    }

    public void LoadAnotherFile(FileEntityInfo info)
    {
        CreateAction = info.UseCreateScript ? ActionsScope.ObjectActions[info.CreateScript] : null;
        UpdateAction = ActionsScope.ObjectActions[info.UpdateScript];
        CreatedAt = Box.CurrentTick;
        RemoveAction = info.UseRemoveScript ? ActionsScope.ObjectActions[info.RemoveScript] : null;
        if(!info.IsBullet)
            DieAction = info.UseDieScript ? ActionsScope.ObjectActions[info.DieScript] : null;
        Array.Copy(info.Header, Header, info.Header.Length);
        Array.Copy(info.FloatingPoints, FloatingPoints,info.FloatingPoints.Length);
        info.FloatingPoints[10] = info.FloatingPoints[0];
    }

    public RuntimeObject CloneWithPositionSpawnTick(int x, int y, int tick)
    {
        RuntimeObject entity = new RuntimeObject();
        entity.Header = Header;
        entity.Source = Source;
        entity.Texture = Texture;
        entity.Header[0x15] = tick;
        entity.Header[0x05] = x;
        entity.Header[0x06] = y;
        entity.UpdateOrigin();
        entity.UpdateTargetRectangle();
        return entity;
    }
    
    public Drop BadDrop => new Drop(Header[0x18]);
    public Drop GoodDrop => new Drop(Header[0x19]);
    
    public float RenderRotation
    {
        get => BitConverter.ToSingle(BitConverter.GetBytes(Header[0x0D]), 0);
        set => Header[0x0D] = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
    }

    public Rectangle SourceRectangle => Source;
    public Rectangle TargetRectangle => Target with { X = FloatingPoints[0x10], Y = FloatingPoints[0x11] };
    
    public float FacingRotation
    {
        get => FloatingPoints[0x6];
        set => FloatingPoints[0x6] = value;
    }
    
    public float RenderScaleX
    {
        get => BitConverter.ToSingle(BitConverter.GetBytes(Header[0x09]), 0);
        set
        {
            Header[0x09] = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            UpdateTargetRectangle();
            UpdateOrigin();
        }
    }

    public float RenderScaleY
    {
        get => BitConverter.ToSingle(BitConverter.GetBytes(Header[0x0A]), 0);
        set
        {
            Header[0x0A] = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            UpdateTargetRectangle();
            UpdateOrigin();
        }
    }

    public float CollisionScale
    {
        get => FloatingPoints[2];
        set => FloatingPoints[2] = value;
    }

    public byte Transparency
    {
        get => (byte)Header[0x0C];
        set => Header[0x0C] = value;
    }

    public float Speed
    {
        get => FloatingPoints[7];
        set => FloatingPoints[7] = value;
    }

    public Vector2 CollectableVelocity
    {
        get => new(FloatingPoints[2],  FloatingPoints[6]);
        set
        {
            FloatingPoints[2] = value[0];
            FloatingPoints[6] = value[1];
        }
    }

    public float X
    {
        get => FloatingPoints[0x10];
        set
        {
            FloatingPoints[0x10] = value;
            if (0 != (Header[0] & FlagIsBoss))
                BackgroundDistortionEffect.Position.X =
                    BossCircleEffect.Position.X = 
                    value;
        }
    }

    public float Y
    {
        get =>  FloatingPoints[0x11];
        set
        {
            FloatingPoints[0x11] = value;
            if (0 != (Header[0] & FlagIsBoss))
                BackgroundDistortionEffect.Position.Y =
                    BossCircleEffect.Position.Y = 
                        value;
        }
    }

    public float Z
    {
        get => FloatingPoints[0x12];
        set => FloatingPoints[0x12] = value;
    }

    public int CreatedAt
    {
        get => Header[0x17];
        set => Header[0x17] = value;
    }

    public Vector2 Position
    {
        get => new(FloatingPoints[0x10],  FloatingPoints[0x11]);
    }

    public int SpawnId => Header[2];
    public int BossId => Header[7];

    void UpdateSourceRectangle()
    {
        Source = new Rectangle(Header[3], Header[4], Header[1], Header[2]);
    }

    void UpdateTargetRectangle()
    {
        Target.Width = RenderScaleX * Header[1];
        Target.Height = RenderScaleY * Header[2];
        Target.X = Header[5] - (Target.Width / 2);
        Target.Y = Header[6] - (Target.Height / 2);
    }

    void UpdateOrigin()
    {
        //Origin = new Vector2(RenderScaleX * Header[1] / 2,  RenderScaleY * Header[2] / 2);
    }

    public void Update()
    {
        UpdateAction?.Invoke(this);
    }

    public void UpdateCollectableBullet()
    {
        var direction = Helper.GetDirection(Position, new Vector2(Box.Player.X, Box.Player.Y));
        FloatingPoints[2] = Raymath.MoveTowards(FloatingPoints[2], direction.X * 100000, 0.1f);
        FloatingPoints[6] = Raymath.MoveTowards(FloatingPoints[6], direction.Y * 100000, 0.1f);
        X += FloatingPoints[2];
        Y += FloatingPoints[6];
        if (Helper.IsCollied(TargetRectangle, Box.Player.Collision))
        {
            Box.Score += (int)Math.Pow(10, (448-Y)/10) * Header[5];
            Box.RemoveObject(this);
        }
    }

    public void UpdateCollectable()
    {
        FloatingPoints[0x5] = MathF.Abs(FloatingPoints[2]) > 0 ? Box.CurrentTick : 0;
        FloatingPoints[2] = Raymath.MoveTowards(FloatingPoints[2], 0, 0.1f);
        FloatingPoints[6] = Raymath.MoveTowards(FloatingPoints[6], float.MaxValue, 0.1f);
        X += FloatingPoints[2];
        Y += FloatingPoints[6];
        if (Helper.IsCollied(TargetRectangle, Box.Player.Collision))
        {
            switch (Header[4])
            {
                case 0:
                    Box.Player.Power += 1;
                    break;
                case 1:
                    Box.Player.Power += 100;
                    break;
                case 2:
                    Box.Player.Power = 400;
                    break;
                case 3:
                    Box.Score += (int)(MathF.Floor(MathF.Pow(2, 14 * (464-Box.Player.Y) / 464) / 16384 * 30) / 30 * 100000);
                    break;
                case 4:
                    Box.Player.HeartPoints++;
                    break;
                case 5:
                    Box.Player.HeartSpices++;
                    break;
                case 6:
                    Box.Player.Bombs++;
                    break;
                case 7:
                    Box.Player.BombsSpices++;
                    break;
            }
            Box.RemoveObject(this);
        }
    }
}