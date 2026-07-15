using DmitryAndDemid;

// The GTK pre-launch dialog is desktop-only. On Android there is no GTK at all — the in-game settings screen
// (Screens/SettingsScreen.cs, drawn by the renderer like everything else) is the only configuration UI.
#if !ANDROID
if (Configuration.Config.AlwaysAsk)
    new PreconfigWindow().Open();
else
#endif
{
    Runtime.CurrentRuntime = new Runtime();
    Runtime.CurrentRuntime.Start();
}
