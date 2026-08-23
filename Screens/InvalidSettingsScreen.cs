using DmitryAndDemid.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace DmitryAndDemid.Screens;

internal class InvalidSettingsScreen : MenuScreen
{
    public InvalidSettingsScreen()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["invalid_settings.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);

        MenuItems.Add(new MenuItem(""))
    }
}
