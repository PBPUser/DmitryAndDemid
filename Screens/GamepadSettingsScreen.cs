using DmitryAndDemid.Rendering;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using DmitryAndDemid.Utils.DualSense;
using static DmitryAndDemid.Rendering.Gfx;
#if DEBUG
using static ImGuiNET.ImGui;
#endif

namespace DmitryAndDemid.Screens;

public class GamepadSettingsScreen : MenuScreen
{
    /// <summary>
    /// The rebinding rows, paired with where the button they capture is stored. Held by reference (not by index)
    /// because the DualSense rows below are only present when a DualSense is plugged in, so a fixed
    /// "index &lt; 5 means a binding row" test — which is what this screen used to do — reads the wrong rows the
    /// moment the list changes shape.
    /// </summary>
    private readonly List<(MenuItem Item, Action<PadButton> Assign)> BindingRows = new();

    /// <summary>Rumble scale presets the row cycles through.</summary>
    private static readonly float[] RumbleStrengths = [0f, 0.25f, 0.5f, 0.75f, 1f];

    public GamepadSettingsScreen()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["gamepad_settings.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        CurrentY = Runtime.CurrentRuntime.Height / 2;
    }

    public override void CreateMenu()
    {
        EnableScrolling = true;   // with the DualSense rows this list outgrows the screen

        AddBinding("controller.bomb", () => Configuration.Config.BombButton, b => Configuration.Config.BombButton = b);
        AddBinding("controller.shoot", () => Configuration.Config.ShootButton, b => Configuration.Config.ShootButton = b);
        AddBinding("controller.jump", () => Configuration.Config.JumpButton, b => Configuration.Config.JumpButton = b);
        AddBinding("controller.focus", () => Configuration.Config.FocusButton, b => Configuration.Config.FocusButton = b);
        AddBinding("controller.pause", () => Configuration.Config.PauseButton, b => Configuration.Config.PauseButton = b);

        if (DualSensePad.IsConnected)
            AddDualSenseRows();

        MenuItems.Add(new MenuItem("settings.default", "", i =>
        {
            var defaults = new Configuration();
            Configuration.Config.JumpButton = defaults.JumpButton;
            Configuration.Config.PauseButton = defaults.PauseButton;
            Configuration.Config.FocusButton = defaults.FocusButton;
            Configuration.Config.ShootButton = defaults.ShootButton;
            Configuration.Config.BombButton = defaults.BombButton;
            Configuration.Config.GamepadProfile = "";
            Configuration.Config.Save();
            RefreshBindingLabels();
        }));
        MenuItems.Add(new MenuItem("controller.back", "", i => Exit()));
        base.CreateMenu();
    }

    private void AddBinding(string key, Func<PadButton> read, Action<PadButton> assign)
    {
        var item = new MenuItem(key, PadButtonNames.Describe(read()), i => { });
        MenuItems.Add(item);
        BindingRows.Add((item, assign));
    }

    /// <summary>
    /// The rows that only mean anything with a DualSense in hand. Rumble works for any user; the lightbar and the
    /// adaptive triggers need permission on the pad's device nodes, so a row whose hardware is out of reach says
    /// so instead of pretending to toggle something.
    /// </summary>
    private void AddDualSenseRows()
    {
        MenuItems.Add(new MenuItem("dualsense.status", DualSensePad.StatusLine(), null) { Enabled = false });

        AddToggle("dualsense.rumble", DualSensePad.RumbleAvailable,
            () => Configuration.Config.DualSenseRumble,
            v => Configuration.Config.DualSenseRumble = v,
            // Play the new setting rather than describe it — the pad is in the player's hands right now.
            () => DualSenseFeedbackTestPulse());

        MenuItem strength = new("dualsense.rumble_strength", StrengthLabel(), null);
        strength.Action = i =>
        {
            int next = Array.FindIndex(RumbleStrengths,
                s => s > Configuration.Config.DualSenseRumbleStrength + 0.01f);
            Configuration.Config.DualSenseRumbleStrength = next < 0 ? RumbleStrengths[0] : RumbleStrengths[next];
            Configuration.Config.Save();
            strength.Replace = StrengthLabel();
            DualSenseFeedbackTestPulse();
        };
        MenuItems.Add(strength);

        AddToggle("dualsense.lightbar", DualSensePad.LightsAvailable,
            () => Configuration.Config.DualSenseLightbar,
            v => Configuration.Config.DualSenseLightbar = v);

        AddToggle("dualsense.triggers", DualSensePad.TriggersAvailable,
            () => Configuration.Config.DualSenseTriggers,
            v => Configuration.Config.DualSenseTriggers = v);

        MenuItems.Add(new MenuItem("controller.dualsense_defaults", "", i =>
        {
            Configuration.Config.ApplyDualSenseDefaults();
            RefreshBindingLabels();
        }));
    }

    private void AddToggle(string key, bool available, Func<bool> read, Action<bool> write, Action? onChanged = null)
    {
        // Both the label and the value are translation KEYS — the menu translates each when it draws the row,
        // so "True"/"False" and the unavailable notice come out in the game's own voice.
        MenuItem item = new(key, available ? $"{read()}" : "dualsense.unavailable", null);
        if (!available)
        {
            // The hardware is out of reach (no permission on the pad's device nodes); a toggle that could not
            // change anything would just be a lie, so the row is inert and says why.
            item.Enabled = false;
            MenuItems.Add(item);
            return;
        }
        item.Action = i =>
        {
            write(!read());
            Configuration.Config.Save();
            item.Replace = $"{read()}";
            if (read())
                onChanged?.Invoke();
        };
        MenuItems.Add(item);
    }

    /// <summary>A short buzz so a change to the rumble rows is felt immediately.</summary>
    private static void DualSenseFeedbackTestPulse() => DualSensePad.Rumble(0.6f, 0.4f, 200);

    private static string StrengthLabel() => $"{(int)(Configuration.Config.DualSenseRumbleStrength * 100)}%";

    private void RefreshBindingLabels()
    {
        BindingRows[0].Item.Replace = PadButtonNames.Describe(Configuration.Config.BombButton);
        BindingRows[1].Item.Replace = PadButtonNames.Describe(Configuration.Config.ShootButton);
        BindingRows[2].Item.Replace = PadButtonNames.Describe(Configuration.Config.JumpButton);
        BindingRows[3].Item.Replace = PadButtonNames.Describe(Configuration.Config.FocusButton);
        BindingRows[4].Item.Replace = PadButtonNames.Describe(Configuration.Config.PauseButton);
    }

    public override void TopUpdate()
    {
        base.TopUpdate();
        if (SelectedIndex < 0 || SelectedIndex >= MenuItems.Count)
            return;

        // Only a rebinding row swallows pad presses; on any other row the pad is just navigating the menu.
        var row = BindingRows.FirstOrDefault(r => ReferenceEquals(r.Item, MenuItems[SelectedIndex]));
        if (row.Item is null)
            return;

        // GetGamepadButtonPressed() is PadButton? and is null when nothing is pressed (every frame, on the
        // backends without a real "last pressed" query). The old unconditional (PadButton) cast unboxed that
        // null and threw the moment this screen opened.
        PadButton? pressed = GetGamepadButtonPressed();
        if (pressed is null or PadButton.Unknown)
            return;

        row.Assign(pressed.Value);
        row.Item.Replace = PadButtonNames.Describe(pressed.Value);
        // The bindings no longer came from a layout the game chose, so stop offering to choose one.
        Configuration.Config.GamepadProfile = "custom";
        Configuration.Config.Save();
    }

    public override void Render()
    {
        DrawBackground();
        DrawTitle();
        DrawMenu();
    }

#if DEBUG
    public override void DrawImgui()
    {
        Begin("Gamepad Options");
        Text($"DualSense: {DualSensePad.StatusLine()}");
        if (DualSensePad.Diagnostic is { } diagnostic)
            Text(diagnostic);
        if (Button("Test rumble"))
            DualSensePad.Rumble(1f, 0.5f, 300);
        if (Button("Exit"))
            Exit();
        End();
    }
#endif
}
