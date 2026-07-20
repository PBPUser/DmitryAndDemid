# iOS (iPadOS) port — roadmap

Target: run the game on iPhone/iPad via **[.NET for iOS](https://learn.microsoft.com/dotnet/ios/)**
(`net10.0-ios`, full AOT) with **[MoltenVK](https://github.com/KhronosGroup/MoltenVK)** — Vulkan-over-Metal —
so the existing `Rendering/VulkanBackend.cs` can be reused instead of writing a native Metal backend.

This document is the map. Unlike the desktop and Android builds, **none of this can be built or run on this
Linux box** — iOS linking, AOT and code-signing require macOS + Xcode + the `ios` .NET workload. Everything
below lives behind an `IOS` compile guard (mirroring `SWITCH`) so it stays out of the normal build until a Mac
toolchain exists.

> Reality check: this is a real, multi-part effort with two showstopper-class risks — **MoltenVK loader
> integration under AOT** and **decoupling `VulkanBackend` from `Silk.NET.Windowing`**. Prove those two on a
> device before investing in polish. The gameplay/simulation code ports for free; the graphics + host layer is
> where the work is.

---

## The four environments

| | Desktop | Android | Switch (scaffold) | **iOS (this doc)** |
|---|---|---|---|---|
| Runtime | .NET 10 CoreCLR (JIT) | .NET 10 Mono (JIT kept) | mono-nx interpreter, .NET 9 | **.NET 10 for iOS, full AOT** |
| Backend | Raylib / Silk GL / Vulkan | Silk GLES | deko3d (new) | **Vulkan → MoltenVK (Metal)** |
| Surface | GLFW/SDL via Silk windowing | `GLSurfaceView` | libnx | **`CAMetalLayer` + `VK_EXT_metal_surface`** |
| Native loading | dynamic (`dlopen`) | dynamic | static only | **dynamic (embedded `.framework`)** |
| Shaders | runtime GLSL / SPIR-V | runtime GLES | offline `.dksh` | **runtime SPIR-V (MoltenVK → MSL)** |
| Config UI | GTK pre-launch dialog | in-game Settings screen | in-game Settings | **in-game Settings screen** |

The row that matters most: iOS is the **only** target that is both AOT-only (like Switch) *and* has a rich,
supported .NET SDK with dynamic native loading (like desktop/Android). That combination is what makes MoltenVK
viable here where it would be far harder on Switch.

---

## Hard constraints (why the plan looks the way it does)

1. **No JIT — full AOT.** Same constraint as Switch. The Roslyn `CSharpScript` stage-script path faults with no
   codegen. It is already gated at `Gameplay/RuntimeData/RuntimeStageInfo.cs:26` (`#if SWITCH`); iOS must join
   that gate. Shipped content runs through `ActionsScope` delegates, so gameplay is unaffected. Set
   `<RunAOTCompilation>true` and expect the `System.Text.Json` dynamic-code issue the Switch notes already
   describe (declare dynamic code unsupported so STJ uses its reflection path — see `Program.cs`).

2. **MoltenVK is a *portability* driver.** It does not expose full Vulkan. The instance must be created with
   `VK_KHR_portability_enumeration` + `InstanceCreateFlags.EnumeratePortabilityBitKhr`, and any device that
   advertises `VK_KHR_portability_subset` **must** enable it. The current `VulkanBackend.CreateInstance`
   (`Rendering/VulkanBackend.cs:248`) and `CreateLogicalDevice` (device extensions at `:320`) hard-code a
   desktop assumption and will need these added on iOS.

3. **`Silk.NET.Windowing` does not run on iOS.** The backend is welded to `IWindow` — surface creation
   (`Window.VkSurface`, `:250`/`:276`), the frame pump (`Window.DoEvents()`, `:649`), sizing, vsync, icon,
   `IsClosing`. iOS has no such window: a `UIViewController` owns a `CAMetalLayer` and a `CADisplayLink` drives
   frames. So the backend needs an **`StartIos(layerPtr, w, h)` init path that bypasses Silk windowing** and an
   externally-driven frame step — exactly the split Android already made with `Runtime.StartAndroid` /
   `Runtime.RunFrame` vs desktop `OpenWindow`.

4. **Mac + Apple tax.** Building, signing and shipping need macOS, Xcode, the `dotnet workload install ios`
   workload, and an Apple Developer account ($99/yr) for on-device runs, TestFlight and the App Store. There is
   no way around a Mac in the loop (local, CI, or a rented macOS runner).

5. **GTK / Raylib / ImGui are desktop-only.** Compiled out on iOS exactly as Android does it (the editors,
   `PreconfigWindow`, `RaylibBackend`, `RaylibAudio`).

---

## Prerequisites (Phase 0)

- macOS + Xcode (matching the .NET iOS workload's supported Xcode).
- `dotnet workload install ios` on that Mac.
- Apple Developer account; a signing certificate + provisioning profile (device) or just a simulator to start.
- **MoltenVK** for iOS: either the [Vulkan SDK for iOS](https://vulkan.lunarg.com/sdk/home#mac) (ships the
  loader + MoltenVK ICD) or MoltenVK's `MoltenVK.xcframework`. Decide **loader vs. direct-ICD** early — see
  [Native integration](#native-integration-the-crux).

---

## The plan

### Phase 1 — project + `IOS` guard

- Add `Ios/DmitryAndDemid.iOS.csproj`, `TargetFramework=net10.0-ios`, `OutputType=Exe`, following the Android
  csproj's shape: `Compile Include="..\**\*.cs"` with the same exclude list **plus** anything GLES/Silk-window
  specific, and `<DefineConstants>$(DefineConstants);IOS`.
- Keep the Vulkan backend **in** (unlike Android, which excludes it): iOS is the Vulkan target. Exclude
  `RaylibBackend`, `RaylibAudio`, `PreconfigWindow`, `Program.cs`, the ImGui editors, and — because Silk
  windowing is gone — do **not** rely on any `Silk.NET.Windowing` code path (see Phase 3, it must be
  `#if IOS`-excluded within `VulkanBackend`).
- `PackageReference`s: `Silk.NET.Vulkan`, `Silk.NET.Vulkan.Extensions.KHR`, **`Silk.NET.Vulkan.Extensions.EXT`**
  (already used on desktop — it provides `ExtMetalSurface`), `StbImageSharp`, `StbTrueTypeSharp`, `LZMA-SDK`,
  `LuaCSharp`. **Drop** `Microsoft.CodeAnalysis.CSharp.Scripting` (dead under AOT, and a large trim liability).
- `<RunAOTCompilation>true</RunAOTCompilation>`; reproduce `Program.cs`'s AOT `AppContext` switches
  (`IsDynamicCodeSupported=false`, STJ reflection path) in the iOS entry point.
- Gate the Roslyn path: change `RuntimeStageInfo.cs:26` from `#if SWITCH` to `#if SWITCH || IOS` (or introduce a
  shared `NO_JIT` symbol defined by both — cleaner, and future-proofs the next AOT target).
- Reuse the Android `GenerateBuildInfo` target verbatim so `BuildInfo.Number` exists (it only reads
  `build_number.txt`, no increment).

### Phase 2 — host + seams (`Ios/`)

Mirror `Android/` file-for-file:

- **`IosAssetSource : IAssetSource`** — *simpler than Android*. iOS ships resources as real files in the app
  bundle, so this is essentially `FileSystemAssetSource` rooted at `NSBundle.MainBundle.BundlePath`. Add the
  assets with `<BundleResource Include="..\Assets\**\*"><Link>Assets\%(RecursiveDir)%(Filename)%(Extension)`.
  No unpack step (contrast `AndroidAssetSource`, which copies APK assets to storage first).
- **`IosPlatform`** — set `Platform.DataDirectory` to the app's Documents dir
  (`Environment.GetFolderPath(SpecialFolder.Personal)`), `TraceHandler`/`FatalErrorHandler` → `NSLog` /
  `Console.Error`. (`Utils/Platform.cs` is already the hook seam.)
- **`IosAudio : IAudio`** — the one genuinely new subsystem. Implement against **AVAudioEngine** (or OpenAL via
  `OpenTK`/`Silk.NET.OpenAL`). Model it on `Android/AndroidAudio.cs`; the game only needs SFX one-shots + a
  volume, so a small player-pool over AVAudioPlayerNode is enough. (Music is currently stubbed —
  `Helper.UpdatePlayingMusic` throws — so audio scope is SFX only for now.)
- **`AppDelegate` + `GameViewController`** — a `UIViewController` whose `View`'s `Layer` is a `CAMetalLayer`
  (override `+layerClass`), sized to the screen scale. A `CADisplayLink` calls `Runtime.RunFrame()` once per
  vsync. On first frame, build the runtime and attach the backend to the layer (see Phase 3). This is the
  iOS analogue of `Android/MainActivity.cs`'s `GameRenderer.onDrawFrame` → `Game.RunFrame()`.
- **`Runtime.StartIos(...)`** — an init entry beside `StartAndroid` that hands the backend the `CAMetalLayer`
  pointer + pixel size + `IosAudio`, then loads assets (same "load everything before returning" contract as
  `StartAndroid`).

### Phase 3 — MoltenVK backend adaptation (the core graphics work)

`VulkanBackend.cs` needs an iOS init path that does **not** touch `Silk.NET.Windowing`. Concretely:

- **Split init.** Keep desktop `OpenWindow` (`:201`) behind `#if !IOS`. Add `StartIos(nint metalLayer, int w,
  int h)` that runs the same sequence — `CreateInstance` → `CreateSurface` → `PickPhysicalDevice` →
  `CreateLogicalDevice` → swapchain — but with the iOS-specific instance/surface below and **no `IWindow`**.
- **Instance extensions (manual).** Desktop pulls them from `Window.VkSurface!.GetRequiredExtensions()`
  (`:250`). On iOS, hard-list: `VK_KHR_surface`, `VK_EXT_metal_surface`, `VK_KHR_portability_enumeration`, and
  set `InstanceCreateInfo.Flags = InstanceCreateFlags.EnumeratePortabilityBitKhr`.
- **Surface (manual).** Replace `Window.VkSurface!.Create(...)` (`:276`) with
  `ExtMetalSurface.CreateMetalSurface(Instance, new MetalSurfaceCreateInfoEXT { SType = MetalSurfaceCreateInfoExt,
  PLayer = (void*)metalLayer }, null, out Surface)`. `ExtMetalSurface` comes from the already-referenced
  `Silk.NET.Vulkan.Extensions.EXT`.
- **Portability subset device extension.** In `CreateLogicalDevice` (`:320`), if the picked device advertises
  `VK_KHR_portability_subset`, add it to the enabled device extensions alongside `VK_KHR_swapchain` (required by
  the Vulkan spec for portability drivers).
- **Frame pump.** `Window.DoEvents()` (`:649`) / `IsClosing` / `SetWindowSize` / `SetVSync` / `SetWindowIcon`
  are all `IWindow` calls — bracket them `#if !IOS`. On iOS the `CADisplayLink` is the pump; resize comes from
  the view controller (see Phase 4).
- **Format/feature caveats.** MoltenVK prefers `B8G8R8A8Unorm` (already the backend's first choice at `:353`).
  Watch for unsupported features MoltenVK lacks; keep the pipeline to the common subset the game already uses.

### Phase 4 — input, lifecycle, rotation

- **Touch.** Feed `UITouch` began/moved/ended from `GameViewController` into the backend the way Android does
  (`Backend.SetTouches(...)`); the existing `TouchControls` handles the rest. Map coordinates through the same
  present-rect letterboxing the menus already use.
- **Lifecycle.** On background (`applicationDidEnterBackground`) stop the `CADisplayLink` and idle the GPU; on
  foreground, resume. Handle the Metal layer being torn down/re-created (recreate swapchain + surface).
- **Resize / rotation.** On `viewDidLayoutSubviews`, update the `CAMetalLayer.drawableSize` and recreate the
  swapchain (the backend needs a `RecreateSwapchain(w,h)` entry — desktop currently leans on Silk's resize
  event, which iOS lacks).

### Phase 5 — AOT / trim hardening + assets

- **Trimming.** Full AOT + ILLink can strip types reached only by reflection. Audit: `System.Text.Json`
  deserialization of the data models (`ProtogonistData`, `BulletRenderingInfo`, stage JSON), `LuaCSharp`, and
  any `Activator`/reflection in the asset loaders. Add `[DynamicDependency]` / a trimmer roots file as needed.
  The Switch notes already flag the STJ + `Vector2` dynamic-codegen crash — apply the same `AppContext` switch.
- **Assets** ship as `BundleResource` (Phase 2). Verify the case-sensitivity + path separators match what the
  loaders pass to `Assets.Resolve` (they use forward slashes already).

### Phase 6 — signing + distribution

- Simulator first (no signing), then a development-signed device build, then TestFlight, then App Store review.
- App Store note: the app must be self-contained; embedding MoltenVK as a framework is allowed (many shipping
  Vulkan iOS games do this). No private API use — MoltenVK is pure public Metal.

---

## Native integration (the crux)

Silk.NET.Vulkan resolves entry points through the Vulkan **loader** (`libvulkan`), which does not exist on iOS
by default. Two ways to satisfy it:

1. **Loader + MoltenVK ICD** — embed `libvulkan.dylib` (built for iOS) and the MoltenVK ICD; ship the ICD JSON.
   Silk works unchanged. Heavier bundle, but standard.
2. **Direct MoltenVK** — embed only `MoltenVK.framework` and point Silk's loader at MoltenVK's
   `vkGetInstanceProcAddr` (MoltenVK *is* an ICD and also exports the core entry points). Lighter, but you must
   override how Silk acquires the initial `vkGetInstanceProcAddr` (custom `Vk.GetApi` loader).

Either way the dylib/framework must be embedded in the app bundle's `Frameworks/` and referenced from the
csproj (`NativeReference` / `@rpath`). `.NET for iOS` supports dynamically-loaded embedded frameworks (unlike
Switch's static-only P/Invoke), so `[DllImport]` against MoltenVK resolves at runtime. **Prove a bare
`vkCreateInstance` succeeds on-device before anything else** — this is the single highest-risk integration point.

---

## Risks / showstoppers (rank order)

1. **MoltenVK loader under AOT** — getting Silk.NET.Vulkan to bind MoltenVK entry points inside an AOT app.
   De-risk with a 50-line spike: init instance + enumerate the physical device, nothing else.
2. **Decoupling `VulkanBackend` from `IWindow`** — invasive but mechanical; every `Window.*` call needs an iOS
   branch or an injected abstraction. Consider a tiny `IWindowHost` seam so the backend never sees `IWindow`
   directly (benefits desktop testability too).
3. **MoltenVK feature gaps** — a Vulkan feature/format the game uses that MoltenVK lacks. Low risk given the
   game's simple 2D pipeline, but verify shaders cross-compile (SPIR-V → MSL) cleanly.
4. **Trimming stripping data models** — surfaces as JSON deserialization returning nulls on-device only.
5. **Performance** — the 60 TPS `GameBox` sim on a phone CPU. Almost certainly fine (far weaker than desktop
   but far stronger than the Switch interpreter target), but measure with the stress benchmark
   (`Utils/Benchmark.cs`, now with the system-info panel) early.

---

## Alternatives considered

- **GLES via EAGL (reuse `SilkGLBackend`)** — the Android backend runs on GLES, and an `EAGLContext` +
  `CAEAGLLayer` would reuse it almost verbatim, avoiding MoltenVK entirely. *Lower effort, but GLES is
  deprecated on iOS* (functional today, Apple-uncertain long-term). Good fallback if MoltenVK integration
  stalls. This was **not** chosen for the roadmap per the port decision (Vulkan/MoltenVK).
- **Native Metal backend** (`Rendering/Metal/MetalBackend.cs` behind `IBackend`) — the Apple-blessed, durable
  path, no third-party driver. But it is a large new backend comparable to the Switch deko3d effort, and it
  throws away the working Vulkan code. Reserve for "MoltenVK proved unshippable."

---

## Code-touch map

New (all under `Ios/`, none built on this machine):

- `Ios/DmitryAndDemid.iOS.csproj` — `net10.0-ios`, `IOS` define, AOT, exclude list, BundleResource assets.
- `Ios/AppDelegate.cs`, `Ios/GameViewController.cs` — UIKit host, `CAMetalLayer`, `CADisplayLink` → `RunFrame`.
- `Ios/IosAssetSource.cs`, `Ios/IosPlatform.cs`, `Ios/IosAudio.cs` — the three seams (asset/platform/audio).
- `docs/ios-port.md` — this file.

Changed in shared code (all behind `#if IOS` / `NO_JIT`):

- `Gameplay/RuntimeData/RuntimeStageInfo.cs:26` — add iOS to the no-JIT gate.
- `Rendering/VulkanBackend.cs` — `#if !IOS` around every `Silk.NET.Windowing` call; add `StartIos`, manual
  instance extensions + portability flags, `VK_EXT_metal_surface` surface, `VK_KHR_portability_subset` device
  extension, `RecreateSwapchain(w,h)`.
- `Rendering/Engine.cs` — backend construction path for iOS (attach-to-layer instead of open-window), beside
  the existing `#if SWITCH` branch at `:32`.
- `Runtime.cs` — a `StartIos(...)` alongside `StartAndroid`.
- `Rendering/RendererRegistry.cs` — iOS offers only `("vulkan", "Vulkan")` (MoltenVK); no picker needed.

Nothing in `Gameplay/`, `Screens/`, `Common/`, `Utils/` (except the two gates above) should need to change —
that is the point of the seams, and it is what makes this a *port* rather than a rewrite.
