using System.Numerics;
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// The per-character score board (reached from the main menu's "stats" entry). Shows the selected character's
/// best-ten scores for the selected difficulty, plus games played / wins / total time at the bottom middle.
/// The character is switched with Left/Right (the two pulsing red triangles that flank the character name in the
/// bottom-left of the title), and the difficulty with Up/Down (a matching pair of triangles on the right).
///
/// Layout positions are authored in the game's 640x480 space (scaled up by ScaleF) and are a best-effort first
/// pass — they are the numbers most likely to want tuning on a real screen.
/// </summary>
public class ScoreScreen : ScreenWithTitle
{
    private static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard", "Max", "Extra" };

    // Triangle orientations for the pulse_triangle shader's "dir" uniform.
    private const float DirRight = 0f, DirLeft = 1f, DirUp = 2f, DirDown = 3f;

    private string[] CharacterNames = [];   // display names (person file names)
    private string[] CharacterIds = [];     // PlayerData keys (ProtogonistData.ID)
    private int CharacterIndex = 0;
    private int DifficultyIndex = 0;

    private ShaderHandle TriangleShader;
    private int LocTriTime, LocTriDir;
    private TextureHandle QuadTexture;      // any texture; the shader ignores its colour and draws the triangle
    private Rect QuadSource;
    private FontHandle Font, NameFont;

    private double LastInput = 0;
    private const double InputCooldown = 0.14;
    // When the character / difficulty selector last changed, for its shake-and-enlarge pop.
    private double CharSwitchTime = -1, DiffSwitchTime = -1;

    private readonly PlayerData.PersonPlayerData Empty = new();   // shown for a character that has never been played

    public ScoreScreen()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["score-title.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    protected override void Created()
    {
        Font = Runtime.CurrentRuntime.Fonts["kodemono"];
        NameFont = Runtime.CurrentRuntime.Fonts["newsreader"];
        TriangleShader = Runtime.CurrentRuntime.Shaders["pulse_triangle"];
        LocTriTime = GetShaderLocation(TriangleShader, "time");
        LocTriDir = GetShaderLocation(TriangleShader, "dir");
        QuadTexture = Runtime.CurrentRuntime.Textures["score-title.png"];
        QuadSource = Helper.GetFullSource(QuadTexture);

        string[] files = Assets.Files("Assets/Data/PlayablePersons/", "*.json");
        CharacterNames = new string[files.Length];
        CharacterIds = new string[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            CharacterNames[i] = Path.GetFileNameWithoutExtension(files[i]);
            try
            {
                ProtogonistData? data = JsonSerializer.Deserialize<ProtogonistData>(File.ReadAllText(files[i]));
                CharacterIds[i] = data?.ID ?? CharacterNames[i];
            }
            catch
            {
                CharacterIds[i] = CharacterNames[i];
            }
        }
    }

    private float S(float v) => v * Runtime.CurrentRuntime.ScaleF;

    public override void TopUpdate()
    {
        double now = GetTime();
        if (now - LastInput < InputCooldown)
            return;

        if (CharacterNames.Length > 0 && (IsKeyDown(KeyCode.Left) || Controller.IsButtonDown(PadButton.LeftFaceLeft)))
            StepCharacter(-1, now);
        else if (CharacterNames.Length > 0 && (IsKeyDown(KeyCode.Right) || Controller.IsButtonDown(PadButton.LeftFaceRight)))
            StepCharacter(1, now);
        else if (IsKeyDown(KeyCode.Up) || Controller.IsButtonDown(PadButton.LeftFaceUp))
            StepDifficulty(-1, now);
        else if (IsKeyDown(KeyCode.Down) || Controller.IsButtonDown(PadButton.LeftFaceDown))
            StepDifficulty(1, now);
        else if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X) || Controller.IsButtonDown(PadButton.RightFaceRight))
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
            Exiting();
            Runtime.CurrentRuntime.RemoveScreen(this);
        }
    }

    private void StepCharacter(int d, double now)
    {
        LastInput = now;
        CharSwitchTime = now;
        CharacterIndex = (CharacterIndex + d + CharacterNames.Length) % CharacterNames.Length;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
    }

    private void StepDifficulty(int d, double now)
    {
        LastInput = now;
        DiffSwitchTime = now;
        DifficultyIndex = Math.Clamp(DifficultyIndex + d, 0, PlayerData.PersonPlayerData.DifficultyCount - 1);
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
    }

    /// <summary>A 1→0 pop factor decaying over a quarter-second after a selector last changed.</summary>
    private float Pop(double switchTime) =>
        switchTime < 0 ? 0f : (float)Math.Clamp(1 - (GetTime() - switchTime) / 0.25, 0, 1);

    private PlayerData.PersonPlayerData CurrentData =>
        CharacterIds.Length > 0 ? PlayerData.Instance.GetPerson(CharacterIds[CharacterIndex]) ?? Empty : Empty;

    private static string FormatTime(long seconds)
    {
        long h = seconds / 3600, m = (seconds % 3600) / 60, s = seconds % 60;
        return $"{h:D2}:{m:D2}:{s:D2}";
    }

    /// <summary>An integer date packed as YYYYMMDD, shown as YY.MM.DD; 0/-1 (never set) reads as dashes.</summary>
    private static string FormatDate(int date)
    {
        if (date <= 0)
            return "--.--.--";
        int y = date / 10000, m = (date / 100) % 100, d = date % 100;
        return $"{y % 100:D2}.{m:D2}.{d:D2}";
    }

    /// <summary>Draws text with a dark drop-shadow behind it so the readouts stay legible over the busy
    /// background — the "shading" used throughout this screen.</summary>
    private void DrawShaded(FontHandle font, string text, Vector2 pos, float size, Rgba color)
    {
        Vector2 shadow = new(S(1.5f), S(1.5f));
        DrawTextEx(font, text, pos + shadow, size, 1, Rgba.Black with { A = 180 });
        DrawTextEx(font, text, pos, size, 1, color);
    }

    /// <summary>Draws text with a drop-shadow, right-aligned so its right edge sits at <paramref name="right"/>.</summary>
    private void DrawShadedRight(FontHandle font, string text, float right, float y, float size, Rgba color)
    {
        float w = MeasureTextEx(font, text, size, 1).X;
        DrawShaded(font, text, new Vector2(right - w, y), size, color);
    }

    /// <summary>Draws one pulsing red triangle (via the shader) filling <paramref name="dest"/>, oriented by dir.</summary>
    private void DrawTriangle(Rect dest, float dir)
    {
        SetShaderValue(TriangleShader, LocTriTime, (float)GetTime(), UniformType.Float);
        SetShaderValue(TriangleShader, LocTriDir, dir, UniformType.Float);
        BeginShaderMode(TriangleShader);
        DrawTexturePro(QuadTexture, QuadSource, dest, Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
    }

    public override void Render()
    {
        DrawBackground();
        DrawTitle();

        DrawScoreBoard();
        DrawStats();
        DrawCharacterSelector();
        DrawDifficultySelector();
    }

    private void DrawScoreBoard()
    {
        PlayerData.PersonPlayerData data = CurrentData;
        PlayerData.PersonPlayerScoreRecord[] board = data.ScoreRecords[DifficultyIndex];
        float x = S(80), y = S(148), rowH = S(25), fontSize = S(15);
        float dateRight = S(400), scoreRight = S(560);
        for (int i = 0; i < board.Length; i++)
        {
            var rec = board[i];
            float ry = y + i * rowH;
            DrawShaded(Font, $"{i + 1,2}", new Vector2(x, ry), fontSize, Rgba.Yellow);
            DrawShaded(NameFont, rec.Name, new Vector2(x + S(26), ry), fontSize, Rgba.White);
            DrawShadedRight(Font, FormatDate(rec.Date), dateRight, ry, fontSize, Rgba.White with { A = 200 });
            DrawShadedRight(Font, rec.Score.ToString(), scoreRight, ry, fontSize, Rgba.White);
        }
    }

    private void DrawStats()
    {
        PlayerData.PersonPlayerData data = CurrentData;
        float fontSize = S(16), lineH = S(20);
        // Stacked vertically and centre-aligned along the bottom.
        string[] lines =
        {
            $"Games   {data.GamesPlayed}",
            $"Wins    {data.Wins}",
            $"Time    {FormatTime(data.TimeSpentSeconds)}",
        };
        float y = S(408);
        for (int i = 0; i < lines.Length; i++)
        {
            float w = MeasureTextEx(Font, lines[i], fontSize, 1).X;
            DrawShaded(Font, lines[i], new Vector2((Runtime.CurrentRuntime.Width - w) / 2f, y + i * lineH), fontSize, Rgba.White);
        }
    }

    private void DrawCharacterSelector()
    {
        if (CharacterNames.Length == 0)
            return;
        // Bottom-left of the title (title is 640x135). Two triangles flanking the character name.
        string name = CharacterNames[CharacterIndex];
        float pop = Pop(CharSwitchTime);
        float t = (float)GetTime();
        float fontSize = S(16) * (1f + 0.25f * pop);                       // enlarges on switch
        Vector2 shake = new Vector2(MathF.Sin(t * 80f), MathF.Cos(t * 70f)) * S(2) * pop;   // and jitters
        float triW = S(14), triH = S(18), y = S(110);
        float leftX = S(10);
        float nameX = leftX + triW + S(6);
        float nameW = MeasureTextEx(NameFont, name, fontSize, 1).X;
        float rightX = nameX + nameW + S(6);

        DrawTriangle(new Rect(leftX, y, triW, triH), DirLeft);
        DrawShaded(NameFont, name, new Vector2(nameX, y) + shake, fontSize, Rgba.White);
        DrawTriangle(new Rect(rightX, y, triW, triH), DirRight);
    }

    private void DrawDifficultySelector()
    {
        // Right side, same look as the character selector but stacked vertically, switched by Up/Down.
        string name = DifficultyNames[DifficultyIndex];
        float pop = Pop(DiffSwitchTime);
        float t = (float)GetTime();
        float fontSize = S(16) * (1f + 0.25f * pop);
        Vector2 shake = new Vector2(MathF.Sin(t * 80f), MathF.Cos(t * 70f)) * S(2) * pop;
        float triW = S(22), triH = S(14);
        float centerX = S(578);
        float midY = S(130);   // moved up (was S(200)), so it sits near the top by the character selector
        float nameW = MeasureTextEx(NameFont, name, fontSize, 1).X;

        DrawTriangle(new Rect(centerX - triW / 2, midY - triH - S(20), triW, triH), DirUp);
        DrawShaded(NameFont, name, new Vector2(centerX - nameW / 2, midY - S(8)) + shake, fontSize, Rgba.White);
        DrawTriangle(new Rect(centerX - triW / 2, midY + S(20), triW, triH), DirDown);
    }
}
