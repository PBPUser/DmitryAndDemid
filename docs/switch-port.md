# Nintendo Switch (Horizon OS) port — roadmap

Target: run the game — and so the **Nikitos Engine** under it — on a real Switch as homebrew, with
**[mono-nx](https://github.com/exelix11/mono-nx)** as the managed runtime and
**[deko3d](https://github.com/devkitPro/deko3d)** as the GPU backend.

This document is the map. Most of the work is **not** in this C# repo — it is in a fork of the mono-nx
native runtime and in an offline shader-compilation step. The C# side only has to grow one new backend
(`Rendering/Switch/Deko3dBackend.cs`) behind a `SWITCH` compile guard — a new thing for Likhanov32D to run on,
not a new engine.

> Reality check: this is a large, multi-repo effort with real showstopper-class risks (interpreter
> performance, offline shaders). Nothing here can be built or tested on a desktop — the `SWITCH` guard keeps
> it out of the normal build until a device toolchain exists.

---

## The three environments

| | Desktop | Android | **Switch (this doc)** |
|---|---|---|---|
| Runtime | .NET 10 CoreCLR (JIT) | .NET 10 (Mono/AOT) | **mono-nx: Mono interpreter, .NET 9** |
| Backend | Raylib / Silk.NET GL / Vulkan | Silk GLES | **deko3d** (new) |
| Native loading | dynamic (`dlopen`) | dynamic | **static only** (`dl_shim.c`) |
| Shaders | runtime GLSL / SPIR-V | runtime GLES | **offline `.dksh` via UAM** |

## Hard constraints (why the plan looks the way it does)

1. **mono-nx P/Invoke is static-only.** No `dlopen`. Every native entrypoint the C# calls must be listed in
   the runtime's `dl_shim.c` and linked into a custom mono-nx build. → see [dl_shim symbols](#dl_shim-symbols).
2. **mono-nx ships SDL2 + Dear ImGui, not deko3d.** Using deko3d means also compiling `deko3d` + `libnx`
   into the runtime image and exposing their symbols. (Falling back to the bundled SDL2 is the lower-effort
   alternative — see [Alternatives](#alternatives).)
3. **Interpreter, no JIT.** The 60 TPS simulation (`GameBox.BoxUpdate` → thousands of `RuntimeObject`s) is the
   perf risk. Measure early with a stress replay before investing in graphics.
4. **deko3d shaders are offline (UAM).** The runtime shader loader (`IRenderer.LoadShaderFromSource`) and the
   DEBUG shader previewer cannot exist on Switch. Every `Assets/Shaders/*.fs` needs a pre-built `.dksh`.
5. **The three existing backends can't load.** Silk.NET, Raylib-cs and GtkSharp all dynamically load native
   libs → unavailable. The Switch build must drop those package references and the GTK pre-launch dialog.
6. **Roslyn scripting won't run.** `CSharpScript.Create` / `Microsoft.CodeAnalysis` need runtime codegen.
   Shipped content already routes through `ActionsScope` delegates (see CLAUDE.md), so gate the Roslyn path
   (`RuntimeStageInfo.Scripts`) out under `SWITCH`.

---

## Phased plan

### Phase 0 — de-risk (do this first, before any deko3d code)
- [x] Build a stock mono-nx and get a managed assembly running on an emulator (Ryujinx). **Done — the actual
      game boots**: the interpreter loads `aag2.dll` + all 33 deps, config + assets resolve from `sdmc:/mono`,
      the deko3d backend inits headless (see below), all 54 shaders no-op, and it reaches the frame loop. No
      crash. Confirmed via `udp_io_redirect` / `nc -ulp 9999`.
- [~] Run a headless slice of the sim and measure ticks/sec. **Menu loop measured: ~32,000 FPS** headless on
      Ryujinx (`Deko3dBackend.EndFrame` heartbeat) — the interpreter has huge headroom for UI-weight work. But
      the heavy `GameBox` sim (thousands of `RuntimeObject`s) only starts once a stage is entered, which needs
      input (absent — no pad on stock mono-nx) or an auto-start bench. **Tick-rate under bullet load is still
      the open make-or-break number** — the ~32k FPS menu figure does NOT exercise the sim.
- [x] Confirm static P/Invoke behaviour — but the finding is the *inverse* of the plan: stock mono-nx's
      `__Internal` dl_shim exports only a few libnx symbols, and many pad helpers are `static inline` (not
      linkable at all). Real platform/input access needs C wrappers in a mono-nx fork, not just `dl_shim.c` edits.

**Milestone: the managed game runs on mono-nx (headless, black screen).** The remaining work is (1) measure
60 TPS, then (2) a real draw path — deko3d fork or an SDL2/GLES backend (mono-nx already exports SDL2).

**Benchmark harness (`Utils/Benchmark.cs`).** Builds a real `GameBox` on the first character + stage, deep-clones
live bullets up to a target load so the update + O(n²) collision run at real-scene scale (desktop test reached
~6.2k objects), ticks it ungated (`GameBox.BenchMode`), and reports **ticks/sec vs the 60 TPS budget** plus
GC-heap max/avg/median. SFX and debug console spam are muted during the run. Two entry points:
- **Headless:** `--bench` (desktop) or `"Bench": true` in `config.json` (on-device, where no argv is passed) —
  prints to the UDP/file log. This is the path for stock mono-nx, which has no input to drive menus.
- **In-game:** Settings → Benchmark (`BenchmarkScreen` → `StatisticsScreen`) — for desktop and any build with
  input. Desktop baseline (RTX 5070, JIT): ~2.4k ticks/s at ~6.2k objects (40× the budget). The mono-nx/Tegra
  number under the same load is the make-or-break figure still to capture.

### Phase 1 — managed portability (in THIS repo, testable on desktop)
- [x] Add the `SWITCH` build seam (`-p:SwitchBuild=true` → `SWITCH` constant; `Engine.Create` branch).
- [x] Retarget-compatible: `TargetFramework` is now conditional — `net9.0` under `SwitchBuild`, `net10.0`
      otherwise (a net10 assembly won't load on mono-nx's .NET 9 runtime). Verified: **`dotnet build -c Release
      -p:SwitchBuild=true` compiles clean as net9.0**, and both desktop net10 configs stay green. The retarget
      surfaced three real net10-only API uses in core (Release-path) code, all rewritten to
      framework-agnostic equivalents (no `#if`):
      - `IEnumerable.IndexOf` (LINQ, new in .NET 10) → `Array.IndexOf` — `Backgrounds/DrogichinBackground.cs`.
      - `Vector4.WithElement` (new in .NET 10) → `new Vector4(vec3, 1f)` — `Gameplay/Effects/{Strength,
        EntityDeath}ScreenEffect.cs`.
      Note: **Debug + SwitchBuild does not compile**, and intentionally so — the `#if DEBUG` editor screens
      (`StageEditorScreen`/`GameplayEditorScreen`) lean on the same net10 `IndexOf` LINQ, but they are DEBUG-only
      desktop tooling excluded from the Release device build (same rationale as the ImGui/previewer gating
      below). The shipping device build is Release, which compiles.
      The two build checks used throughout: plain `dotnet build` (desktop net10, no `SWITCH`) and
      `dotnet build -c Release -p:SwitchBuild=true` (net9.0 + `SWITCH`, the device config).
- [x] Gate out the runtime-path incompatibilities:
      - GTK `PreconfigWindow` — `Program.cs` now guards the launch with `#if !ANDROID && !SWITCH` (GtkSharp
        can't load under static P/Invoke; in-game `SettingsScreen` is the only config UI, same as Android).
      - Roslyn `Scripts` — `RuntimeStageInfo.LoadFromFile` skips `CSharpScript.Create` under `SWITCH` (the
        compiled `Scripts` are written but never executed; shipped content runs through `ActionsScope`).
      - The DEBUG shader previewer and ImGui editors need **no** `SWITCH` guard: a Switch *device* build is
        Release, so `#if DEBUG` already excludes them. (`Deko3dBackend.SupportsDebugUi` is `false` regardless.)
        The editor screens' own Roslyn / `LoadShaderFromMemory` / direct-`File` calls live behind that same
        DEBUG exclusion, so they are out of the device path too.
- [x] Route all asset IO through a single seam so `romfs:/` can back it on Switch. **Already in place** —
      `Utils/Assets.cs` (`IAssetSource` + settable `Assets.Source`); a Switch host sets
      `Assets.Source = new FileSystemAssetSource("romfs:/")` (or a dedicated romfs source). The only two
      direct-`File` bypasses left are in `GameplayEditorScreen` (DEBUG-only, out of the device path).
- [x] Implement the pure-managed input in `Deko3dBackend` (buttons/sticks) — no device needed to author it;
      see Phase 3's input line. Only touch (`HidTouchScreenState` layout) remains TODO.

### Phase 2 — native runtime fork (in a mono-nx fork)
- [x] Build a stock mono-nx runtime + interpreter on this machine (devkitPro + `switch-dev`, `llvm`,
      `switch-sdl2`/`switch-sdl2_image`; `build_mono.sh` then `native/interpreter/make`). Produces the .NET 9
      libnx framework (`artifacts/bin/runtime/net9.0-libnx-Debug-arm64`) and `native/interpreter/mono_nx.nro`.
      Gotchas hit: the AOT-offsets tool wants `/usr/local/lib/libclang.so` (symlink the system one); the LLVM
      toolchain needs the `llvm` package (`llvm-ar`); the interpreter's imgui/SDL2 backend needs the switch SDL2
      portlibs.
- [x] Stage the game as a mono-nx payload — `Tools/stage_mononx.sh` builds the net9 assemblies and lays out
      `aag2.dll` + NuGet deps + `Assets/` in the shape `copy_sd_files.sh` consumes (`lib_net9.0` +
      `default_assembly`). This is a **stock** mono-nx target: it should boot and run the 60 TPS loop (the
      no-op `Deko3dBackend.OpenWindow` calls only libnx symbols mono-nx ships), rendering a black screen — the
      Phase-0 smoke/perf test. **Running it needs Ryujinx or hardware (not doable on the build host).**
- [ ] Add `deko3d` + `libnx` (hid, romfs, applet, audout/audrv) to the runtime link. **← the real remaining
      fork work**: stock mono-nx ships SDL2, not deko3d, so the backend can't draw until these symbols exist.
- [ ] Populate `dl_shim.c` with the [symbol list](#dl_shim-symbols).
- [ ] Produce a bootable `.nro`/`.nsp` that loads the game's managed dlls from the SD card / romfs.

> **✅ RUNNING ON SWITCH (60 fps).** The game boots to the main menu and renders at a steady 60 fps on
> mono-nx via the SDL2 backend. Bring-up fixes that got it there: DllImport library names must be `"SDL2"`/
> `"SDL2_image"`/`"libnx"` (NOT `"__Internal"`, which only resolves the interpreter's own symbols); drop
> `SDL_RENDERER_PRESENTVSYNC` (a blocking vsync present hangs the console); shaders return valid *dummy* handles
> (LoadShaders treats Id==0 as a fatal compile error); audio `Initialize()` must report success (a false halts
> on an ADP error screen); and — the big one — set `RuntimeFeature.IsDynamicCodeSupported = false` so
> System.Text.Json uses its reflection-only path (the interpreter crashes on the Reflection.Emit converter it
> builds for `Vector2` fields, e.g. `BulletRenderingInfo`). **Open gap: fragment shaders** (all effects are
> stubbed — the SDL 2D renderer has none); an SDL+GLES path is the next step if shader effects are wanted.
>
> **Shader path built (`Rendering/Switch/SdlGlBackend.cs`).** SDL owns the window + GLES 3.0 context + input;
> the game's proven GL renderer (`SilkGLBackend`) draws, attached to the SDL context via
> `GL.GetApi(new LamdaNativeContext(SDL_GL_GetProcAddress))` — the same "attach external context" trick the
> Android host uses. All shaders/textures/targets/fonts are reused; `Assets/Shaders/gles` supplies the GLES
> variants. Selected with renderer key `gl`. **Two gates before it runs:** (1) the interpreter must be rebuilt
> with `MONO_NX_USE_OPENGL=1` (glad/EGL) — stock has no GL; (2) unproven risk — Silk.NET.OpenGL may hit the
> interpreter's no-dynamic-code limit (the Reflection.Emit issue that bit System.Text.Json); if so, fall back to
> a raw-GL renderer via `eglGetProcAddress` + unmanaged function pointers.
>
> **Path chosen: SDL2 backend.** mono-nx's `dl_shim_sdl2` exports the full SDL2 API (1249 symbols), so
> `Rendering/Switch/SdlBackend.cs` + `SdlInterop.cs` implement `IBackend` on SDL's 2D renderer via `__Internal`
> — window, accelerated renderer, textures (`IMG_LoadTexture`), nested render targets (with a flip fix for SDL's
> top-down targets vs the game's bottom-up convention), sprites/rects/lines, and Stb-rasterised fonts, plus
> gamepad input via `SDL_GameController`. It is now the default `SWITCH` backend (`Engine.Create`; deko3d stays
> selectable via the `deko3d` key for a future fork). **Known gaps (by design for the first cut):** fragment
> shaders can't run on SDL's renderer, so shader EFFECTS are stubbed (a future SDL+GLES path restores them);
> audio is stubbed (video-first — SDL core audio is the follow-up).

### Phase 3 — deko3d backend (in THIS repo, behind `SWITCH`)
- [ ] Flesh out `Deko3dBackend`: device/queue/memblock, swapchain on `nwindowGetDefault()`, a 2D quad
      pipeline, texture upload, uniform buffers, render-to-`DkImage` for the nested-target model.
- [ ] Map `IRenderer` blend modes / nested targets / `TargetFloor` onto deko3d state.
- [~] Input via libnx `pad*` — buttons + analog sticks **done** (`Deko3dBackend.IsPadDown`/`GetPadAxis`/
      `GetPressedPadButton`, positional `PadButton`→`HidNpadButton` mapping); touch still TODO
      (`hidGetTouchScreenStates` needs the `HidTouchScreenState` layout). Audio via `audrv`, time already wired
      via `armGetSystemTick`/`armGetSystemTickFreq`.

### Phase 4 — offline shader pipeline
- [ ] Author GLSL→deko3d variants of every `Assets/Shaders/*.fs` and compile with **UAM** to `.dksh`.
- [ ] Ship `.dksh` in romfs; `LoadShader` loads the blob into a `DkShader` (no runtime compile).
- [ ] Bake uniform names → uniform-buffer offsets at build time (replaces `GetUniformLocation`).

---

## dl_shim symbols

The static P/Invoke surface the mono-nx fork must export. Grouped by area; exact signatures live in
`deko3d.h` / libnx headers and are mirrored (to be verified) in `Rendering/Switch/Deko3dInterop.cs`.

**deko3d — device/memory/queue**
`dkDeviceCreate` `dkDeviceDestroy`
`dkMemBlockCreate` `dkMemBlockDestroy` `dkMemBlockGetCpuAddr` `dkMemBlockGetGpuAddr`
`dkQueueCreate` `dkQueueDestroy` `dkQueueSubmitCommands` `dkQueueFlush` `dkQueueWaitIdle`
`dkQueueAcquireImage` `dkQueuePresentImage`

**deko3d — command buffers**
`dkCmdBufCreate` `dkCmdBufDestroy` `dkCmdBufAddMemory` `dkCmdBufFinishList`
`dkCmdBufClear*` `dkCmdBufBindRenderTargets` `dkCmdBufBindShaders`
`dkCmdBufBindVtxBuffer` `dkCmdBufBindVtxAttribState` `dkCmdBufBindUniformBuffer`
`dkCmdBufBindTextures` `dkCmdBufBindImageDescriptorSet` `dkCmdBufBindSamplerDescriptorSet`
`dkCmdBufSetViewports` `dkCmdBufSetScissors` `dkCmdBufBindColorState` `dkCmdBufBindBlendStates`
`dkCmdBufDraw` `dkCmdBufDrawIndexed`

**deko3d — images / swapchain / shaders / descriptors / fences**
`dkImageLayoutInitialize` `dkImageLayoutGetSize` `dkImageLayoutGetAlignment` `dkImageInitialize`
`dkImageDescriptorInitialize` `dkSamplerDescriptorInitialize`
`dkSwapchainCreate` `dkSwapchainDestroy` `dkSwapchainSetCrop`
`dkShaderInitialize`
`dkFenceWait` `dkFenceSignal`

**libnx — platform / input / audio / fs**
`nwindowGetDefault`
`padConfigureInput` `padInitializeDefault` `padUpdate` `padGetButtons` `padGetButtonsDown` `padGetStickPos`
`hidInitializeTouchScreen` `hidGetTouchScreenStates`
`romfsInit` `romfsExit`
`appletMainLoop` `appletLockExit` `appletUnlockExit`
`audrvCreate` `audrvClose` `audrvVoiceStart` `audrvUpdate`
`armGetSystemTick` `armGetSystemTickFreq`

---

## Alternatives

- **SDL2 backend instead of deko3d.** mono-nx already static-links SDL2, so an `Sdl2Backend` skips the native
  fork's deko3d/libnx step. But SDL2's 2D renderer has no portable custom-fragment-shader path, and this game
  is shader-heavy — so you'd still need SDL2+GL (a GL context), which reintroduces a shader story. deko3d is
  the more honest fit for the hardware; SDL2 is the faster route to *something* on screen.
- **Switch-on-Linux (L4T / switchroot).** The project already targets `linux-arm64` (Tegra) and the existing
  **Vulkan** backend runs there on Mesa. If "on a Switch" can mean "Switch booted into Linux", this whole
  homebrew port is unnecessary — ship the arm64 Vulkan build (`Tools/publish_tegra.sh`). This is by far the
  lowest-risk way to see the game on Switch hardware.

---

## Files in this repo

- `Rendering/Switch/Deko3dInterop.cs` — P/Invoke declarations (guarded `#if SWITCH`). Signatures mirror
  `deko3d.h`/libnx and **must be verified against the installed headers**.
- `Rendering/Switch/Deko3dBackend.cs` — `IBackend` scaffold (guarded `#if SWITCH`). Lifecycle sketched,
  draw ops are TODOs pending Phase 3/4.
- `Rendering/Engine.cs` — `#if SWITCH` branch selecting the backend.
- `DmitryAndDemid.csproj` — `SwitchBuild` property that defines `SWITCH` and (when a device build exists)
  strips the desktop-only package references.
