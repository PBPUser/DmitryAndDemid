# Native Metal backend (macOS / iOS)

A native Apple **Metal** implementation of `IBackend` — another backend for the **Nikitos Engine** to run on
— and the durable alternative to the MoltenVK path in
`docs/ios-port.md` — no third-party Vulkan driver, Apple-blessed, and the long-term graphics path for both
macOS and iOS.

Files: `Rendering/Metal/MetalBackend.cs` (the backend) and `Tools/compile_metal_shaders.py` (the shader
cross-compiler). Wired into `Engine.Create` and `RendererRegistry` behind `#if METAL`.

> **Status: Phase-1 scaffold, never compiled.** The `Metal` / `CoreAnimation` bindings only exist under a
> `net10.0-ios` / `net10.0-macos` TFM, which cannot be built on the Linux dev box — so the whole backend is
> under `#if METAL` and is dead in every currently-buildable configuration (verified: the desktop build is
> unaffected). This mirrors the Switch `Deko3dBackend`: the seam and plumbing are established and reasoned
> about off-device; the draw path is brought up incrementally on a Mac. `TODO(metal)` marks every spot a first
> Mac compile pass must reconcile against the installed workload's exact binding signatures.

---

## Why a native backend at all

`docs/ios-port.md` chose MoltenVK first because it reuses the existing `VulkanBackend`. A native Metal backend
is the *other* option that doc lists: more work up front (a whole new `IBackend`), but no MoltenVK packaging, no
`VK_KHR_portability_subset` caveats, and it is what Apple actually supports. The two can coexist — MoltenVK to
get pixels fast, Metal as the shipping backend — since both are just `IBackend` implementations selected in
`Engine.Create`.

---

## The shader story (the part that makes this tractable)

The game authors **legacy GLSL**. Metal consumes **MSL**. But the Vulkan backend already faced the same wall
and solved it: `Tools/compile_shaders.py` massages the GLSL and emits **SPIR-V + a JSON reflection sidecar**
(`{blockSize, blockBinding, uniforms:[{name,offset,type}], samplers:[{name,binding}]}`) into
`Assets/Shaders/vulkan/`.

Metal rides that pipeline instead of duplicating it:

```
GLSL ──(compile_shaders.py: massage + glslang)──▶ SPIR-V ──(compile_metal_shaders.py: SPIRV-Cross)──▶ MSL
                                                     │
                                                     └─ reflection JSON  ────────── reused UNCHANGED ─────────┐
                                                                                                              ▼
                                        MetalBackend writes uniforms into a `constant` struct at the SAME offsets
```

The crucial property: **SPIRV-Cross preserves the byte layout** of the generated `gl_DefaultUniformBlock`, so a
uniform recorded at offset N in the sidecar lands at offset N in the MSL `constant` struct. The sidecar is
therefore copied verbatim — one reflection format for both backends. `compile_metal_shaders.py` consumes the
committed `.spv`, so there is a single GLSL-massaging step (the Vulkan one) and Metal is pure back-end
translation.

- Output: `Assets/Shaders/metal/<name>.<stage>.metal` + copied `<name>.<stage>.json`, and (on macOS only)
  a precompiled `<name>.<stage>.metallib`.
- Entry point: SPIRV-Cross renames `main` → **`main0`** (main is MSL-reserved). The backend looks up the
  vertex/fragment `IMTLFunction` by that name.
- Runtime tools: `spirv-cross` (any OS) for `.metal`; `xcrun metal`/`metallib` (macOS) for the optional
  `.metallib`. Artifacts are committed, so players need neither — exactly like the Vulkan artifacts.

---

## How it maps onto `IRenderer`

| Area | Metal realisation |
|---|---|
| Setup | `MTLDevice.SystemDefault`, one `IMTLCommandQueue`, the host's `CAMetalLayer` (`PixelFormat = BGRA8Unorm`) |
| Frame | `BeginFrame`: `layer.NextDrawable()` + `CommandBuffer()` + a render-command encoder. `EndFrame`: end encoding, `PresentDrawable`, `Commit` |
| Clear | Metal clears at pass start, so `Clear()` restarts the encoder with `LoadAction.Clear` on the current colour texture |
| Textures | `IMTLTexture` (`RGBA8Unorm`), uploaded via `ReplaceRegion`; StbImageSharp decodes the PNG through the `Assets` seam |
| Render targets | offscreen `IMTLTexture` (`RenderTarget` usage); a nested stack with `TargetFloor`/`ResetTargets`, each `BeginTarget` opening a `LoadAction.Load` pass so targets accumulate — same nesting contract as the GL/Vulkan backends |
| Shaders | one `IMTLRenderPipelineState` per `(shader, BlendMode, colour-format)`, built lazily from the MSL `main0` functions; the fragment uniform block is a CPU `byte[]` written at reflected offsets and bound as a constant buffer |
| Uniforms | "location" **is** the byte offset from the sidecar; `SetUniform`/`SetUniformArray` write into the block; `colDiffuse` is pre-seeded to white (a zeroed block would paint every pixel black — the trap the Vulkan backend documents) |
| Drawing | immediate-mode textured/coloured quads and lines into a dynamic vertex buffer, one triangle-strip draw each *(bring-up target — see below)* |
| Fonts | StbTrueType-baked glyph atlas → textured quads, the same approach as the Silk/SDL backends *(bring-up target)* |
| Diagnostics | `QueryGpuInfo` → `device.Name`, `RecommendedMaxWorkingSetSize` as the VRAM proxy (feeds the benchmark system-info panel) |

### Host-driven, not window-owning

On Apple the window, run-loop and input belong to the UIKit/AppKit host — a view controller whose layer is a
`CAMetalLayer`, stepped by a `CADisplayLink`. So:

- `OpenWindow` throws; the host calls **`StartMetal(layerPtr, w, h, audio)`** (the analogue of
  `Runtime.StartAndroid`) and pumps `BeginFrame`/…/`EndFrame` itself.
- Input is fed in: `SetTouches(...)` / `SetKeyState(...)`, surfaced through `IInput` exactly as the Android host
  feeds the Silk backend. `TouchCount`/`GetTouchPosition` then drive the existing `TouchControls`.
- Audio (Demidonic) is **injected** via `StartMetal` (an `IAudio` the host supplies, e.g. AVAudioEngine), so the backend
  stays constructible parameterlessly through `Engine.Create` — same split as `SilkGLBackend` + `StartAndroid`.

### Coordinate note (will bite during bring-up)

The game's render targets follow OpenGL's bottom-up convention and sample themselves flipped (negative-height
source rects); Metal textures are top-down. The `Blit`/quad path must toggle the vertical flip for target
textures so composited frames come out upright — the same fix the Switch `SdlBackend` documents. This is the
first thing to get wrong and the first thing to check once quads draw.

---

## Bring-up order (incremental, Deko3d-style)

Draw calls are structured but not yet emitting geometry, so the loop runs before the vertex path is validated:

1. **Clear** — prove device/queue/layer/frame lifecycle by clearing the drawable to a colour. *(wired)*
2. **Quads** — the dynamic vertex buffer + default textured-quad pipeline; `DrawRect` with a 1×1 white texture.
3. **Textures** — `DrawTexture` variants (source→dest, rotation about origin), then render targets + the flip.
4. **Shaders** — pipeline cache keyed by `(shader, blend, format)`; bind the uniform block; port `BlendMode`
   → `MTLBlendFactor` on the colour attachment.
5. **Fonts** — bake the atlas, wire `MeasureText`/`DrawText`/`DrawTextPro`.

Each step is independently visible, so regressions localise instead of every frame aborting.

---

## Prerequisites

- macOS + Xcode + `dotnet workload install {ios|macos}`; a `net10.0-ios`/`-macos` host project that defines
  `METAL` (and pulls `MetalBackend.cs` + the shared sources in, like the Android csproj does).
- `spirv-cross` to generate the MSL (run `Tools/compile_shaders.py` first for the SPIR-V, then
  `Tools/compile_metal_shaders.py`).
- Full AOT applies (no JIT on iOS): the Roslyn stage-script path must be gated out and STJ put on its
  reflection path — see `docs/ios-port.md`, all of which applies here too.

---

## Risks

1. **Binding-signature drift** — the backend is unbuilt; the first Mac compile will surface `Metal` binding
   names/overloads that differ from what is written here. Mechanical, but real. Every such spot is `TODO(metal)`.
2. **The target flip** — getting bottom-up game targets to composite upright under top-down Metal.
3. **Pipeline-state explosion** — `(shader × blend × format)` combinations; the lazy cache bounds it, but watch
   the count.
4. **Font atlas fidelity** — matching the other backends' glyph metrics/UVs so text lays out identically.

Not on the list: performance (the game's 2D workload is trivial for Metal) and shader correctness (SPIRV-Cross
MSL from the already-validated SPIR-V is reliable).
