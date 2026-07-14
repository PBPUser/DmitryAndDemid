using DmitryAndDemid.Rendering;
using DmitryAndDemid.Common;
using static DmitryAndDemid.Rendering.Gfx;
using static ImGuiNET.ImGui;

namespace DmitryAndDemid.Screens;

public class GamepadSettingsScreen : MenuScreen
{
    public GamepadSettingsScreen()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["gamepad_settings.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        CurrentY = Runtime.CurrentRuntime.Height / 2;
    }

    public override void CreateMenu()
    {
        MenuItems.Add(new MenuItem("controller.bomb", Configuration.Config.BombButton.ToString(), i => {}));
        MenuItems.Add(new MenuItem("controller.shoot", Configuration.Config.ShootButton.ToString(), i => {}));
        MenuItems.Add(new MenuItem("controller.jump", Configuration.Config.JumpButton.ToString(), i => {}));
        MenuItems.Add(new MenuItem("controller.focus", Configuration.Config.FocusButton.ToString(), i => {}));
        MenuItems.Add(new MenuItem("controller.pause", Configuration.Config.PauseButton.ToString(), i => {}));
        MenuItems.Add(new MenuItem("settings.default", "", i =>
        {
            var defaultConfiguration = new Configuration();
            Configuration.Config.JumpButton = defaultConfiguration.JumpButton;
            Configuration.Config.PauseButton = defaultConfiguration.PauseButton;
            Configuration.Config.FocusButton = defaultConfiguration.FocusButton;
            Configuration.Config.ShootButton = defaultConfiguration.ShootButton;
            Configuration.Config.BombButton = defaultConfiguration.BombButton;
            Configuration.Config.Save();
            MenuItems[0].Replace = Configuration.Config.PauseButton.ToString();
            MenuItems[1].Replace = Configuration.Config.BombButton.ToString();
            MenuItems[2].Replace = Configuration.Config.ShootButton.ToString();
            MenuItems[3].Replace = Configuration.Config.FocusButton.ToString();
            MenuItems[4].Replace = Configuration.Config.JumpButton.ToString();
        }));
        MenuItems.Add(new MenuItem("controller.back", "", i => Exit()));
        base.CreateMenu();
    }

    public override void TopUpdate()
    {
        base.TopUpdate();
        if (SelectedIndex >= 5)
            return;
        PadButton padButton = (PadButton)GetGamepadButtonPressed();
        if (padButton == PadButton.Unknown)
            return;
        MenuItems[SelectedIndex].Replace = padButton.ToString();
        switch (SelectedIndex)
        {
            case 0: Configuration.Config.BombButton = padButton; break;
            case 1: Configuration.Config.ShootButton = padButton; break;
            case 2: Configuration.Config.JumpButton = padButton; break;
            case 3: Configuration.Config.FocusButton = padButton; break;
            case 4: Configuration.Config.PauseButton = padButton; break;
        }
        Configuration.Config.Save();
    }

    public override void Render()
    {
        DrawBackground();
        DrawTitle();
        DrawMenu();
    }

    public override void DrawImgui()
    {
        Begin("Gamepad Options");
        if (Button("Exit"))
            Exit();
        End();
    }
}