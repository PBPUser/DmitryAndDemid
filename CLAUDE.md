# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Touhou-style danmaku (bullet-hell) game written in C# on .NET 10, rendering through Raylib-cs. Single project, no solution file, no test suite.

The engine it runs on is part of this repo and has a name — in fact two, used interchangeably and with no difference in meaning:

> **the Nikitos Engine**, also **the Lihanov Engine**

Both are after Никита Лиханов (nikitos), stage 1's boss. Either is correct; the project has never settled on one and does not intend to, so prose, comments and docs mix them freely. What is *not* free-form is the spelling: anything that puts the name on screen or in a log reads it from `Engine.Name` / `Engine.AlternateName` (`Rendering/Engine.cs`) rather than typing a literal, which is what keeps the two names from quietly becoming four.

Its three parts are named too, and those names are the ones to use when you mean the part rather than the whole:

| Part | Name | Constant | Where it lives |
|---|---|---|---|
| Graphics | **Likhanov32D** — the "32D" is 3D *and* 2D | `Engine.GraphicsName` | `Rendering/Gfx.cs`, `IRenderer`, the backends |
| Sound | **Demidonic** | `Engine.AudioName` | `IAudio` (`Rendering/IPlatform.cs`) and its backend implementations |
| Physics | **Pizzics** — pizza + physics | `Engine.PhysicsName` | the collision sweep in `GameBox`, `Helper.IsCollied` |

Watch the spelling of the graphics one: **Likh**anov32D, but **Lih**anov Engine. They grew separately and neither is a typo of the other — don't "fix" either to match.

Pizzics has no folder: every collision in the game is a distance test between two radii, inline in `GameBox`'s per-tick sweep. The name exists to talk about that code, not as a plan to extract it. Likhanov32D rasterises in 2D only — the 3D half of its name is raymarching inside fragment shaders (`Assets/Shaders/houses.fs` is the reference case), not a geometry pipeline.

**The engine is not the backend.** `Engine.Name` is the Nikitos Engine; `Engine.BackendName` is Raylib / Silk-GL / Vulkan / Metal / an SDL flavour on Switch — whichever it happens to be running on this launch. The window title, the startup splash and the debug overlay all print both, side by side, for exactly that reason. When you write "the engine", mean the seam (`Rendering/Engine.cs`, `Gfx`, `IRenderer`), not the thing behind it.

## Commands

```bash
dotnet build                  # Debug by default
dotnet run                    # build + launch the game
dotnet build -c Release       # Release: strips all the DEBUG tooling below
```

There are no tests, no linter, and no CI. Verification is running the game.

`Debug` and `Release` are meaningfully different builds, not just optimization levels. `#if DEBUG` gates:
- ImGui overlays (`Screen.DrawImgui`, called only on the topmost screen)
- The texture/shader previewer in `Runtime.cs` (Ctrl+J to open, arrows to cycle textures/shaders, `S` to poke uniforms)
- Cheat/spawn keybinds inside `GameBox.BoxUpdate` (RightShift+key to spawn items, adjust lives, etc.)
- A 500 ms loading screen instead of the real 3 s / 33 s wait

Native prerequisites: GTK 3 (used for the pre-launch config dialog and error popups) and an OpenGL-capable display.

`bin/` and `obj/` are **committed to git** — there is no `.gitignore`. Build artifacts show up as modified files in every `git status`; that is expected, not something to fix unless asked.

## Startup and the Runtime singleton

`Program.cs` → if `config.json` has `AlwaysAsk`, open the GTK `PreconfigWindow`; otherwise construct `Runtime` and `Start()`.

`Runtime.CurrentRuntime` is a global singleton reachable from everywhere. It owns the window, the main loop, and every asset dictionary. Assets are loaded by **scanning directories at startup**, so adding an asset means dropping a file in the right folder — there is no manifest to update. The key convention differs per type, and this is a frequent source of lookup bugs:

| Dictionary | Source | Key |
|---|---|---|
| `Textures` | `Assets/Textures/*.png` | filename **with** extension (`"241fps.png"`) |
| `Shaders` | `Assets/Shaders/*.fs` | filename **without** extension |
| `Sounds` | `Assets/Sounds/*` | filename without extension |
| `Fonts` | `Assets/Fonts/*` | filename without extension |
| `BulletVisualPresets` | `Assets/Data/BulletVisuals/*.json` | filename without extension |

A fragment shader is paired with a same-named `.vs` if one exists, otherwise with `Assets/Shaders/base.vs`.

## The Nikitos Engine (rendering, platform, input, audio)

Everything under `Rendering/` is the engine — the Nikitos Engine, or the Lihanov Engine, whichever you feel like calling it that day. Three pieces:

- `Rendering/Engine.cs` — the front door and the only place a backend is chosen. Holds every name constant (`Name`, `AlternateName`, `GraphicsName`, `AudioName`, `PhysicsName`) plus `BackendName` (what it is running on this launch).
- `Rendering/Gfx.cs` — Likhanov32D's drawing API, what everything else calls via `using static DmitryAndDemid.Rendering.Gfx;`. Method names mirror Raylib's because they used to *be* Raylib's; every one is a thin forward to the active backend.
- `Rendering/IRenderer.cs` + `IBackend` — the contract a backend signs (with `IAudio`, Demidonic's half of it, in `IPlatform.cs`). No backend type appears in any signature anywhere in the game, which is what makes a second renderer possible at all.

Backends: Raylib-cs (desktop default), Silk.NET/OpenGL, Vulkan, Metal (`#if METAL`, scaffold), and SDL/SDL-GL/deko3d on Switch (`#if SWITCH`). `RendererRegistry` lists the ones that exist and is deliberately backend-dependency-free so the GTK configurator can link it without loading a graphics stack.

**The engine is not the backend**, and the names are drawn side by side so nobody confuses them: the window title (`Nikitos Engine / <backend>`), the GTK configurator's title and renderer row, the startup splash — which is the engine credit proper, both engine names under the sugar logo with `Likhanov32D · Demidonic · Pizzics` beneath — and the debug overlay, which uses the Lihanov spelling. All read the constants; `Tests/EngineNameTests.cs` fails the build if any of them is ever typed out as a literal instead.

## Screen stack

`Runtime` holds a `List<Screen>` rendered bottom-to-top. Add/remove goes through `AddScreen`/`RemoveScreen`, which **queue** the change; it is applied in `RefreshScreens()` at the top of the next frame, so it is safe to call from inside a screen's own update. Only `Screens.Last()` receives `TopUpdate()` and `DrawImgui()`. `SetScreenRenderingFrom(i)` skips rendering everything below index `i` (used when an opaque screen fully covers the ones beneath).

`Screens/` holds concrete screens (main menu, difficulty, gameplay, pause, endings, music room, trophies, replay saving) plus the in-game editors (`StageEditorScreen`, `GameplayEditorScreen`, `DropEditorScreen`). `Common/` holds the base classes: `Screen`, `MenuScreen`, `ScreenWithTitle`, `GameplayOverlay`, `GameplayScreenEffect`, `StageBackground`.

## Gameplay core

`GameBox` is the simulation. It runs a **fixed 60 TPS tick** derived from wall time (`CurrentTick` catches up to `GetTime() * TargetTPS`), decoupled from render framerate. Rendering targets fixed-size render textures: 384×448 for the playfield, 224×480 for the left UI strip, scaled to the window (`Runtime.Scale = width / 640`). Gameplay coordinates are always in that 384×448 space regardless of resolution.

A stage is a list of **chapters** (`FileChapterInfo`) played in sequence with `DelayBetweenChapters` (120 ticks) between them. Chapter types include normal, non-spell, and spell cards (timed, scored by remaining time).

### RuntimeObject: the flat-array entity model

Every bullet, enemy, boss, and collectable is a `RuntimeObject` — one class whose entire state lives in `int[128] Header` and `float[128] FloatingPoints`, addressed by hard-coded hex indices (`obj.Header[0x50]`, `obj.FloatingPoints[0x5C]`). `Header[0]` is a bit mask whose meaning **overloads by object kind** (`FlagIsGroupParent` and `FlagOverrideColor` are both `0x0004`, `FlagUseDieScript` and `FlagDangerousRelatedToEnemy` are both `0x0400`, etc.).

**The `.sp` files are the schema.** They are plain-text field maps, not code, and they are the only documentation of what each index means:
- `Gameplay/RuntimeData/RuntimeObject.sp` — runtime object header + floats
- `Gameplay/RuntimeEntityObject.sp` — the newer/entity-side layout
- `Data/Archive/File*.sp` — the on-disk layouts (`FileStageInfo`, `FileChapterInfo`, `FileEntityInfo`, `FileDialogInfo`)
- `Data/Drop.sp` — item-drop bit mask
- `Data/CommandsSpecification`, `Data/ChapterInformationSpecification` — a bytecode opcode table and chapter header spec

Read the relevant `.sp` before touching any magic index, and update it when you add a field. Named flag constants live at the top of `RuntimeObject.cs`; prefer them over raw literals.

### Behavior scripting

Entity and chapter behavior is **not** data-driven at runtime — it's a string→delegate registry. `Gameplay/RuntimeData/ActionsScope.cs` holds two `FrozenDictionary`s (`ObjectActions`, `ChapterActions`) mapping names like `"nikitos#spell1"` or `"MysticalToilet"` to C# lambdas. Data files reference behavior *by that string*, and `RuntimeObject.LoadFromFile` looks it up. **Adding a new enemy pattern means adding an entry to `ActionsScope`** — a name in a data file with no matching key throws on load.

`RuntimeStageInfo` additionally compiles stage-level `Scripts` through Roslyn (`CSharpScript.Create`), and `Microsoft.CodeAnalysis.CSharp.Scripting` / `LuaCSharp` are referenced for that path, but the shipped content goes through `ActionsScope`.

## DualSense

Pads are read generically by the backends (GLFW/SDL), and that is untouched. `Utils/DualSense/` adds the parts a
generic pad API cannot reach on Linux: rumble through evdev force feedback, the lightbar / player LEDs and the
adaptive triggers through the raw HID output report (`/dev/hidraw*`, which needs `Tools/99-dualsense.rules`), and
PlayStation button labels in the rebinding screen. `Gameplay/DualSenseFeedback.cs` maps game state onto all of it.

Every piece is optional and fails to a no-op — no pad, no permission, or a non-Linux host each just turn it off.
Verify against real hardware with `dotnet bin/Debug/net10.0/aag2.dll --dualsense-test` (headless, like
`--selftest`); the pure parts (report layout, CRC-32, sysfs walk) are covered by `Tests/DualSenseTests.cs`.
Full write-up in `docs/dualsense.md`.

## Persistence

`Utils/BitPackage` is a hand-rolled varint binary reader/writer (7-bit continuation byte, sign bit `0x40`). It backs everything binary:
- `scoreaag2.gsy` — `PlayerData.Instance`, a lazily-loaded static singleton that **saves on every mutation** (each unlock setter calls `Save()`). Holds high scores, unlocked stages/music/nicknames, per-character spell-card try counts.
- `Replays/*.rpy` — a JSON header plus one packed input byte per tick. `PlayerController.Update` writes the bitfield (left/right/up/down/focus/shoot/bomb) into `Movements[tick]`; `ReplayController` replays it. Both derive from `PlayerControllerBase`, so gameplay code is agnostic to which is driving.
- `Assets/Data/SpellCards/*.sid` — spell cards authored by `StageEditorScreen`.
- `*.negr` — the in-project image format (`Data/Archive/CpuImage.cs`). Unlike the above, it is a **block** stream rather than a fixed field order: `[type:1][length:varint][payload]` repeated to an END block, where bit `0x80` of the type byte says whether a reader that does not know the type must fail or skip it by its length. The abstract `ImageBlock` (`Rendering/ImageBlock.cs`) owns all the framing; each concrete block is a subclass in `Rendering/ImageBlocks.cs` declaring its id with `[ImageBlock(id)]`, found by reflection — **adding a block type means adding a class with that attribute, and nothing else**. Spec in `Data/Archive/CpuImage.sp`; both the id table and the spec's worked example are pinned by `Tests/CpuImageFormatTests.cs`.

  The picture itself is a grid of **16×16 tiles**, row-major, one block each and no coordinates on them — the Nth tile block is the Nth cell, so the file must hold exactly `ceil(W/16) * ceil(H/16)` of them. Edge tiles are still whole 16×16 blocks that overhang the image; their outside pixels are written as zero and discarded on read. Ids `0x00–0x0F` are the file's structure (END, RESOLUTION, MANIFEST, METADATA); **ids `0x10` and up are tile encodings**, interchangeable ways of spelling one patch that all decode to the same 1024 bytes of RGBA8888, so a writer may pick a different one per tile. Only `TILE_RAW8` (`0x10`) exists so far: the colours uncompressed, 3 bytes per pixel or 4 depending on `ALPHA_ENABLED` in the MANIFEST block — which is why the manifest must precede any tile, as it is what gives a tile payload its length.

Everything else is JSON under `Assets/Data/` (bullet visuals, entity visuals, playable characters, stages, endings, trophies, music descriptions) plus `config.json` at the root.

## Text and localization

`Helper` loads `Assets/Data/translation.json` and `Assets/Data/cyrilic-transliteration-table.json`. Note that many literals in the C# source are Cyrillic text typed with Latin lookalikes (e.g. `"HeBo3MoJKHo uHutsuAJlu3upoBaTb"` = «Невозможно инициализировать»). That is deliberate — the game's font/aesthetic depends on it. Don't "fix" these strings to real Latin words.
