using DmitryAndDemid.Rendering;
using System.Numerics;
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

public class TrophyScreen : ScreenWithTitle
{
    public TrophyScreen()
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        SetTitle(Runtime.CurrentRuntime.Textures["nickname.png"]);
        YFrom = Runtime.CurrentRuntime.Height/4;
        Load();
    }

    private TargetHandle[] Menu;
    private TargetHandle[] Description;
    private float ItemSwitchTime = 0;
    private float ItemTriggerTime = 0;
    public bool IsItemTriggered = false;
    public Action? Action = null;
    private int Index = 0;
    private int Columns = 0;
    private int YFrom = 0;

    private TrophyInfo[] Infos = [];

    void Load()
    {
        string[] files = Assets.Files("Assets/Data/Trophy", "*.json");
        Menu = new TargetHandle[files.Length];
        Description = new TargetHandle[files.Length];
        Infos = new TrophyInfo[files.Length];
        for (int i = 0; i < files.Length; i++)
        {
            // Each trophy JSON carries its own Index (and its unlock/action metadata); key everything on that,
            // not on file order, so the earned-check and the Enter action line up with how trophies are unlocked.
            TrophyInfo info;
            try { info = JsonSerializer.Deserialize<TrophyInfo>(System.IO.File.ReadAllText(files[i])) ?? new TrophyInfo(); }
            catch { info = new TrophyInfo { Index = i }; }
            Infos[i] = info;
            // Earned trophies show their number; still-locked ones are masked as **.
            string label = PlayerData.Instance.IsTrophyUnlocked(info.Index) ? $"{info.Index:00}" : "**";
            Menu[i] = Helper.DrawTextScaled(label,
                32, 8, 4, 2,
                Runtime.CurrentRuntime.Fonts["newsreader"],
                "gradient");
        }
        Columns = (int)Math.Sqrt(Menu.Length)+1;
        DisappearingTime = 1f;
    }

    /// <summary>
    /// Opens the ending / staff roll a trophy is tied to (only when it's earned). Ending trophies open their
    /// ending art; everything else (difficulty trophies, staff) opens a standalone credits roll. The trophy grid
    /// stays underneath, so closing the ending returns straight here.
    /// </summary>
    private void OpenTrophyAction(TrophyInfo info)
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
        if (info.ActionType == "ending")
        {
            string path = $"Assets/Data/Endings/{info.ActionInfo}.json";
            if (Assets.Exists(path))
            {
                try
                {
                    EndingInfo? ending = JsonSerializer.Deserialize<EndingInfo>(Assets.ReadAllText(path));
                    if (ending != null)
                    {
                        Runtime.CurrentRuntime.AddScreen(new EndingScreen(0, ending, showStaffRoll: false));
                        return;
                    }
                }
                catch { /* fall through to the staff roll */ }
            }
        }
        // "staff" trophies (and any ending that failed to load) show a standalone credits roll.
        Runtime.CurrentRuntime.AddScreen(new CreditsScreen());
    }
    
    /// <summary>How long one pass of the shine takes to cross the whole grid, and how much of that pass any one
    /// trophy spends lit. A long period and a narrow band: it should read as an occasional glint, not a strobe.
    /// </summary>
    private const float ShinePeriod = 5f, ShineWidth = 0.12f, ShineStagger = 0.045f;

    /// <summary>
    /// The travelling highlight's strength on trophy <paramref name="i"/>, 0 when it is unearned or the band is
    /// elsewhere. The per-index stagger is what turns a single pulse into something that sweeps.
    /// </summary>
    private float EarnedShine(int i, float time)
    {
        if (i >= Infos.Length || !PlayerData.Instance.IsTrophyUnlocked(Infos[i].Index))
            return 0f;
        float phase = (time / ShinePeriod - i * ShineStagger) % 1f;
        if (phase < 0f)
            phase += 1f;
        return phase < ShineWidth ? MathF.Sin(phase / ShineWidth * MathF.PI) : 0f;
    }

    public override void Render()
    {
        DrawBackground();
        DrawTitle();
        int row = 0, start = 0;
        Rect rc;
        float time = (float)GetTime();
        for (int i = 0; i < Menu.Length; i++)
        {
            rc = Helper.GetFullSource(Menu[0].Texture);
            if (i % Columns == 0)
            {
                start = (Runtime.CurrentRuntime.Width - Menu[0].Texture.Width * Math.Min(Menu.Length - i, Columns)) / 2;
                row = i / Columns;
            }
            float itemW = Menu[0].Texture.Width, itemH = Menu[0].Texture.Height;
            float baseX = start + (i % Columns) * itemW;
            float baseY = (YFrom) + (Runtime.CurrentRuntime.Height * (1-Helper.EaseInOutElasticF((float)Helper.ComputeObjectTime(time, TimeAppear + (.02f * i), 1, TimeDisappear + (.02f * i), 1)))) + (row) * itemH;
            Rect dest = rc with { X = baseX, Y = baseY };
            if (Index == i)
            {
                // Selected trophy: a persistent slight enlarge plus a shake burst right after a switch. The shake
                // fades out with `pop` so a stationary cursor sits still — no idle jitter — matching the main menu.
                float pop = (float)Math.Clamp(1 - (time - ItemSwitchTime) / 0.25f, 0, 1);
                float scale = 1.16f;
                float sf = Runtime.CurrentRuntime.ScaleF;
                Vector2 shake = new Vector2(MathF.Sin(time * 90f), MathF.Cos(time * 78f)) * 2.5f * sf * pop;
                float dw = itemW * (scale - 1f), dh = itemH * (scale - 1f);
                dest = new Rect(baseX - dw / 2f + shake.X, baseY - dh / 2f + shake.Y, itemW * scale, itemH * scale);
            }
            // Selected trophy highlights white; the rest stay a softer grey so the grid reads calm.
            Rgba tint = Index == i ? new Rgba(255, 255, 255, 255) : new Rgba(185, 185, 185, 255);
            // …and every so often a band of light sweeps across the grid, catching the EARNED trophies one after
            // another as it passes. The still-locked ones (drawn as **) are skipped entirely, so what the player
            // has actually collected picks itself out of the grid without the layout saying anything different.
            float shine = EarnedShine(i, time);
            if (shine > 0f)
            {
                tint = Helper.Mix(tint, new Rgba(255, 240, 170, 255), shine);
                // The catch is a lift as well as a brightening — a couple of pixels, pivoting on the tile's centre.
                dest = dest with { Y = dest.Y - shine * 3f * Runtime.CurrentRuntime.ScaleF };
            }
            DrawTexturePro(Menu[i].Texture, rc, dest, Vector2.Zero, 0, tint);
        }
        base.Render();
    }
    
    public override void TopUpdate()
    {
        
        float time = (float)GetTime();
        if (IsItemTriggered && time - ItemTriggerTime > MenuScreen.MenuActivateCooldown)
        {
            IsItemTriggered = false;
            Action?.Invoke();
            return;
        }
        if (time - ItemSwitchTime < MenuScreen.MenuSwitchCooldown)
            return;
        int indexDif = 0;
        if (IsKeyDown(KeyCode.Down))
            indexDif += Columns;
        if (IsKeyDown(KeyCode.Up))
            indexDif -= Columns;
        if (IsKeyDown(KeyCode.Left))
            indexDif -= 1;
        if (IsKeyDown(KeyCode.Right))
            indexDif += 1;
        if (Math.Abs(indexDif) > 0)
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            Index = (Index + indexDif + Menu.Length) % Menu.Length;
            ItemSwitchTime = time;
        }
        if (time - ItemSwitchTime < MenuScreen.MenuActivateCooldown)
            return;
        if (IsKeyDown(KeyCode.Enter) || IsKeyDown(KeyCode.Z))
        {
            // An earned trophy with an action opens its ending / staff roll (grid stays underneath); otherwise
            // Enter just leaves the screen, as before.
            TrophyInfo info = Index >= 0 && Index < Infos.Length ? Infos[Index] : new TrophyInfo();
            if (info.HasAction && PlayerData.Instance.IsTrophyUnlocked(info.Index))
            {
                ItemSwitchTime = time;
                OpenTrophyAction(info);
            }
            else
            {
                IsItemTriggered = true;
                Exiting();
                ItemSwitchTime = time;
                TimeDisappear = (float)GetTime() + DisappearingTime;
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
                ItemTriggerTime = time;
                Action = () => Runtime.CurrentRuntime.RemoveScreen(this);
            }
        }
        if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X))
        {
            IsItemTriggered = true;
            Exiting();
            TimeDisappear = (float)GetTime() + DisappearingTime;
            ItemSwitchTime = time;
            Action = () => Runtime.CurrentRuntime.RemoveScreen(this);
            ItemTriggerTime = time + DisappearingTime;
        }
    }
}
