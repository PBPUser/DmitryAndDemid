# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Touhou-style danmaku (bullet-hell) game written in C# on .NET 10, rendering through Raylib-cs. Single project, no solution file, no test suite.

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

## Persistence

`Utils/BitPackage` is a hand-rolled varint binary reader/writer (7-bit continuation byte, sign bit `0x40`). It backs everything binary:
- `scoreaag2.gsy` — `PlayerData.Instance`, a lazily-loaded static singleton that **saves on every mutation** (each unlock setter calls `Save()`). Holds high scores, unlocked stages/music/nicknames, per-character spell-card try counts.
- `Replays/*.rpy` — a JSON header plus one packed input byte per tick. `PlayerController.Update` writes the bitfield (left/right/up/down/focus/shoot/bomb) into `Movements[tick]`; `ReplayController` replays it. Both derive from `PlayerControllerBase`, so gameplay code is agnostic to which is driving.
- `Assets/Data/SpellCards/*.sid` — spell cards authored by `StageEditorScreen`.

Everything else is JSON under `Assets/Data/` (bullet visuals, entity visuals, playable characters, stages, endings, trophies, music descriptions) plus `config.json` at the root.

## Text and localization

`Helper` loads `Assets/Data/translation.json` and `Assets/Data/cyrilic-transliteration-table.json`. Note that many literals in the C# source are Cyrillic text typed with Latin lookalikes (e.g. `"HeBo3MoJKHo uHutsuAJlu3upoBaTb"` = «Невозможно инициализировать»). That is deliberate — the game's font/aesthetic depends on it. Don't "fix" these strings to real Latin words.
