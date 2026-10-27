namespace DmitryAndDemid.Utils;

/// <summary>
/// The few places the game has to talk to the host OS rather than to the renderer. Every one of them is a
/// settable hook with a plain-console default, because this lives in the engine and the engine must not know
/// what the host's UI toolkit is: the desktop game installs a GTK dialog over
/// <see cref="FatalErrorHandler"/> (Program.cs), Android installs a logcat write over
/// <see cref="TraceHandler"/> (MainActivity), and a host that installs neither still gets the message.
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
    /// Reports a failure the game cannot start from. A hook, not a direct call to any toolkit: the desktop
    /// game replaces it with a GTK message dialog and Android with a logcat write, and neither dependency
    /// belongs behind <c>IRenderer</c>. Left alone it writes to stderr, which is the right answer on the
    /// hosts that have no dialog to show (Switch, linux-arm64 headless, the test suite).
    /// </summary>
    public static Action<string> FatalErrorHandler { get; set; } = Console.Error.WriteLine;

    public static void FatalError(string message) => FatalErrorHandler(message);

    /// <summary>
    /// Low-severity diagnostic trace. Desktop writes to the console; Android replaces this with a logcat write
    /// (see MainActivity), because .NET's <see cref="Console"/> does not reach logcat and these marks would
    /// otherwise be lost — which is exactly why a gameplay-entry crash showed up as a bare "signal 9".
    /// </summary>
    public static Action<string> TraceHandler { get; set; } = Console.WriteLine;

    public static void Trace(string message) => TraceHandler(message);
}
