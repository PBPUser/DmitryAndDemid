using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Backgrounds;
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
        // The flight passes everyone who made the game, and their art lives in the character sets rather than
        // in the staff one. MainScreen frees these again when the title screen comes back.
        foreach (string group in ParisChelyabinskBackground.ArtGroups)
            Runtime.CurrentRuntime.LoadTextureGroup(group);
        ClearedRun = clearedRun;
        PlayerData.Instance.SetMusicUnlocked(10, true);   // staff-roll theme unlocked in the music room
        BgTarget = Helper.GetFullscreenSource();
        Backdrop = new ParisChelyabinskBackground();
        var lines = new List<(string, bool)>();
        foreach ((string roleKey, string[] names) in Roll)
        {
            lines.Add((Helper.Translate(roleKey), true));
            foreach (string name in names)
                lines.Add((name, false));
        }
        RollLines = lines.ToArray();
    }

    /// <summary>Parizh, Chelyabinsk, raymarched behind the roll — see <see cref="ParisChelyabinskBackground"/>.
    /// It replaced a scrolling still (staff_roll_background.png), which is why the old BgSource crawl is gone:
    /// the camera does the moving now.</summary>
    private readonly ParisChelyabinskBackground Backdrop;
    
    private const double CreditsLength = ParisChelyabinskBackground.Duration, CreditsFade = 3;

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

        ("credits.creators", ["AKOB", "QAW", "UngMan"]),
    ];

    /// <summary>The roll flattened to drawable lines. Built once: <see cref="Helper.Translate"/> picks at random
    /// between the ";"-separated variants of a key, so calling it per frame would reshuffle the wording.</summary>
    private readonly (string Text, bool IsRole)[] RollLines;

    /// <summary>The whole screen, which the flight is drawn over.</summary>
    private Rect BgTarget;
    
    public override void Render()
    {
        double time = (GetTime() - TimeAppear) 
                      #if DEBUG
                      % CreditsLength
                      #endif
                      ;
        double state = time / CreditsLength;
        double fade = Math.Clamp(((CreditsLength /2) - Math.Abs(time - (CreditsLength/2))) / CreditsFade, 0, 1);
        
        Backdrop.Render(BgTarget, time);
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width,Runtime.CurrentRuntime.Height, Rgba.Black with {A=Helper.TimeToTransparency(1-fade)});
#if DEBUG
        int k = 0;
        DrawText($"Time: {time}", 0, k+=20, 20, Rgba.White);
        DrawText($"Fade: {fade}", 0, k+=20, 20, Rgba.White);
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
        Backdrop.Dispose();
    }
}