Do task, if you have a questions, add to NEEDS ATTENTION, after you completed task, compact your context and move to next

# TODO
fix bug that produces when user runs benchmark from settings, settings dosen't hide during bench

when player play main or extra story after they exited from the game, they should be directed into main menu, if they played replay, or any from of practice - keep current behavior

fix fade from black in manual menu when user opened manual and fix fade to black when user closes manual

add floating move for list of items and scale image from selected item and scale it down into it when user opens/closes manual

add confirmation when player restarts or exits from the game through pause menu ITEMS (not hotkeys), with yes and no answers

add subtitles for pause menu items

# COMPLETED

* add completed task there and mark it with star
* manual should use manual title and have open/close animations
* and use `manual-title.png` as a title in manual menu
* change sound that plays when player moves cursor via escape to `esc.mp3`
* game should play sound called extend.mp3 when player takes a FULL heart (not piece)
* fix bug that pause menu closes by escape, but game still in paused state and green "blur" shader still applied
* fix bug that save replay screen in pause menu when you press enter, doesn't have a delay before next action and opens it again
* spell practice stage/card/difficulty selection screens no longer have a quit button; Escape / X (Back on Android) closes them
* render menu items above pizza in main menu
* laser start point is now a bright white->beam-colour->transparent radial gradient (baked from emit_circle at startup), instead of the star
* fix esc/x on benchmark moving the settings cursor to quit, AND statistics-escape moving the main-menu selection to exit (MenuScreen now arms the shared input cooldown whenever it re-activates)
* pause menu no longer replays its open animation + pause sting when a sub-screen it opened (save-replay / manual / settings) closes
* R restarts and Q quits immediately from the pause menu, no confirmation
* saving a replay now plays extend.mp3 as a "saved!" cue
* spellcard top-right static: labels keep their colour, the score value is now white (red on a failed clean clear); the attempt value was already white
* debug builds show "renderer  vVersion  bBuild" at the top-right of the game area
* version bumped to 0.03a
* pause menu now plays a close/slide-out animation (fork + pause board + menu slide off, ~0.28s) on Escape / Continue-resume before it's removed; Q and exit-to-menu stay instant
* dialog window forks are now cut (clamped inside the panel so rotated forks no longer overhang onto the playfield) and dimmed (mixed toward the panel dark, alpha 130→70) so they sit behind the text as background dressing — no cross-backend scissor exists, so "cut" is a fit-to-panel clamp; say if you meant a hard clipped edge
* benchmark results now show a SYSTEM column (right side of StatisticsScreen): OS, arch, CPU name, cores (physical C / logical T + hybrid topology e.g. "6 P-cores + 8 E-cores"), max clock, total RAM, GPU name + API + VRAM, GPU extension count, NPU. Platform-specific collector = Utils/SystemInfo.cs (Linux /proc+/sys, Windows GlobalMemoryStatusEx P/Invoke, portable RuntimeInformation fallback). GPU comes from a new IRenderer.QueryGpuInfo() default-interface hook implemented in SilkGL (GL strings+extensions) and Vulkan (device name+API version) backends, both guarded; VRAM falls back to AMD sysfs on Linux. RAM/VRAM clock + (often) NPU need root/vendor tools → shown "—"/omitted honestly. Verified on this box (i5-14600KF → "14C / 20T  6 P-cores + 8 E-cores", 62.6 GB, Arch Linux) via Tests/SystemInfoTests.cs.
* unit-test ARCHITECTURE created: new Tests/ xUnit project (added to the .sln), headless philosophy = never touch the GPU, drive the game through the Assets.Source seam (Tests/README.md). Texture-count guard done as headless TextureManifest (Utils/TextureManifest.cs = GPU-free mirror of Runtime.LoadTextures) + a DEBUG self-check in LoadTextures that throws on boot if the live Textures dict drifts from the manifest. Tests: registry is deterministic across reloads / no duplicate keys / count == scanned+procedural / no file-vs-procedural collision, plus asset-seam smoke tests. Run: `dotnet test Tests/DmitryAndDemid.Tests.csproj` (8 pass, 1 GPU integration test skipped). NOTE: had to add `<Compile Remove="Tests\**" />` to the main csproj (it SDK-globs the repo root).

# NEEDS ATTENTION

* type your questions there
* "return to main menu after main/extra story exit": there's no clean reset-to-main helper (SwitchToMain only adds a MainScreen over the startup loading screen; it doesn't clear the run + person-select/difficulty stack below the gameplay). To do this I'd add a Runtime method that clears the whole screen stack and adds a fresh MainScreen, gated on GameBox.Mode == Default/Extra (replay + practice keep current behavior). OK to clear the whole stack that way? And should it fire on the pause-menu Exit / Q-quit only, or also on normal story completion (results/ending)?
* "settings doesn't hide during bench": BenchmarkScreen.Render already does ClearBackground(Black) over the settings behind it, so I couldn't reproduce a leak from the code. What exactly stays visible (the settings menu list?), and for how long (just the first 1-2 frames before the run, or the whole time)?
* manual fade: ManualScreen already fades up from black on open and down to black on close (black veil driven by `shown`). If it looks wrong, what specifically — no fade at all, wrong timing, a flash, or the wrong screen showing through? I can't repro from the code.
* "add extra difficulty to statistics menu": the score/records screen (ScoreScreen) already cycles all 5 difficulties including Extra (PersonPlayerData.DifficultyCount == 5, Up/Down steps through 0..4 = Easy/Normal/Hard/Max/Extra). Which screen do you mean — is there a different "statistics" screen, or does Extra fail to show there (e.g. because story runs never record a score into the Extra board so it always reads empty)? What exactly is missing?
* "do not count spell card difficulties if spell card doesn't have behavior for it" + "display only extra card if spell card contains it": SpellPracticeDifficultyScreen already dims tiers with no defined NAME (Helper.SpellcardDifficultyName(Number,d) == null → "nothing" placeholder, disabled). But it still (a) reserves NumbersPerCard (6) numbering slots per card even for the missing tiers, and (b) always shows Easy..Max as a 4-tier row. To do this right I need to know: what is the source of truth for "a card HAS a behavior for difficulty d"? Is it the same SpellcardDifficultyName name, or a separate per-difficulty behavior string on the chapter that gets looked up in ActionsScope.ObjectActions/ChapterActions? And for "display only extra card if it contains it" — how does a card mark itself Extra-only (a flag on FileChapterInfo, an Extra behavior name, or the presence of a 5th/Extra tier)? Point me at the field and I'll wire both.
* "use button called NAZAD in controlls.png for pause menu": I can't do this without the sprite atlas layout — controlls.png presumably packs several button glyphs; tell me the NAZAD button's source rect (x,y,w,h in the png), where on the pause screen it should sit, and whether it's purely decorative or a tappable Back control. Then it's a quick DrawTexturePro.
* "add a screen with question when player restarts or quits the game from main menu": there's no confirmation-screen pattern in the codebase yet, and the main menu's only leave action is menu.exit → Environment.Exit(0) (no "restart" entry exists on the main menu — restart is a pause-menu concept). Questions: (1) is "restart" here the pause-menu restart, or does the main menu need a new restart entry too? (2) OK for me to build a small reusable Yes/No confirm MenuScreen (semi-transparent modal, "menu.confirm_quit" translation + yes/no items) and gate Environment.Exit(0) behind it? (3) what translation keys / wording do you want for the prompt and the two answers?

