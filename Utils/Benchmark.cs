using System.Diagnostics;
using System.Text.Json;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Screens;

namespace DmitryAndDemid.Utils;

/// <summary>The outcome of a <see cref="Benchmark"/> run, shown by the in-game StatisticsScreen and printed by --bench.</summary>
public struct BenchmarkResult
{
    public string Backend;
    public int TargetLoad;
    public int PeakObjects;
    public int Ticks;
    public double Seconds;
    public double TicksPerSec;
    public double RealtimeMultiple;   // TicksPerSec / 60 — headroom over the fixed tick budget
    public long MemMaxBytes;
    public long MemAvgBytes;
    public long MemMedianBytes;
    public string? Error;

    /// <summary>Host machine snapshot (OS / CPU / RAM / GPU), gathered once at the start of the run.</summary>
    public SystemInfo System;
}

/// <summary>
/// Headless sim-throughput benchmark. It builds a real <see cref="GameBox"/> on the first character + stage,
/// synthesises a heavy bullet load (deep-cloning live bullets up to a target so the update + O(n²) collision run
/// at real-scene scale), and ticks it as fast as the CPU allows (<see cref="GameBox.BenchMode"/> bypasses the
/// 60 TPS gate). The reported ticks/sec is the number that decides whether an interpreter (mono-nx / Switch) can
/// hold 60 TPS under load; the GC-heap samples show memory pressure. Renders nothing. See docs/switch-port.md.
/// </summary>
public static class Benchmark
{
    /// <param name="muteSfx">
    /// Silence SFX for the run. The headless <c>--bench</c> path sets this (no one is listening); the in-game
    /// Settings → Benchmark path leaves it false so the player hears the run.
    /// </param>
    public static BenchmarkResult Run(int targetLoad = 1000, int maxTicks = 2000, double maxSeconds = 12.0,
        bool muteSfx = false)
    {
        var r = new BenchmarkResult { Backend = Engine.BackendName, TargetLoad = targetLoad };
        // Host snapshot for the results panel. The renderer may not be up in every context (headless CLI), so
        // fetch it defensively; SystemInfo.Collect handles a null renderer by omitting the GPU line.
        IRenderer? renderer = null;
        try { renderer = Engine.Renderer; } catch { /* no backend in this context */ }
        r.System = SystemInfo.Collect(renderer);
        // Stage loading and object spawning write binary debug spam through Console.WriteLine; mute it for the
        // whole run (the result is RETURNED, not printed, so the caller / stats screen still reports it).
        TextWriter previousOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        // Ticking the sim fires SFX (damage, spell-timeout, …). Headless runs mute them; an in-game run keeps them.
        float previousSfx = Engine.Audio.SfxVolume;
        if (muteSfx)
            Engine.Audio.SfxVolume = 0f;
        try
        {
            string charPath = Assets.Files("Assets/Data/PlayablePersons/", "*.json")[0];
            var data = JsonSerializer.Deserialize<ProtogonistData>(File.ReadAllText(charPath))
                       ?? throw new Exception("no playable character found");
            string stagePath = FileStageInfo.CampaignStagePaths()[0];
            var pkg = BitPackage.OpenStreamReadPackage(stagePath);
            var stage = FileStageInfo.Load(ref pkg);
            pkg.Dispose();

            var screen = new GameplayScreen(data, 0, [stage], 0, false);
            // The bench's static player dies during ticking → box game-over → GameplayScreen.Paused would
            // AddScreen a PauseMenu into the live stack (it leaks even though this screen isn't shown). IsDemo is
            // the game's existing guard for exactly this headless-gameplay case, so reuse it: no pause menu.
            screen.IsDemo = true;
            GameBox box = screen.GameBox;
            box.BenchMode = true;

            // Warm up: let the stage spawn some real bullets to clone, and let the interpreter JIT the tick path.
            for (int i = 0; i < 120; i++) box.Update();
            Inflate(box, targetLoad);

            var mem = new List<long>();
            var sw = Stopwatch.StartNew();
            int ticks = 0;
            while (ticks < maxTicks && sw.Elapsed.TotalSeconds < maxSeconds)
            {
                box.Update();
                ticks++;
                // Run from the settings menu (SFX not muted): give an audible heartbeat every 1000 ticks. The
                // headless CLI bench passes muteSfx:true and stays silent.
                if (!muteSfx && ticks % 1000 == 0)
                    Helper.PlaySound(Runtime.CurrentRuntime.Sounds["boss-appear"]);
                if (box.BoxObjects.Count > r.PeakObjects) r.PeakObjects = box.BoxObjects.Count;
                if (ticks % 30 == 0)
                {
                    mem.Add(GC.GetTotalMemory(false));
                    Inflate(box, targetLoad);   // top up as cloned bullets fly off screen and despawn
                }
            }
            sw.Stop();

            r.Ticks = ticks;
            r.Seconds = sw.Elapsed.TotalSeconds;
            r.TicksPerSec = r.Seconds > 0 ? ticks / r.Seconds : 0;
            r.RealtimeMultiple = r.TicksPerSec / GameBox.TargetTPS;
            if (mem.Count > 0)
            {
                mem.Sort();
                r.MemMaxBytes = mem[^1];
                r.MemMedianBytes = mem[mem.Count / 2];
                long sum = 0;
                foreach (long m in mem) sum += m;
                r.MemAvgBytes = sum / mem.Count;
            }
        }
        catch (Exception ex)
        {
            r.Error = ex.Message;
        }
        finally
        {
            Console.SetOut(previousOut);
            Engine.Audio.SfxVolume = previousSfx;
        }
        return r;
    }

    /// <summary>
    /// Deep-clone live bullets (ones with an update action — they move and despawn) up to <paramref name="target"/>.
    /// <see cref="GameBox.AddObject"/> queues, so the additions land on the next tick; we clone off the current
    /// count and let the count settle across ticks, topping up periodically.
    /// </summary>
    static void Inflate(GameBox box, int target)
    {
        var live = box.BoxObjects.Where(o => o.UpdateAction != null).ToList();
        if (live.Count == 0) return;
        int need = target - box.BoxObjects.Count;
        for (int i = 0; i < need; i++)
            box.AddObject(live[i % live.Count].DeepCloneForBench());
    }

    static string[] ByteTypes = ["B","KiB","MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB"];

    /// <summary>Format a byte count as a compact human-readable string (used by the stats screen and --bench log).</summary>
    public static string FormatBytes(long bytes)
    {
        // TODO: FIX THAT)
        int index = (int)(Math.Log2(1024) / Math.Log2(bytes));
        return $"{bytes / Math.Pow(1024, index):F1} {ByteTypes[index]}";
    }
}
