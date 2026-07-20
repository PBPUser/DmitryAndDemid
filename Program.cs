using System.Runtime.InteropServices;
using DmitryAndDemid;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;

#if SWITCH
// mono-nx has NO attachable managed debugger — the author's own method (notes/writeup.md) is logging + exception
// stack traces. Two things make that usable for us, and both must run before anything else can throw:
//   1. UseSystemResourceKeys — without it the StackTrace constructor itself faults on mono-nx while formatting an
//      exception (per the writeup), so a crash would hide its own trace. This makes traces printable.
//   2. A last-chance handler that writes the full exception + stack trace to stdout (captured by config.ini's
//      logging / udp_io_redirect / file_io_redirect) before the process terminates — otherwise a fatal error is
//      just "Terminating application" with no cause.
AppContext.SetSwitch("System.Resources.UseSystemResourceKeys", true);
// System.Text.Json builds fast converters with Reflection.Emit when dynamic code is "supported"; the mono-nx
// interpreter can't do dynamic codegen and crashes on it (deserializing BulletRenderingInfo's Vector2 fields
// takes down the app). Declare dynamic code unsupported so STJ uses its reflection-only path — the same state
// NativeAOT runs in, where reflection-JSON still works fine for these plain data types.
AppContext.SetSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", false);
// Write the fatal trace to a FILE as well as stdout: on a hard fault the process dies before the stdout buffer
// flushes over UDP, so the Console line is usually lost — but AppendAllText opens/writes/closes, surviving the
// crash. Read it back over FTP at /mono/crashtrace.txt. (AppContext.BaseDirectory is /mono on the SD.)
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    string msg = "FATAL: " + e.ExceptionObject;
    Console.WriteLine(msg);
    try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "crashtrace.txt"), msg + "\n"); } catch { }
};

// On mono-nx the process working directory is NOT the game folder, so any relative "config.json" / "Assets/..."
// path resolves against the wrong place and the game can't find its files. Anchor both the writable data dir
// (config.json, the save file, replays — via Platform.DataPath) and the read-only asset root (via the Assets
// seam) to the directory the assembly was actually loaded from, which on the SD card is /mono.
Platform.DataDirectory = AppContext.BaseDirectory;
Assets.Source = new FileSystemAssetSource(AppContext.BaseDirectory);
#endif

// --selftest boots the managed game far enough to prove the port is sound on a given CPU/OS — it loads config,
// checks the asset source resolves, and reports which backend would be chosen — then exits WITHOUT opening a
// window or a graphics context. That last part matters: it is the only piece that runs under headless CPU
// emulation (qemu/podman linux-arm64), where there is no GPU or display. Real rendering needs actual hardware.
if (args.Contains("--selftest"))
    return SelfTest.Run();

// --compile-stages [srcDir] [outDir]: headless build step that turns human-authored JSON stages into the
// packed binary .sid the game loads. Defaults: Assets/Data/StagesJson -> Assets/Data/SpellCards. Exits WITHOUT
// opening a window (like --selftest), so it can run during a build or from the CLI.
if (args.Contains("--compile-stages"))
    return StageCompiler.Run(args);

// --export-stages [srcDir] [outDir]: the reverse — dump the packed .sid stages back out to editable JSON
// (Assets/Data/SpellCards -> Assets/Data/StagesJson), to bootstrap or hand-edit a stage. Also headless.
if (args.Contains("--export-stages"))
    return StageCompiler.Export(args);

// The GTK pre-launch dialog is desktop-only. On Android there is no GTK at all — the in-game settings screen
// (Screens/SettingsScreen.cs, drawn by the renderer like everything else) is the only configuration UI. On
// linux-arm64 (Tegra X1 / L4T, Jetson, Shield) GTK is likewise skipped so the game has no hard libgtk-3
// dependency on the device; configuration there goes through the in-game settings screen too. On Switch
// homebrew (SWITCH) GtkSharp can't load at all under mono-nx's static-only P/Invoke, so it is compiled out
// entirely — same as Android — and the in-game settings screen is again the only configuration path.
#if !ANDROID && !SWITCH
bool skipGtk = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
               RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
if (Configuration.Config.AlwaysAsk && !skipGtk)
    new PreconfigWindow().Open();
else
#endif
{
    // --bench (desktop) or "Bench": true in config.json (on-device, where no argv is passed) runs the headless
    // sim-throughput benchmark after asset load instead of the menu loop. See Runtime.RunBench.
    Runtime.BenchMode = args.Contains("--bench") || Configuration.Config.Bench;
    Runtime.CurrentRuntime = new Runtime();
    Runtime.CurrentRuntime.Start();
}

return 0;

static class SelfTest
{
    public static int Run()
    {
        Console.WriteLine("=== AAG2 self-test ===");
        Console.WriteLine($"OS:   {RuntimeInformation.OSDescription}");
        Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"JVM:  {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"Identifier:  {RuntimeInformation.RuntimeIdentifier}");

        bool ok = true;

        try
        {
            _ = Configuration.Config;
            string renderer = Configuration.Config?.Renderer ?? "(default)";
            Console.WriteLine($"config.json: loaded (renderer='{renderer}')");
        }
        catch (Exception e)
        {
            ok = false;
            Console.WriteLine($"config.json: FAILED — {e.Message}");
        }

        Console.WriteLine($"Raylib supported here: {RendererRegistry.RaylibSupported}");
        Console.WriteLine($"Renderers offered: {string.Join(", ", RendererRegistry.Available.Select(r => r.Key))}");

        // Prove the asset layer resolves the files the game reads at startup.
        foreach (string probe in new[] { "Assets/Data/translation.json", "Assets/Shaders/base.vs" })
        {
            try
            {
                bool exists = Assets.Exists(probe);
                Console.WriteLine($"asset '{probe}': {(exists ? "found" : "MISSING")} ({Assets.Resolve(probe)})");
                ok &= exists;
            }
            catch (Exception e)
            {
                ok = false;
                Console.WriteLine($"asset '{probe}': FAILED — {e.Message}");
            }
        }

        Console.WriteLine(ok ? "SELFTEST OK" : "SELFTEST FAILED");
        return ok ? 0 : 1;
    }
}

static class StageCompiler
{
    public static int Run(string[] args)
    {
        int i = Array.IndexOf(args, "--compile-stages");
        string src = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : "Assets/Data/StagesJson";
        string outDir = i + 2 < args.Length && !args[i + 2].StartsWith("--") ? args[i + 2] : "Assets/Data/SpellCards";

        if (!Directory.Exists(src))
        {
            Console.WriteLine($"compile-stages: source folder '{src}' not found — nothing to do.");
            return 0;
        }
        Directory.CreateDirectory(outDir);

        int compiled = 0, failed = 0;
        foreach (string json in Directory.GetFiles(src, "*.json"))
        {
            string sid = Path.Combine(outDir, Path.GetFileNameWithoutExtension(json) + ".sid");
            try
            {
                DmitryAndDemid.Data.Archive.FileStageInfo info = DmitryAndDemid.Data.Archive.StageJson.Load(json);
                var package = new DmitryAndDemid.Utils.BitPackage();
                // Info.Save writes debug spam through BitPackage.WriteVarULong; keep the build output clean.
                System.IO.TextWriter previous = Console.Out;
                Console.SetOut(System.IO.TextWriter.Null);
                try { info.Save(ref package); }
                finally { Console.SetOut(previous); }
                File.WriteAllBytes(sid, package.Export());
                compiled++;
                Console.WriteLine($"compile-stages: {json} -> {sid}");
            }
            catch (Exception e)
            {
                failed++;
                Console.Error.WriteLine($"compile-stages: FAILED {json}: {e.Message}");
            }
        }
        Console.WriteLine($"compile-stages: {compiled} compiled, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }

    public static int Export(string[] args)
    {
        int i = Array.IndexOf(args, "--export-stages");
        string src = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : "Assets/Data/SpellCards";
        string outDir = i + 2 < args.Length && !args[i + 2].StartsWith("--") ? args[i + 2] : "Assets/Data/StagesJson";

        if (!Directory.Exists(src))
        {
            Console.WriteLine($"export-stages: source folder '{src}' not found — nothing to do.");
            return 0;
        }
        Directory.CreateDirectory(outDir);

        int exported = 0, failed = 0;
        foreach (string sid in Directory.GetFiles(src, "*.sid"))
        {
            string json = Path.Combine(outDir, Path.GetFileNameWithoutExtension(sid) + ".json");
            try
            {
                var package = DmitryAndDemid.Utils.BitPackage.OpenStreamReadPackage(sid);
                DmitryAndDemid.Data.Archive.FileStageInfo info = DmitryAndDemid.Data.Archive.FileStageInfo.Load(ref package);
                package.Dispose();
                DmitryAndDemid.Data.Archive.StageJson.Save(info, json);
                exported++;
                Console.WriteLine($"export-stages: {sid} -> {json}");
            }
            catch (Exception e)
            {
                failed++;
                Console.Error.WriteLine($"export-stages: FAILED {sid}: {e.Message}");
            }
        }
        Console.WriteLine($"export-stages: {exported} exported, {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
}
