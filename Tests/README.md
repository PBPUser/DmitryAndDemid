# Tests

Headless unit tests for the game. There was no test suite before this; the project's only prior verification was
running the game (see the top-level `CLAUDE.md`).

## Running

```bash
dotnet test Tests/DmitryAndDemid.Tests.csproj
```

or, from the solution:

```bash
dotnet test DmitryAndDemid.sln
```

## The one rule: no GPU

The game owns a window and a GL/Vulkan context. A test run has neither, so **tests never touch the GPU** — no
`LoadTexture`, no `Runtime` construction, no `BeginDrawing`. Anything that must be verified is reached through the
game's *headless seams*:

- **`Assets.Source`** (`Utils/Assets.cs`) — the `IAssetSource` abstraction the game already uses to read content
  on Android and Switch. `TestEnvironment.UseRepoAssets()` points it at the repository's real `Assets/` folder,
  so tests read exactly what ships. A `ProjectReference` does **not** copy the game's `Assets/**` into the test
  output, which is why the seam (rather than a copied file) is the way in.
- **Pure accounting split out from GPU work** — e.g. `Utils/TextureManifest.cs` computes *which* texture keys get
  registered (the file scan + the fixed procedural entries) without uploading anything. `Runtime.LoadTextures`
  is the GPU consumer of that same list.

`Program.cs`'s `--selftest` is the same idea at the process level: boot far enough to prove the assets resolve
and the backend picks, then exit before opening a window.

## What's covered

| Test file | Guards |
|---|---|
| `TextureRegistryTests.cs` | The texture count is deterministic across reloads, has no duplicate keys, equals `scanned + procedural`, and no file name collides with a procedural key. This is the headless form of "restart the game, compare the total texture count." |
| `AssetSeamTests.cs` | Core startup assets (`translation.json`, `base.vs`, …) resolve through the seam; file enumeration is stable and ordinally sorted (the property that makes the counts reproducible). |

## Keeping the texture guard honest

`TextureManifest` mirrors what `Runtime.LoadTextures` registers. To stop the two from drifting, `LoadTextures`
runs a **DEBUG self-check** on boot: it throws if the live `Textures` dictionary's keys don't equal
`TextureManifest.RegisteredKeys()`. So:

- add or remove a **file** in `Assets/Textures/` → both sides update automatically (both scan the same folder);
- add or remove a **procedural** texture in `LoadTextures` → also add/remove its key in
  `TextureManifest.ProceduralKeys`, or the DEBUG build throws on the next launch.

The full GPU-bound "boot twice, compare `Runtime.Textures.Count`" test exists as a skipped placeholder
(`Live_texture_count_is_stable_across_restart`); it needs a display and is redundant with the DEBUG self-check +
the headless tests above.

## Adding a test

1. New `*.cs` file in this folder, namespace `DmitryAndDemid.Tests`.
2. Call `TestEnvironment.UseRepoAssets()` from the fixture ctor if it reads content.
3. Reach the behaviour through a headless seam. If a piece of logic you want to test is welded to GPU calls,
   split the pure part out (as `TextureManifest` was split from `LoadTextures`) rather than reaching for the GPU.
