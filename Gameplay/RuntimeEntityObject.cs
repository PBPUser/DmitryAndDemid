using System.Numerics;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;
using Microsoft.CodeAnalysis.Scripting;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class RuntimeEntityObject
{
    public const int
        IsBullet = 0x0001,
        IsGroupChild = 0x0002,
        IsGroupParent = 0x0004,
        OverrideColor = 0x0004,
        UseCreateScript = 0x0008,
        UseRemoveScript = 0x0010,
        ClearProtected = 0x0020,
        PlayerDanger = 0x0040,
        UseBadDropScenario = 0x0080,
        DropWhenCleared = 0x0100,
        IsBoss = 0x0200,
        IsGrazed = 0x0201,
        UseDieScript = 0x0400,
        DangerousForEnemy = 0x0400,
        ApplyShader = 0x0800,
        IsDied = 0x1000,
        UseRenderRotation = 0x2000,
        UseUpdateScript = 0x4000;
    
    private Rectangle Source = new();
    private Rectangle Target = new();
    public Vector2 Origin = new();
    public int[] Header = new int[128];
    public Texture2D Texture;
    
    

    public static RuntimeEntityObject LoadFromFile(FileEntityInfo info, ref Script<object>[] scripts)
    {
        RuntimeEntityObject entity = new RuntimeEntityObject();
        entity.Header[0] = info.Header[0];
        if ((entity.Header[0] & UseCreateScript) == UseCreateScript)
        {
            entity.Header[0x10] = info.Header[1];
        }
        entity.Header[0x11] = info.Header[2];
        if((entity.Header[0] & UseRemoveScript) == UseRemoveScript)
            entity.Header[0x12] = info.Header[3];
        if ((entity.Header[0] & IsBullet) == IsBullet)
        {
            entity.Header[0x12] = info.Header[3];
            var visual = BulletVisual.Constants[info.Visual];
            var position = visual.GetSourcePosition(Helper.ColorIntToVector3(info.Header[0x7]));
            var size = visual.GetSourceSize();
            entity.Texture = visual.GetTexture(Helper.ColorIntToVector3(info.Header[0x7]));
            entity.Header[1] = (int)size.X;
            entity.Header[2] = (int)size.Y;
            entity.Header[3] = (int)position.X;
            entity.Header[4] = (int)position.Y;
        }
        else
        {
            var visual = EntityVisual.Visuals[info.Visual];
            entity.Texture = Runtime.CurrentRuntime.Textures[visual.Texture];
            entity.Header[1] = (int)visual.RenderSize.X;
            entity.Header[2] = (int)visual.RenderSize.Y;
            entity.Header[3] = (int)visual.SourcePosition.X;
            entity.Header[4] = (int)visual.SourcePosition.Y;
            entity.Header[0x16] = info.Header[0x9];
            if ((entity.Header[0] & UseDieScript) == UseDieScript)
            {
                entity.Header[0x13] = info.Header[0xB];
            }
            if ((entity.Header[0] & IsBoss) == IsBoss)
            {
                entity.Header[0x21] = info.Header[0xC];
            }
        }
        entity.Header[8] = info.Header[0x4]; 
        entity.Header[9] = BitConverter.ToInt32(BitConverter.GetBytes(info.Header[0x4]));
        entity.Header[0xB] = entity.Header[9];
        entity.Header[0xC] = entity.Header[6];
        entity.Header[0xF] = entity.Header[0x5];
        return entity;
    }

    public RuntimeEntityObject CloneWithPositionSpawnTick(int x, int y, int tick)
    {
        RuntimeEntityObject entity = new RuntimeEntityObject();
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

    public Rectangle SourceRectangle => Source with { X = Header[0x01], Y = Header[0x02] };
    public Rectangle TargetRectangle => Target with { X = Header[0x05], Y = Header[0x06] };
    
    public float FacingRotation
    {
        get => BitConverter.ToSingle(BitConverter.GetBytes(Header[0x0E]), 0);
        set => Header[0x0E] = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
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
        get => BitConverter.ToSingle(BitConverter.GetBytes(Header[0x0B]), 0);
        set => Header[0x0B] = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
    }

    public byte Transparency
    {
        get => (byte)Header[0x0C];
        set => Header[0x0C] = value;
    }

    public float Speed
    {
        get => BitConverter.ToSingle(BitConverter.GetBytes(Header[0x17]), 0);
        set => Header[0x17] = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
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
        Origin = new Vector2(RenderScaleX * Header[1] / 2,  RenderScaleY * Header[2] / 2);
    }
}