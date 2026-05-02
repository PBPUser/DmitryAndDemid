using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Screens;

public class CreditsScreen : Screen
{
    public CreditsScreen()
    {
        BgTarget = Helper.GetFullscreenSource();
        NikitosJumpingTexture = Raylib.LoadRenderTexture(Runtime.CurrentRuntime.Width * 4, (int)(Runtime.CurrentRuntime.Height * .75f));
        DmitryEatingTexture = Raylib.LoadRenderTexture(Runtime.CurrentRuntime.Width * 4, (int)(Runtime.CurrentRuntime.Height * .75f));
        NSource = new Rectangle(0,0,Runtime.CurrentRuntime.Width*1.5f,Runtime.CurrentRuntime.Height*.75f);
        DSource = new Rectangle(0,0,Runtime.CurrentRuntime.Width*1.5f,Runtime.CurrentRuntime.Height*.75f);
        BeatLength = 60d / BPM;
        BeatDelay = BeatLength * (BeatAnimateRate - 1);
        DTarget = new Rectangle(BgTarget.Size * new Vector2(1, .3f) - NSource.Size / 2, NSource.Size);
        NTarget = new Rectangle(BgTarget.Size * new Vector2(2f, 1f) - DSource.Size / 2, NSource.Size);
        ForkImgSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["vilkaCut.png"]);
        ForkImgTarget = new Rectangle(0, 0,
            ForkImgSource.Size * .75f * NTarget.Height / ForkImgSource.Height
        );
        ForkImgTarget.X = -ForkImgTarget.Width / 2;
        ForkImgTarget.Y = NSource.Height / 2;
        NiImgSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["nikitos_boss_art.png"]);
        NiImgTarget = new Rectangle(0, 0,
            NiImgSource.Size * .75f * NTarget.Height / NiImgSource.Height
        );
        NiImgTarget.X = -NiImgTarget.Width / 2;
        NiImgTarget.Y = (NSource.Height - NiImgTarget.Height) / 2;
        NikitosStep = 1.2f * NiImgTarget.Width;
        NSource.Y = NSource.Height;
        DSource.Y = DSource.Height;
        NSource.Height *= -1;
        DSource.Height *= -1;
        NOrigin = NSource.Size / 2;
        DOrigin = DSource.Size / 2;
        NikitosJump = (NTarget.Height - NiImgTarget.Height) / 2;

        DBottomSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["dima_bottom.png"]);
        DTopSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["dima_top.png"]);
        DTopTarget = new Rectangle(0, 0,
            DTopSource.Size * .75f * DTarget.Height / DTopSource.Height
        );
        DBottomTarget = new Rectangle(0, 0,
            DBottomSource.Size * .75f * DTarget.Height / DBottomSource.Height
        );
        DBottomTarget.Y = DTarget.Height - DBottomTarget.Height;
        ObjectSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["object.png"]);
        ObjectTarget = new Rectangle(Vector2.Zero, ObjectSource.Size / 2);
        ObjectTarget.X = -ObjectTarget.Width / 2;
        DTopTarget.X = -DTopTarget.Width / 2;
        DBottomTarget.X = -DBottomTarget.Width / 2;
        ObjectTarget.Y = (DTarget.Height - ObjectTarget.Height) / 1.2f;
        DmitryStep = 1.2f * DTopTarget.Width;
        DmitryJump = 0.15f * DSource.Height;
        Bloom = Runtime.CurrentRuntime.Shaders["bloom_ending"];
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "resolution"), NSource.Size, ShaderUniformDataType.Vec2);
    }

    private Shader Bloom;
    
    private const double
        CreditsLength = 30;

    private const int
        BPM = 120, BeatAnimateRate = 8;
    
    private RenderTexture2D 
        NikitosJumpingTexture, DmitryEatingTexture;

    private Rectangle
        BgSource = new(0, 0, 1440, 1080),
        BgTarget,
        NSource,
        NTarget,
        NiImgSource,
        NiImgTarget,
        ForkImgSource,
        ForkImgTarget,
        DTopSource,
        DTopTarget,
        DBottomSource,
        DBottomTarget,
        ObjectSource,
        ObjectTarget,
        DSource,
        DTarget;
    
    private Vector2 NOrigin, DOrigin;
    
    private double BeatLength, BeatDelay;
    private float NikitosStep, NikitosJump, DmitryStep, DmitryJump;
    
    public override void Render()
    {
        double time = GetTime() - TimeAppear;
        double state = ((time)
                % CreditsLength
                ) / CreditsLength;
        DrawTexturePro(Runtime.CurrentRuntime.Textures["staff_roll_background.png"],
            BgSource with { Y = (float)(state * 720) },
            BgTarget,
            Vector2.Zero, 0, Color.White);
        BeginTextureMode(NikitosJumpingTexture);
        ClearBackground(Color.White with {A=0});
        float x = 0;
        int j = 0;
        float state2 = 0;
        while (x < NikitosJumpingTexture.Texture.Width)
        {
            state2 = 1-Raymath.Clamp(Helper.Pow2F(MathF.Abs((float)(1-((time/BeatLength+j)%(2+BeatDelay))))),0,1);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["vilkaCut.png"],
                ForkImgSource, ForkImgTarget with { X = ForkImgTarget.X+x }, Vector2.Zero, 0, Color.White);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["nikitos_boss_art.png"],
                NiImgSource, NiImgTarget with { X = NiImgTarget.X+x,Y=NiImgTarget.Y+(state2*NikitosJump) }, Vector2.Zero, 0, Color.White);
            x += NikitosStep;
            j++;
        }
        EndTextureMode();
        BeginTextureMode(DmitryEatingTexture);
        ClearBackground(Color.White with {A=0});
        x = 0;
        j = 0;
        while (x < DmitryEatingTexture.Texture.Width)
        {
            state2 = 1-Raymath.Clamp(Helper.Pow2F(MathF.Abs((float)(1-((time/BeatLength+j)%(2+BeatDelay))))),0,1);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["object.png"],
                ObjectSource, ObjectTarget with { X = ObjectTarget.X+x, Height = (.5f * (1-Helper.Pow2F(state2)) + .5f) * ObjectTarget.Height }, Vector2.Zero, 0, Color.White);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["dima_top.png"],
                DTopSource, DTopTarget with { X = DTopTarget.X+x,Y=DTopTarget.Y - state2 * DmitryJump }, Vector2.Zero, MathF.Sin(state2) * -1, Color.White);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["dima_bottom.png"],
                DBottomSource, DBottomTarget with { X = DBottomTarget.X+x, Y = DBottomTarget.Y + state2 * DmitryJump }, Vector2.Zero, MathF.Cos(state2) * 1, Color.White);
            x += DmitryStep;
            j++;
        }
        EndTextureMode();
        #if DEBUG
        int k = 0;
        DrawText($"NikitosSource: {NSource}", 0, k+=20, 20, Color.White);
        DrawText($"NikitosTarget: {NTarget}", 0, k+=20, 20, Color.White);
        #endif
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "strength"), 6+MathF.Abs((float)Math.Sin(time % 2 - 1)), ShaderUniformDataType.Float);
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "opacity"), .25f, ShaderUniformDataType.Float);
        BeginShaderMode(Bloom);
        DrawTexturePro(NikitosJumpingTexture.Texture,
            NSource with { X = (float)(state * Runtime.CurrentRuntime.Width * 2 / 1.2) },
            NTarget, NOrigin,120, Color.White with { A = 64 } );
        EndShaderMode();
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "opacity"), .5f, ShaderUniformDataType.Float);
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "strength"), 3+MathF.Abs((float)Math.Tan(time % 2 - 1)), ShaderUniformDataType.Float);
        BeginShaderMode(Bloom);
        DrawTexturePro(DmitryEatingTexture.Texture,
            DSource with { X = (float)(state * Runtime.CurrentRuntime.Width * 2 / 1.2) },
            DTarget, DOrigin,-15, Color.White with { A = 128 } );
        EndShaderMode();
        base.Render();
    }
#if DEBUG
    public override void TopUpdate()
    {
        if (IsKeyDown(KeyboardKey.J) && GetTime() - TimeAppear > 1f)
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            Runtime.CurrentRuntime.AddScreen(new CreditsScreen());
            Unload();
        }
    }
#endif

    public override void Unload()
    {
        UnloadRenderTexture(NikitosJumpingTexture);
        UnloadRenderTexture(DmitryEatingTexture);
    }
}