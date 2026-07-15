using System.Numerics;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.RuntimeData;

/// <summary>
/// Plays a chapter's dialog: the pre-fight exchange between the player and the boss.
///
/// Everything it needs already existed and was simply never driven — <see cref="FileDialogInfo"/> lines in the
/// chapter (authored in the stage editor), the speech-cloud renderer in <see cref="Helper.DrawDialog"/>, and
/// the *_dialog_arts.png sheets, one 768x1024 frame per reaction.
///
/// A line stays up for <see cref="LineDuration"/> seconds on its own; pressing shoot moves to the next one
/// early. Shoot is taken on the press, not while it is held, so keeping the button down through a fight's
/// opening does not blow through the whole conversation.
/// </summary>
public class RuntimeDialog
{
    /// <summary>How long a line stays up on its own, in seconds.</summary>
    public const double LineDuration = 5.0;

    /// <summary>A press is ignored for this long after a line appears, so one press cannot skip two lines.</summary>
    private const double AdvanceCooldown = 0.15;

    /// <summary>The dialog art sheets are a horizontal strip of 768x1024 reaction frames.</summary>
    private const int FrameWidth = 768;
    private const int FrameHeight = 1024;

    private readonly Line[] Lines;
    private readonly GameBox Box;

    private int Index;
    private double Elapsed;
    private double LineElapsed;
    private double LastUpdate;
    private bool ShootWasDown;

    public bool Finished { get; private set; }

    /// <summary>The line on screen right now.</summary>
    private Line Current => Lines[Index];

    public RuntimeDialog(FileDialogInfo[] dialogs, ProtogonistData protogonist, GameBox box)
    {
        Box = box;
        Lines = dialogs.Select(d => new Line(d, protogonist)).ToArray();
        Finished = Lines.Length == 0;
        LastUpdate = Gfx.GetTime();
    }

    /// <summary>
    /// Advances the dialog. Driven from GameBox's update, which stops simulating while a dialog is up — hence
    /// the wall clock here rather than the tick counter, and the clamp: after a pause the gap since the last
    /// call is arbitrarily large and must not be counted against the line's five seconds.
    /// </summary>
    public void Update()
    {
        if (Finished)
            return;

        double now = Gfx.GetTime();
        double delta = Math.Clamp(now - LastUpdate, 0, 0.1);
        LastUpdate = now;
        Elapsed += delta;
        LineElapsed += delta;

        bool shootDown = IsKeyDown(KeyCode.Z) || Controller.IsButtonDown(Configuration.Config.ShootButton)
                                              || TouchControls.IsDragging;
        bool pressed = shootDown && !ShootWasDown && LineElapsed > AdvanceCooldown;
        ShootWasDown = shootDown;

        if (pressed || LineElapsed >= LineDuration)
            Next();
    }

    private void Next()
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["dialogue"]);
        Index++;
        LineElapsed = 0;
        if (Index >= Lines.Length)
        {
            Index = Lines.Length - 1;
            Finished = true;
        }
    }

    /// <summary>
    /// Draws the current line into the target the caller has already begun — the portraits at the bottom of
    /// the playfield, speaker lit and the other one dimmed, with the speech cloud above them.
    /// </summary>
    public void Draw(TargetHandle target)
    {
        if (Finished || Lines.Length == 0)
            return;

        float scale = Runtime.CurrentRuntime.ScaleF;
        float width = target.Texture.Width;
        float height = target.Texture.Height;

        // Portraits stand on the bottom edge, player on the left, boss on the right, each turned inward.
        float artHeight = height * 0.62f;
        float artWidth = artHeight * FrameWidth / FrameHeight;
        float artY = height - artHeight;

        DrawPortrait(Current.PlayerArt, Current.PlayerFrame,
            new Rect(-artWidth * 0.12f, artY, artWidth, artHeight), Current.IsPlayer, false);
        DrawPortrait(Current.BossArt, Current.BossFrame,
            new Rect(width - artWidth * 0.88f, artY, artWidth, artHeight), !Current.IsPlayer, true);

        // The cloud's tail already points at its speaker (Helper.DrawDialog picks the angle), so the bubble
        // only has to sit on that speaker's side.
        TextureHandle cloud = Current.Cloud.Texture;
        float cloudWidth = Math.Min(cloud.Width, width * 0.8f);
        float cloudHeight = cloudWidth * cloud.Height / cloud.Width;
        float cloudX = Current.IsPlayer ? width * 0.06f : width - cloudWidth - width * 0.06f;
        float cloudY = height * 0.5f - cloudHeight * 0.5f - 16 * scale;

        DrawTexturePro(cloud,
            new Rect(0, 0, cloud.Width, -cloud.Height),
            new Rect(cloudX, cloudY, cloudWidth, cloudHeight),
            Vector2.Zero, 0, Rgba.White);
    }

    private static void DrawPortrait(TextureHandle? art, int frame, Rect destination, bool speaking, bool flip)
    {
        if (art == null)
            return;

        int frames = Math.Max(1, art.Value.Width / FrameWidth);
        Rect source = new(Math.Clamp(frame, 0, frames - 1) * FrameWidth, 0,
            flip ? -FrameWidth : FrameWidth, FrameHeight);

        // The listener stays on screen but recedes: dimmed, so it is obvious who is talking.
        Rgba tint = speaking ? Rgba.White : new Rgba(128, 128, 148, 220);
        DrawTexturePro(art.Value, source, destination, Vector2.Zero, 0, tint);
    }

    public void Unload()
    {
        foreach (Line line in Lines)
            line.Unload();
    }

    /// <summary>One authored line, with its speech cloud rendered once up front.</summary>
    private class Line
    {
        public readonly bool IsPlayer;
        public readonly TargetHandle Cloud;
        public readonly TextureHandle? PlayerArt;
        public readonly TextureHandle? BossArt;
        public readonly int PlayerFrame;
        public readonly int BossFrame;

        public Line(FileDialogInfo info, ProtogonistData protogonist)
        {
            IsPlayer = info.IsPlayerDialog;

            // Same tail angles the old RuntimeDialogElement used: down-right for the boss on the right,
            // down-left for the player on the left.
            Cloud = Helper.DrawDialog(info.Text, IsPlayer ? 2.34f : 0.79f);

            PlayerArt = Lookup(protogonist.DialogArtName);
            BossArt = Lookup(info.CharacterTexture);

            // Header[2] is the reaction frame, and it only applies to whoever is speaking.
            int reaction = info.SwitchReaction ? info.Header[2] : 0;
            PlayerFrame = IsPlayer ? reaction : 0;
            BossFrame = IsPlayer ? 0 : reaction;

            // Header[3] would switch the track here, but the game has no music playback yet
            // (Helper.UpdatePlayingMusic throws NotImplementedException), so SwitchMusic is carried in the
            // data and ignored at runtime.
        }

        static TextureHandle? Lookup(string name) =>
            !string.IsNullOrEmpty(name) && Runtime.CurrentRuntime.Textures.TryGetValue(name, out TextureHandle t)
                ? t
                : null;

        public void Unload() => UnloadRenderTexture(Cloud);
    }
}
