namespace DmitryAndDemid.Utils;

/// <summary>
/// The few places the game has to talk to the host OS rather than to the renderer. Desktop answers with GTK;
/// Android has no GTK at all, so it answers by logging — the message still reaches the loading screen, which
/// is drawn by the renderer like everything else.
/// </summary>
public static class Platform
{
    /// <summary>
    /// Where the game may WRITE: config.json, the save file, replays. Empty means the working directory,
    /// which is what desktop has always done. Android sets this to the app's private storage — an APK's
    /// contents are read-only, so a relative path there would fail on the first save.
    /// </summary>
    public static string DataDirectory { get; set; } = "";

    public static string DataPath(string file) =>
        string.IsNullOrEmpty(DataDirectory) ? file : Path.Combine(DataDirectory, file);

    /// <summary>
    /// Reports a failure the game cannot start from. Replaced wholesale on Android (see
    /// <c>Android/AndroidPlatform.cs</c>), which is why it is a hook and not a direct GTK call.
    /// </summary>
    public static Action<string> FatalErrorHandler { get; set; } = DefaultFatalError;

    public static void FatalError(string message) => FatalErrorHandler(message);

    private static void DefaultFatalError(string message)
    {
#if ANDROID
        Console.Error.WriteLine(message);
#else
        var dialog = new Gtk.MessageDialog(null, Gtk.DialogFlags.Modal, Gtk.MessageType.Info,
            Gtk.ButtonsType.Ok, message);
        dialog.Run();
        dialog.Destroy();
#endif
    }
}
