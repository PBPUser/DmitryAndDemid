using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.Effects;

public class BossCircleScreenEffect : GameplayScreenEffect
{
    public BossCircleScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear, RuntimeObject obj) 
        : base(box, position, index, "boss_cursor", timeAppear, timeDisappear)
    {
        Boss = obj;
        Layer = EffectLayer.BackgroundOnly;
        CursorTexture = Runtime.CurrentRuntime.Textures["cursosor2.png"];
        LocationTexture = GetShaderLocation(Shader, "textureCursor");
        SetShaderValueTexture(Shader, LocationTexture, CursorTexture);
    }

    private RuntimeObject Boss;
    private int LocationTexture;
    TextureHandle CursorTexture;
    
    
    public override void ApplyShading(float gameTime)
    {
        Position = Boss.Position;
        base.ApplyShading(gameTime);
    }
}