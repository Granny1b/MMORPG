# Changelog

All notable changes to this project are recorded here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Paths are relative to the project root (`D:\1. Unity projekt\MMORPG Granny`).

## [Unreleased]

### Added

- **`Assets/Scripts/UI/UIEscapeWindowsHandler.cs`** (2026-08-29) — WoW-style escape handling:
  the first Escape press closes every opened window, the next one toggles the system menu.
  Windows are auto-collected at `Awake` from `windowContainers` (`UIBase` on direct children) and
  `windowObjects` (`UIBase` on the transform itself), with an `excludingWindows` opt-out, so new
  dialogs dropped into `UIDialogs_Standalone` are picked up without re-wiring the inspector.
  After closing anything it calls `UISceneGameplay.HideNpcDialog()`, the only close path that also
  tells the server the player stopped talking to the NPC — a bare `UIBase.Hide()` would leave
  `CurrentNpcDialog` set server-side.
  - Deliberately **not** collected: `UIConstructBuilding` and `UICurrentBuilding`, whose kit close
    paths (`HideConstructBuildingDialog` / `HideCurrentBuildingDialog`) fire callbacks a plain
    `Hide()` would skip, and `UIIsWarping`, which must stay up while warping.
  - The kit's `UIStackManager` / `UIStackEntry` pair was not reused: `UIStackEntry` is attached to
    nothing in the project, so its static stack is always empty, and it pops only one entry anyway.

- **`blockAttackWhenCursorOverUI`** on `TopDownAimController` (2026-08-29) — stops a click
  on inventory/shop/hotkey UI from also swinging the weapon, now that M1 is bound to Attack.
  `UpdateWASDAttack` is not virtual and reads `InputManager.GetButton("Attack")` directly, so
  `UpdateInput` (which is virtual) mutes the "Attack" input action around the base call
  instead of trying to intercept the attack. Restored in a `finally` block, so the action
  can never be left disabled. Relies on there being no `Attack` axis in the legacy Input
  Manager — if one is ever added, `InputManager.GetButton` would fall back to it and this
  suppression would stop working.

- **Top-down controller addon** (2026-08-28) — installed
  [UnityMultiplayerARPG_TopDownController](https://github.com/suriyun-mmorpg/UnityMultiplayerARPG_TopDownController)
  into `Assets/TopDownController/`. Only the camera prefab was kept; see *Removed*.
- **`Assets/TopDownController/Demo/Prefabs/TopDownWasdPlayerCharacterController.prefab`** (2026-08-28) —
  copy of the kit's stock `PlayerCharacterController.prefab` with two changes:
  `controllerMode` `Both` → `WASD`, and `gameplayCameraPrefab` → `TopDownGameplayCamera`.
  Superseded as the active controller by `TopDownAimController`, kept as a fallback.
- **`Assets/TopDownController/Scripts/TopDownAimController.cs`** (2026-08-29) — subclass of
  `PlayerCharacterController` adding top-down cursor aiming. The character turns to face the
  mouse cursor, projected onto a horizontal plane at its feet (a plane rather than a physics
  raycast, so aiming still works over pits, water and gaps in geometry). WASD movement,
  activating and hotkeys are inherited unchanged; `GetMoveDirection` is deliberately untouched
  so movement stays camera-relative.
  - Exposes `AimWorldPosition` and `CurrentAimPosition` (a ready-built `AimPosition`) for
    wiring cursor-aimed skillshots into `UseHotkey` later.
  - Inspector fields: `Face Cursor`, `Turn Immediately`, `Strict Cursor Aim`,
    `Face Cursor While Doing Action`, `Ignore While Cursor Over UI`,
    `Aim Plane Height Offset`, `Min Aim Distance`.
- **`Assets/TopDownController/Demo/Prefabs/TopDownAimController.prefab`** (2026-08-29) — cloned
  from the WASD prefab with the script reference swapped, so all tuned values carried over
  (`controllerMode=WASD`, top-down camera, minimap camera, target object, `wasdLockAttackTarget=False`).
- **This changelog** (2026-08-29).

### Changed

- **`Assets/UnityMultiplayerARPG/Demo/Prefabs/UI/_Gameplay/CanvasGameplay.prefab`** (2026-08-29) —
  **stock kit asset.** Added `UIEscapeWindowsHandler` to the canvas root and removed the
  `keyCode: 27` entry from `UISceneGameplay.toggleUis`, which used to toggle `UISystemDialog`
  directly. Both must not read Escape in the same frame, or the menu would open while the windows
  are closing. Wired containers: `UIDialogs_Standalone`, `UIInAppPurchase`, `UIMailLayout`,
  `UICraftingLayout`; object: `UISettingDialog`; system menu: `UISystemDialog`.
  `CanvasGameplayMobile.prefab` and the two Survival canvases still carry the stock Escape entry.

- **MMORPG KIT updated from the creator's GitHub** (2026-08-29) — the single largest change in
  this log. The Asset Store package was ~7 months stale: `Core` matched upstream commit
  `7876b7e` (2026-02-02) byte-for-byte and was **368 commits / 300 files behind** `main`.
  - `Core/` mirrored from [UnityMultiplayerARPG_Core](https://github.com/suriyun-mmorpg/UnityMultiplayerARPG_Core)
    @ `2830829`, including all 14 submodules (`LiteNetLibManager`, `CameraAndInput`, `SharedData`,
    `xNode`, `UpdateManager`, …). 2,982 files touched.
  - `MMO/` mirrored from [UnityMultiplayerARPG_MMO](https://github.com/suriyun-mmorpg/UnityMultiplayerARPG_MMO)
    @ `cbccdcf`, with the `MMOSource` and `DatabaseManagerSource` submodules. 426 files touched.
  - Totals: 3,173 modified, 125 deleted, 115 added.
  - **Verified safe before applying:** every `.cs` `.meta` GUID is identical between the store
    version and upstream (0 mismatches across 2,577 shared files in Core, 0 in MMO), so prefab and
    scene references survived intact. The only 12 GUID differences were folder `.meta` files under
    `LiteNetLibManager/Plugins/UniTask`.
  - Notable upstream API changes that landed: `EntityInfo` is now pooled (`_info.SetEntityInfo(...)`
    instead of a 10-arg constructor) as part of the GC.Alloc reduction work; entity event delegates
    gained a leading `target` parameter; `IGameEntity attacker` became `EntityInfo`;
    `NetManager.*` renamed to `LiteNetManager.*`; ZLogger dropped from `LiteNetLibManager/Plugins`;
    `Editor/Addressables/` renamed to `Editor/AssetTools/`; `ICharacterData` / `IPlayerCharacterData`
    moved from `Core/Scripts/CharacterData/` into the `SharedData` submodule.
  - **Not updated:** `Demo/`, `Demo2D/`, `DemoShooter/`, `DemoSurvival/`, `DemoGuildWar/` have no
    public repo and remain February-era Asset Store content. This is the most likely source of
    future oddities — see Known follow-ups.
  - Rollback: the pre-update state is commit `e3a5a32` on branch `topdown-controller`.

- **`Assets/UnityMultiplayerARPG/Core/CameraAndInput/Scripts/Camera/FollowCameraControls.cs`**
  (2026-08-29) — moved the camera-state save out of `Update()`, which was calling
  `PlayerPrefs.Save()` (a disk/registry write) every frame while `isSaveCamera` was on.
  Extracted into `SaveCameraPrefs()`, called from `OnDisable()` and `OnApplicationQuit()`.
  Guarded with `Application.isPlaying` because the class is `[ExecuteInEditMode]`, so those
  callbacks also fire on editor domain reloads and would otherwise overwrite the saved
  value with edit-mode state. Deliberately NOT hooked to `OnDestroy`: the base
  `FollowCamera` declares a private `OnDestroy`, and adding one in the subclass would hide
  it from Unity's message dispatch and leak its `_targetFollower` cleanup.
  **This edits a stock kit asset** — a kit update would revert it.

- **`Assets/TopDownController/Demo/Prefabs/TopDownGameplayCamera.prefab`** (2026-08-29) —
  `isSaveCamera` `false` → `true`, so zoom/rotation persist between sessions.
  `FollowCameraControls.Start()` restores `xRotation`, `yRotation` and `zoomDistance` from
  PlayerPrefs under the `GAMEPLAY` prefix; only zoom actually varies here, since both
  rotations are clamped to a single value. Note the kit calls `PlayerPrefs.Save()` every
  frame in `Update()` while this is enabled — see Known follow-ups.

- **`Assets/UnityMultiplayerARPG/Demo/Scenes/00Init.unity`** (2026-08-28 → 29) — GameInstance
  `Default Controller Prefab`: `PlayerCharacterController` → `TopDownWasdPlayerCharacterController`
  → `TopDownAimController`. All player character entity prefabs have `Controller Prefab` set to
  `None`, so this single field drives every character.
- **`Assets/TopDownController/Demo/Prefabs/TopDownGameplayCamera.prefab`** (2026-08-28)
  - `minZoomDistance` / `maxZoomDistance`: `12` / `12` → `6` / `25`. They were identical, so
    `FollowCameraControls` clamped every scroll input back to 12 and zoom appeared broken.
  - `zoomSpeed`: `-5` → `-100` (by Sam). The Input System scroll path reports a much smaller
    per-notch delta than the legacy axis, so the original value was imperceptible.
    May need retuning on other mice/platforms.
- **`Assets/UnityMultiplayerARPG/Demo/Textures/MinimapRenderTexture.asset`** (2026-08-28) —
  `depthStencilFormat` `None` → `D16_UNorm` (depth `0` → `16`). Unity 6's Render Graph requires
  every camera output texture to have a depth buffer; without one the minimap camera logged an
  error every frame. **This edits a stock kit asset** — revert this one field if the kit is
  ever reimported or updated.

- **`Assets/UnityMultiplayerARPG/Demo/InputActions.inputactions`** (2026-08-29) — added a
  `<Mouse>/leftButton` binding to the `Attack` action, which previously had only
  `<Keyboard>/v` and `<Gamepad>/buttonWest`. `InputManager.GetButton` resolves the Input
  System action before falling back to the legacy Input Manager, and no `Attack` axis exists
  in `ProjectSettings/InputManager.asset`, so this asset is where the binding belongs.
  **This edits a stock kit asset** — a kit reimport would revert it.

### Fixed

- **Item duplication exploit** (2026-08-29, from upstream `13f341d`) — `CmdPickupItemFromContainer`
  did not clamp the requested amount to what the container actually held, so a modified client could
  duplicate items on pickup. Fixed by the Core update: `if (amount < 0)` → `if (amount < 0 || amount > pickingItem.amount)`.
  This was the concrete reason for doing the update.
- **DevExt demo scripts vs. new delegate signatures** (2026-08-29) — `DevExtDemo_PlayerCharacterEntity.cs`
  and `DevExtDemo_MonsterCharacterEntity.cs` are Asset-Store-only demo content with no upstream
  counterpart, so they were hand-fixed for the new delegates: added the leading `target` parameter
  and changed `IGameEntity attacker` to `EntityInfo attacker`. Parameter *names* don't affect
  delegate compatibility, so only types changed; the one body change is
  `attacker.GetGameObject().name` → a null-guarded `attacker.Entity != null ? ... : attacker.Id`,
  because `EntityInfo` has no `GetGameObject()`.
- **GuildWar vs. new Core APIs** (2026-08-29) — three files. `BaseGameNetworkManager_GuildWar.cs`
  could no longer `foreach` over `Assets.GetSpawnedObjects()` (now returns an `Enumerator`), and
  `GuildWarMonsterCharacterEntity.cs` / `GuildWarMapInfo.cs` used the removed `EntityInfo` constructor,
  `SummonerType`, `SummonerGuildId` and `CharacterSummoner.Id/IsAlly/IsEnemy`. All three now match
  [UnityMultiplayerARPG_GuildWar](https://github.com/suriyun-mmorpg/UnityMultiplayerARPG_GuildWar)
  `main` byte-for-byte rather than being hand-patched.
- **Re-applied our `FollowCameraControls` teardown-save patch** (2026-08-29) — the Core mirror
  overwrote it. Re-derived against the new upstream file rather than force-applying the old diff,
  since upstream had refactored to cached save keys (`_xRotationSaveKey`). Same design as before.

- **Attacks firing in the movement direction instead of the aim direction** (2026-08-29) —
  two independent causes in `TopDownAimController`:
  1. *Ordering.* The base runs `UpdateWASDInput()` (sets look rotation to `_moveDirection`)
     then `UpdateWASDAttack()` (fires using current facing) within one frame. The aim rotation
     was being applied at the end of `ManagedUpdate`, after the attack had already been issued.
     Moved into an override of `UpdateWASDInput` — the only virtual hook ahead of the attack.
  2. *Auto-turn to nearest enemy.* `UpdateWASDAttack` finds the nearest entity in range and
     defers the attack via `_turnToTargetActionType`; `UpdateTurnToTargetToDoAction` then rotates
     toward that entity and fires within 15°, ignoring the cursor. `UpdateWASDAttack`,
     `RequestAttack` and `TurnCharacterToEntityToAttack` are all non-virtual, but
     `_turnToTargetPosition` / `_turnToTargetActionType` are `protected` and
     `UpdateTurnToTargetToDoAction` prefers `_turnToTargetPosition` when set — so
     `RedirectPendingActionToCursor()` repoints the pending action at `AimWorldPosition`.
     Gated behind the `Strict Cursor Aim` toggle (on by default); also covers `UseSkill`.

- **Character turning to the run direction mid-attack** (2026-08-29) —
  `BuiltInEntityMovementFunctions3D` re-aims the character at `_moveDirection` on every
  movement update while `_lookRotationApplied` is true. `SetLookRotation` clears that flag,
  but `UpdateRotation` sets it back each frame, so the suppression only holds for as long as
  the controller keeps calling `SetLookRotation`. `faceCursorWhileDoingAction` was gating
  those calls off during attack/skill animations — handing rotation back to the movement
  system at exactly the wrong moment. Default flipped to `true`. `Entity.CanTurn()` is still
  checked inside `SetLookRotation`, so skills that legitimately lock rotation still work.

### Removed

- **`Assets/TopDownController/Scripts/TopDownPlayerCharacterController.cs`** and
  **`Demo/Prefabs/TopDownPlayerCharacterController.prefab`** (2026-08-28) — the addon's own
  controller forced `controllerMode = PlayerCharacterControllerMode.PointClick` every frame in
  `ManagedUpdate`, making WASD impossible. Replaced by the stock controller in WASD mode.
- **`Assets/TopDownController/Demo/Scenes/00Init_TopDownDemo.unity`** and its `.lighting` file
  (2026-08-28) — the addon's demo scene, orphaned once the point-click prefab was deleted.
  Verified absent from Build Settings and unreferenced before removal.

## Environment notes

Not project changes, but they affected the editor and are worth recording:

- **Smart App Control disabled** (2026-08-28, by Sam). Burst JIT-compiles jobs into unsigned
  DLLs under `Library/BurstCache/JIT/`; Windows Application Control blocked Unity from loading
  them, surfacing as `Unable to load the unmanaged library ... error code 4551`
  (= "An Application Control policy has blocked this file"), confirmed via CodeIntegrity events
  3033/3077. No reboot was needed. **This is irreversible without a Windows reinstall.**
- Burst AOT produces an unsigned `lib_burst_generated.dll` too, so standalone builds would have
  hit the same block. Shipped players on other machines are unaffected.

## Known follow-ups

- `Demo/`, `Demo2D/`, `DemoShooter/`, `DemoSurvival/`, `DemoGuildWar/` are still February-era Asset
  Store content running against August Core. They compile, but February prefabs/scenes deserialising
  against changed August code is the most likely source of subtle breakage. Check this first if a
  demo scene misbehaves. An Asset Store package update would realign them.
- Compare our hand-built `TopDownAimController` against the creator's official
  [UnityMultiplayerARPG_AimAtCursorController](https://github.com/suriyun-mmorpg/UnityMultiplayerARPG_AimAtCursorController)
  (pushed 2026-05-20) — it may cover the same ground and be worth adopting or borrowing from.
- Re-run the update periodically; the creator commits to `Core` most days. The version can be
  pinned by diffing a known file against upstream history, as was done to identify `7876b7e`.


- Bind M1/M2 to abilities Battlerite-style: `Attack` is already a virtual button, and
  `UICharacterHotkey` accepts a `buttonName` alongside `KeyCode`, so this is input-map
  configuration rather than code.
- Feed `CurrentAimPosition` into `UseHotkey` so skills become true cursor-aimed skillshots.
- Gate ability input on `UISceneGameplay.IsPointerOverUIObject()` so clicking inventory/shop
  windows doesn't also swing the weapon.
- Hover-based soft targeting (nameplates/health bars) now that clicking no longer selects.
- Interaction prompt UI driven by `ActivatableEntityDetector.activatableEntities`.
