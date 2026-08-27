using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

public class CreditsScreen : Screen
{
    private readonly GameplayScreen? ClearedRun;

    public CreditsScreen(GameplayScreen? clearedRun = null)
    {
        // Staff-roll art is its own texture group ("staff"); object.png below is read straight from the
        // dictionary in this constructor, so the group has to be in before anything else runs.
        Runtime.CurrentRuntime.LoadTextureGroup("staff");
        ClearedRun = clearedRun;
        PlayerData.Instance.SetMusicUnlocked(10, true);   // staff-roll theme unlocked in the music room
        BgTarget = Helper.GetFullscreenSource();
        NikitosJumpingTexture = LoadRenderTexture(Runtime.CurrentRuntime.Width * 4, (int)(Runtime.CurrentRuntime.Height * .75f));
        DmitryEatingTexture = LoadRenderTexture(Runtime.CurrentRuntime.Width * 4, (int)(Runtime.CurrentRuntime.Height * .75f));
        NSource = new Rect(0,0,Runtime.CurrentRuntime.Width*1.5f,Runtime.CurrentRuntime.Height*.75f);
        DSource = new Rect(0,0,Runtime.CurrentRuntime.Width*1.5f,Runtime.CurrentRuntime.Height*.75f);
        BeatLength = 60d / BPM;
        BeatDelay = BeatLength * (BeatAnimateRate - 1);
        DTarget = new Rect(BgTarget.Size * new Vector2(1, .3f) - NSource.Size / 2, NSource.Size);
        NTarget = new Rect(BgTarget.Size * new Vector2(2f, 1f) - DSource.Size / 2, NSource.Size);
        ForkImgSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["vilkaCut.png"]);
        ForkImgTarget = new Rect(0, 0,
            ForkImgSource.Size * .75f * NTarget.Height / ForkImgSource.Height
        );
        ForkImgTarget.X = -ForkImgTarget.Width / 2;
        ForkImgTarget.Y = NSource.Height / 2;
        NiImgSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["nikitos_boss_art.png"]);
        NiImgTarget = new Rect(0, 0,
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
        DTopTarget = new Rect(0, 0,
            DTopSource.Size * .75f * DTarget.Height / DTopSource.Height
        );
        DBottomTarget = new Rect(0, 0,
            DBottomSource.Size * .75f * DTarget.Height / DBottomSource.Height
        );
        DBottomTarget.Y = DTarget.Height - DBottomTarget.Height;
        ObjectSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["object.png"]);
        ObjectTarget = new Rect(Vector2.Zero, ObjectSource.Size / 2);
        ObjectTarget.X = -ObjectTarget.Width / 2;
        DTopTarget.X = -DTopTarget.Width / 2;
        DBottomTarget.X = -DBottomTarget.Width / 2;
        ObjectTarget.Y = (DTarget.Height - ObjectTarget.Height) / 1.2f;
        DmitryStep = 1.2f * DTopTarget.Width;
        DmitryJump = 0.15f * DSource.Height;
        Bloom = Runtime.CurrentRuntime.Shaders["bloom_ending"];
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "resolution"), NSource.Size, UniformType.Vec2);

        var lines = new List<(string, bool)>();
        foreach ((string roleKey, string[] names) in Roll)
        {
            lines.Add((Helper.Translate(roleKey), true));
            foreach (string name in names)
                lines.Add((name, false));
        }
        RollLines = lines.ToArray();
    }

    private ShaderHandle Bloom;
    
    private const double
        CreditsLength = 30, CreditsFade = 3, CreditsDecoractionsStart = 6, CreditsDecorationsFade = 0.5;

    // The credit block crawls between these two moments of the roll: it starts below the bottom edge and is off
    // the top again before the screen fades out, so the names are never caught halfway by the fade.
    private const double
        CreditsRollStart = 4, CreditsRollEnd = 27;

    /// <summary>
    /// Who the staff roll credits, in the order it rolls them. The role is a translation.json key; the names
    /// under it are written the way their owners sign them, so they are not translated or transliterated.
    /// </summary>
    private static readonly (string RoleKey, string[] Names)[] Roll =
    [
        ("credits.creators", ["AKOB", "QAW"]),
    ];

    /// <summary>The roll flattened to drawable lines. Built once: <see cref="Helper.Translate"/> picks at random
    /// between the ";"-separated variants of a key, so calling it per frame would reshuffle the wording.</summary>
    private readonly (string Text, bool IsRole)[] RollLines;

    private const int
        BPM = 120, BeatAnimateRate = 8;
    
    private RenderedTexture 
        NikitosJumpingTexture, DmitryEatingTexture;

    private Rect
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
        double time = (GetTime() - TimeAppear) 
                      #if DEBUG
                      % CreditsLength
                      #endif
                      ;
        double state = time / CreditsLength;
        double fade = Math.Clamp(((CreditsLength /2) - Math.Abs(time - (CreditsLength/2))) / CreditsFade, 0, 1);
        float decorationsFade = (float)Math.Clamp((time - CreditsDecoractionsStart) / CreditsDecorationsFade, 0, 1);
        
        DrawTexturePro(Runtime.CurrentRuntime.Textures["staff_roll_background.png"],
            BgSource with { Y = (float)(state * 720) },
            BgTarget,
            Vector2.Zero, 0, Rgba.White);
        BeginTextureMode(NikitosJumpingTexture);
        ClearBackground(Rgba.White with {A=0});
        float x = 0;
        int j = 0;
        float state2 = 0;
        while (x < NikitosJumpingTexture.Texture.Width)
        {
            state2 = 1-MathUtil.Clamp(Helper.Pow2F(MathF.Abs((float)(1-((time/BeatLength+j)%(2+BeatDelay))))),0,1);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["vilkaCut.png"],
                ForkImgSource, ForkImgTarget with { X = ForkImgTarget.X+x }, Vector2.Zero, 0, Rgba.White);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["nikitos_boss_art.png"],
                NiImgSource, NiImgTarget with { X = NiImgTarget.X+x,Y=NiImgTarget.Y+(state2*NikitosJump) }, Vector2.Zero, 0, Rgba.White);
            x += NikitosStep;
            j++;
        }
        EndTextureMode();
        BeginTextureMode(DmitryEatingTexture);
        ClearBackground(Rgba.White with {A=0});
        x = 0;
        j = 0;
        while (x < DmitryEatingTexture.Texture.Width)
        {
            state2 = 1-MathUtil.Clamp(Helper.Pow2F(MathF.Abs((float)(1-((time/BeatLength+j)%(2+BeatDelay))))),0,1);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["object.png"],
                ObjectSource, ObjectTarget with { X = ObjectTarget.X+x, Height = (.5f * (1-Helper.Pow2F(state2)) + .5f) * ObjectTarget.Height }, Vector2.Zero, 0, Rgba.White);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["dima_top.png"],
                DTopSource, DTopTarget with { X = DTopTarget.X+x,Y=DTopTarget.Y - state2 * DmitryJump }, Vector2.Zero, MathF.Sin(state2) * -1, Rgba.White);
            DrawTexturePro(Runtime.CurrentRuntime.Textures["dima_bottom.png"],
                DBottomSource, DBottomTarget with { X = DBottomTarget.X+x, Y = DBottomTarget.Y + state2 * DmitryJump }, Vector2.Zero, MathF.Cos(state2) * 1, Rgba.White);
            x += DmitryStep;
            j++;
        }
        EndTextureMode();
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "strength"), 6+MathF.Abs((float)Math.Sin(time % 2 - 1)), UniformType.Float);
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "opacity"), .25f * decorationsFade, UniformType.Float);
        BeginShaderMode(Bloom);
        DrawTexturePro(NikitosJumpingTexture.Texture,
            NSource with { X = (float)(state * Runtime.CurrentRuntime.Width * 2 / 1.2) },
            NTarget, NOrigin,120, Rgba.White with { A = 64 } );
        EndShaderMode();
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "opacity"), .5f * decorationsFade, UniformType.Float);
        SetShaderValue(Bloom, GetShaderLocation(Bloom, "strength"), 3+MathF.Abs((float)Math.Tan(time % 2 - 1)), UniformType.Float);
        BeginShaderMode(Bloom);
        DrawTexturePro(DmitryEatingTexture.Texture,
            DSource with { X = (float)(state * Runtime.CurrentRuntime.Width * 2 / 1.2) },
            DTarget, DOrigin,-15, Rgba.White with { A = 128 } );
        EndShaderMode();
        DrawRoll(time);
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width,Runtime.CurrentRuntime.Height, Rgba.Black with {A=Helper.TimeToTransparency(1-fade)});
#if DEBUG
        int k = 0;
        DrawText($"NikitosSource: {NSource}", 0, k+=20, 20, Rgba.White);
        DrawText($"NikitosTarget: {NTarget}", 0, k+=20, 20, Rgba.White);
        DrawText($"Time: {time}", 0, k+=20, 20, Rgba.White);
        DrawText($"Fade: {fade}", 0, k+=20, 20, Rgba.White);
        DrawText($"Decorations Fade: {decorationsFade}", 0, k+=20, 20, Rgba.White);
#endif
        base.Render();
    }

    /// <summary>The colour the role headings are set in — the same warm gold the music room warns in.</summary>
    private static readonly Rgba RoleColor = new(255, 200, 80, 255);

    /// <summary>
    /// The credit block, crawling up the middle of the roll. It is drawn straight to the screen with a
    /// hand-stamped outline (the way the music room draws its descriptions): the background under it is a busy
    /// scrolling picture, and unoutlined text on top of that does not read. Drawn BEFORE the fade rectangle, so
    /// the names fade in and out with the rest of the roll instead of sitting on top of the fade.
    /// </summary>
    private void DrawRoll(double time)
    {
        if (RollLines.Length == 0)
            return;
        float sf = Runtime.CurrentRuntime.ScaleF;
        var roleFont = Runtime.CurrentRuntime.Fonts["notoseriflight"];
        var nameFont = Runtime.CurrentRuntime.Fonts["googlesans"];
        const float Spacing = 2;
        float roleSize = 22 * sf, nameSize = 40 * sf, lineGap = 10 * sf, sectionGap = 40 * sf;

        // The gap ABOVE a line: none for the first, a wide one before each new role, a tight one between names.
        float Leading(int i) => i == 0 ? 0f : RollLines[i].IsRole ? sectionGap : lineGap;
        Vector2 Measure(int i) => RollLines[i].IsRole
            ? MeasureTextEx(roleFont, RollLines[i].Text, roleSize, Spacing)
            : MeasureTextEx(nameFont, RollLines[i].Text, nameSize, Spacing);

        float blockHeight = 0;
        for (int i = 0; i < RollLines.Length; i++)
            blockHeight += Leading(i) + Measure(i).Y;

        // Constant crawl from just under the bottom edge to just past the top over the roll's middle stretch.
        double progress = Math.Clamp((time - CreditsRollStart) / (CreditsRollEnd - CreditsRollStart), 0, 1);
        float y = Runtime.CurrentRuntime.Height - (float)progress * (Runtime.CurrentRuntime.Height + blockHeight);

        for (int i = 0; i < RollLines.Length; i++)
        {
            (string text, bool isRole) = RollLines[i];
            Vector2 size = Measure(i);
            y += Leading(i);
            Helper.DrawTextOutlined(isRole ? roleFont : nameFont, text,
                new Vector2((Runtime.CurrentRuntime.Width - size.X) / 2f, y),
                isRole ? roleSize : nameSize, Spacing,
                isRole ? RoleColor : Rgba.White, new Rgba(0, 0, 0, 217), MathF.Max(1f, 1.5f * sf));
            y += size.Y;
        }
    }

    public override void TopUpdate()
    {
        // The staff roll is the last thing in a clear; let the player leave it back to the main menu once it has
        // been up long enough to not swallow the button press that started it.
        if ((IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X) || IsKeyDown(KeyCode.Enter) || IsKeyDown(KeyCode.Z))
            && GetTime() - TimeAppear > 1f)
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            // After the staff roll of a real clear, go to the results (name entry); a bare credits view (editor
            // preview / debug restart) just returns to wherever it came from.
            if (ClearedRun != null)
                Runtime.CurrentRuntime.AddScreen(new ResultsScreen(ClearedRun));
            return;
        }
#if DEBUG
        if (IsKeyDown(KeyCode.J) && GetTime() - TimeAppear > 1f)
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            Runtime.CurrentRuntime.AddScreen(new CreditsScreen());
            Unload();
        }
#endif
    }

    public override void Unload()
    {
        UnloadRenderTexture(NikitosJumpingTexture);
        UnloadRenderTexture(DmitryEatingTexture);
    }
}