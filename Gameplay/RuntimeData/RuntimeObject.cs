using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
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
        FlagIsUsed = 0x0800,
        FlagApplyShader = 0x8000,
        FlagIsDied = 0x1000,
        FlagUseRenderRotation = 0x2000,
        FlagUseUpdateScript = 0x4000;
    
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

    public static RuntimeObject LoadFromFile(FileEntityInfo info, GameBox box)
    {
        RuntimeObject entity = new RuntimeObject();
        entity.Box = box;
        Array.Copy(info.Header, entity.Header, info.Header.Length);
        Array.Copy(info.FloatingPoints, entity.FloatingPoints, info.FloatingPoints.Length);
        entity.Header[0] |= FlagUseUpdateScript;
        if ((entity.Header[0] & FlagUseUpdateScript) == FlagUseUpdateScript)
            entity.UpdateAction = ActionsScope.ObjectActions[info.UpdateScript];
        if ((entity.Header[0] & FlagIsBullet) == FlagIsBullet)
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
        }
        return entity;
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

    public float X
    {
        get => FloatingPoints[0x10];
        set => FloatingPoints[0x10] = value;
    }

    public float Y
    {
        get =>  FloatingPoints[0x11];
        set => FloatingPoints[0x11] = value;
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
}