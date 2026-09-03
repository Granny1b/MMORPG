# CLAUDE.md

Operating manual for AI agents working in this repository. Read this before changing anything.

`MMORPG Granny` is a Unity 6 game built on Suriyun's **MMORPG KIT** (the `Assets/UnityMultiplayerARPG/` tree).
The kit is vendored, not authored here: 2,507 of the project's 2,514 C# files come from it. Seven are ours.
Treat the kit as a third-party dependency that happens to live in the repo.

- Unity 6000.3.13f1, Universal Render Pipeline 17.3.0
- Input System 1.19.0 with the legacy Input Manager also active (`activeInputHandler: 2`, "Both")
- Official kit documentation: https://suriyun-production.github.io/mmorpg-kit-docs/
- **The source code in this repo always wins over the kit's online docs.** The kit here is a GitHub mirror, newer than the Asset Store release the docs describe.

## Hard rules

1. **Never edit anything under `Assets/UnityMultiplayerARPG/Core/` or `Assets/UnityMultiplayerARPG/MMO/`.** Both trees are mirrored wholesale from GitHub during kit updates. Edits there are silently destroyed. Two exceptions already exist and are documented below; do not add a third without recording it.
2. **Never edit anything under `Assets/UnityMultiplayerARPG/Demo*/` without logging it.** Those trees survive a GitHub mirror but are destroyed by an Asset Store re-import. Several already carry project edits.
3. **New work goes in `Assets/1. Data/`, `Assets/Scripts/` or `Assets/TopDownController/`.** Prefer forking a kit prefab into `Assets/1. Data/` over editing it in place.
4. **Every non-trivial change gets a `CHANGELOG.md` entry.** See the changelog section below. This is the most important convention in the repo.
5. **Do not commit purchased art.** See the fresh clone section.
6. **Verify against the source before describing kit behaviour.** The kit is large and the online docs lag it.

## Layout

| Path | What it is | Ownership |
|---|---|---|
| `Assets/UnityMultiplayerARPG/Core/` | Gameplay framework plus 14 sub-libraries (LiteNetLibManager, UniTask, xNode, CameraAndInput, SharedData, ...) | Vendored, mirrors `suriyun-mmorpg/UnityMultiplayerARPG_Core` at commit `2830829` |
| `Assets/UnityMultiplayerARPG/MMO/` | Multi-server MMO layer, database, SQL schema | Vendored, mirrors `suriyun-mmorpg/UnityMultiplayerARPG_MMO` at commit `cbccdcf` |
| `Assets/UnityMultiplayerARPG/GuildWar/` | Guild war add-on, talks to an external Node.js service | Vendored, matches `UnityMultiplayerARPG_GuildWar` main |
| `Assets/UnityMultiplayerARPG/Demo*/`, `MMO/Demo*/` | Asset Store demo content: game data, prefabs, scenes | Vendored, February 2026 era, **no public repo**, partly edited by us |
| `Assets/1. Data/` | Our game data, prefabs, scenes, UI forks | Ours |
| `Assets/Scripts/` | Our runtime and editor code (6 files) | Ours |
| `Assets/TopDownController/` | Our cursor-aim controller and camera prefabs | Ours |
| `Assets/Settings/` | URP pipeline assets and quality levels | Ours |
| `CHANGELOG.md` | Decision log | Ours |
| `Documentation/` | Architecture reference, see the end of this file | Ours |

**Assembly note:** there are no assembly definitions on kit gameplay code or on our code. Everything compiles into the default `Assembly-CSharp`. This is why `partial class` extensions and `[DevExtMethods]` hooks on kit types work from `Assets/Scripts/` without any wiring.

## Where things go

- **New game data** (item, skill, quest, NPC, map, monster): create the asset under `Assets/1. Data/GameData/<Category>/` with a `_G` suffix, then register it in `Assets/1. Data/GameDatabase_G.asset`. An unregistered asset does not exist at runtime.
- **Do not put game data under a `Resources/` folder.** We use the explicit-list `GameDatabase`, not `ResourcesFolderGameDatabase`. Anything under `Resources/` is force-included in every build whether referenced or not. The kit's own data does live under `Resources/`; that is its convention, not ours.
- **New runtime behaviour**: subclass a kit type (as `TopDownAimController` does with `PlayerCharacterController`), or add a side component that reads public kit API (as `LocomotionPhaseSync` and `ActionLayerMaskUpdater` do), or add a `partial class` with `[DevExtMethods("Awake")]` hooks. Never fork a kit script.
- **Changing rules and formulas** (damage, exp, gold, drop rates): subclass `BaseGameplayRule` and assign it on `GameInstance`. Do not edit `DefaultGameplayRule`.
- **New UI window**: add it under the forked `Assets/1. Data/Prefabs/UI Prefabs/UIDialogs_G.prefab`. `UIEscapeWindowsHandler` collects windows automatically at `Awake`, so Escape handling needs no wiring.
- **Editor tooling**: `Assets/Scripts/Editor/`, namespace `MMORPGGranny.EditorTools`, menu path under `Tools/`.

## The changelog

`CHANGELOG.md` at the repo root is the project's memory. It exists because the reasoning behind a change is not recoverable from the diff, and because this project sits on a vendored framework where "why did we do it this way" usually means "what did the kit force on us".

**Format.** Keep a Changelog, with entries grouped under `## [Unreleased]` into `### Added`, `### Changed`, `### Fixed` and `### Removed`. Two extra sections live at the bottom: `## Environment notes` for things that affected the editor or machine rather than the code, and `## Known follow-ups` for deliberate loose ends.

**An entry names the artifact, dates it, and then explains itself.** The pattern in use:

```markdown
- **`Assets/path/To/Thing.cs`** (2026-08-30) — one-line summary of what changed.
  - Why this way and not the obvious alternative, with the mechanism that forced it.
  - What was measured or tested, with the actual numbers.
  - What was rejected and why, so nobody retries it.
  - **This edits a stock kit asset** — a kit update would revert it.
```

Rules that matter:

- **Record the rejected alternative.** Half the value of this log is stopping a future agent from "fixing" a deliberate choice. Existing entries explain why the Synty combo clips were unusable and why an item's `EquipmentModel` carries the grip offsets instead of the `WeaponR` anchor. Both read as odd choices without the reasoning.
- **Flag every edit to a kit file in bold**, with the consequence. These entries are the recovery checklist after a kit update.
- **Cite the mechanism, not the symptom.** "Movement froze during attacks because `moveSpeedRateWhileAttacking` multiplies move speed directly and defaults to 0" beats "fixed attack movement".
- **Keep measurements.** Where a choice came from sampling clips or comparing poses, the numbers are in the log. They justify the choice and let it be re-derived for a different asset set.
- Dated entries are historical claims and do not go stale. Do not rewrite old entries when behaviour later changes; add a new one.

## Kit files we have edited in place

These are reverted by a kit update. After any kit mirror or Asset Store re-import, re-apply them and check `CHANGELOG.md` for the current list.

**In `Core/`, highest risk:**
- `Core/CameraAndInput/Scripts/Input/InputManager.cs`: `GetAxis` no longer falls back to the legacy Input Manager when an Input System action exists. Without this, mouse wheel zoom jumps by 100x on fall-through frames.
- `Core/CameraAndInput/Scripts/Camera/FollowCameraControls.cs`: camera prefs saved in `SaveCameraPrefs()` from `OnDisable` and `OnApplicationQuit` instead of `PlayerPrefs.Save()` every frame.

**In `Demo/`:** `CanvasGameplay.prefab`, `UIItemsDialog.prefab`, `GameData/GameDatabase.asset`, `Resources/PlayerCharacters/Warrior.asset`, `Resources/WeaponTypes/TwoHandSword.asset`, `InputActions.inputactions`, `Textures/MinimapRenderTexture.asset`, `Scenes/00Init.unity`, and the two `Scripts/DevExt/DevExtDemo_*.cs` files.

## Entry points

- **LAN and offline:** build scene 0 is `Assets/UnityMultiplayerARPG/Demo/Scenes/00Init.unity`. Its `GameInstance` points at our `GameDatabase_G.asset`, our `CanvasGameplay_G.prefab` and our `TopDownAimController.prefab`.
- **MMO:** `Assets/UnityMultiplayerARPG/MMO/Demo/Scenes/00Init_MMO.unity`. **It is not wired to our assets.** It still uses the kit's `GameInstance.prefab`, which references the kit demo database, the stock controller and the stock canvas. Expect the MMO flavour to behave differently from the LAN one until this is fixed.
- `GameInstance` runs at execution order `int.MinValue`, creates default services for any null field, then loads the game database asynchronously. Game data lands in static dictionaries keyed by an integer `DataId`.

## Gotchas that have already cost time

- **Renaming a game data asset changes its `DataId` and orphans saved data.** `BaseGameData.Id` returns the serialized `id` field, or the asset name when `id` is empty, and `DataId` is a hash of that string (`BaseGameData.cs:30` and `:180`). Every project asset currently leaves `id` empty, so the name is load-bearing. Set `id` explicitly before renaming anything that players may already own, or accept that existing items, skills and quests referencing it become unresolvable.
- **Attack animation arrays do not fall back.** `GetRightHandAttackAnimations` tests `!= null`, and an empty array is not null. A weapon type with a `weaponAnimations` entry but no attack clip resolves to a zero-length action. Base locomotion states do fall back, because `SetBaseState` skips null clips.
- **`moveSpeedRateWhileAttacking` defaults to 0**, which is a hard movement freeze for the length of every swing. Set it to 1 on every new weapon. `SyntySword001_G` still has the default.
- **`genericAudioSource` null means silent SFX.** `PlayActionAnimationAudioClip` returns early on a null source with nothing logged.
- **`PlayAction` picks the action layer's avatar mask once**, at the start of the swing, and never revisits it. `ActionLayerMaskUpdater` exists to correct this.
- **`DISABLE_ADDRESSABLES` is defined for Standalone.** Every `#if !DISABLE_ADDRESSABLES` path is compiled out of PC builds, so direct prefab references are used instead. The editor compiles both paths, which hides mistakes.
- **`STEAMWORKS_NET` is defined on four platforms but no Steamworks code exists.** It is an inert leftover.
- **Demo content is February-era against August code.** February prefabs and scenes deserializing against changed August code is the first thing to suspect when a demo scene misbehaves.

## Fresh clone setup

Purchased art is excluded by `.gitignore` and must be re-imported **before** opening the project, or prefabs under `Assets/1. Data/` come up with missing meshes, clips and sprites: Synty (`Assets/Synty/`, `Assets/Polygon*/`, `Assets/Interface*/`, `Assets/AnimationBaseLocomotion/`), Action RPG SFX V2, Hovl Studio, Kevin Iglesias, Melee Weapons Pack 1, and the BLINK icons under `Assets/2. Art/Blink/`. Asset GUIDs are stable across re-imports, so references reconnect. Git LFS is used for binary art formats.

## Deeper documentation

`Documentation/` holds architecture reference generated from the source. Start at `Documentation/PROJECT_OVERVIEW.md`.

`Documentation/Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md` is the one to keep current: it is the full inventory of what is ours versus vendored, plus the re-apply checklist after a kit update.

The other files under `Documentation/Systems/` describe vendored kit subsystems. **Treat them as a snapshot, not as truth.** They were written against the kit at the commits named above and will not be updated when the kit is mirrored again. When you need current detail about a kit subsystem, read the source or regenerate the document. When a document and the code disagree, the code is right.
