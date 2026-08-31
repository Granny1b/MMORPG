# Changelog

All notable changes to this project are recorded here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Paths are relative to the project root (`D:\1. Unity projekt\MMORPG Granny`).

## [Unreleased]

### Changed

- **Freshly imported Asset Store SFX/VFX/animation/model packs added to `.gitignore`** (2026-08-31) —
  `Assets/Action RPG SFX V2/` (187 MB), `Assets/Hovl Studio/` (318 MB), `Assets/Kevin Iglesias/`
  (338 MB) and `Assets/Melee Weapons Pack 1/` (1.1 GB), plus their `.meta` files. ~1.9 GB of
  purchased, re-downloadable content that was still untracked, following the same convention as the
  existing Synty and BLINK entries. Re-import these packages after a fresh clone or prefab/scene
  references to them will show up as missing. The animation and model packs are in a separate block
  in `.gitignore` so they are easy to un-ignore if they should be tracked after all.

- **Two-hand sword attack, authored on the `TwoHandSword` WeaponType** (2026-08-30) —
  `HumanM@Attack2H01` from Kevin Iglesias `Human Animations`, masked to `SyntyUpperBody` while
  moving, trigger rate 0.350, carrying the same five swoosh clips as the 1H sword.
  - Set on the **WeaponType asset** (`playableCharacterModelSettings.applyWeaponAnimations`) rather
    than in the character prefab's `weaponAnimations` list. `TryGetWeaponAnimations` checks the
    model's own list first and falls through to the WeaponType, so this is a project-wide default
    that every humanoid model inherits, leaving the per-model list free for real exceptions.
  - Edited the kit's `Demo/GameData/Resources/WeaponTypes/TwoHandSword.asset` in place rather than
    forking it into `1. Data`: it is already referenced by `GameDatabase_G`, `DarkFortressSword001_G`
    already uses the Demo `OneHandSword` type, and a fork would change the DataId and orphan the
    registered `TwoHandSword001` item. The `Demo/` tree has no upstream repo, so the GitHub update
    path never overwrites it.
  - Clip chosen by measurement across all four male 2H attacks, 61 sampled poses on the Synty rig.
    `2H01` moves the legs 0.159 against `2H03`'s 0.603 and `2H04`'s 0.907 (a lunge and a spin, both
    unusable under an upper-body mask), while swinging harder than the 1H clip in service
    (arms 0.672 vs 0.520). All four are self-contained - start pose equals end pose, zero root
    drift - unlike the Synty combo clips that had to be discarded.
  - `2H02` isolates marginally better (legs 0.131, ratio 5.17 vs 4.23) but lands contact at 0.73s
    against `2H01`'s 0.56s, with a broad speed plateau instead of a single sharp peak. Both clear
    the "subtle legs" bar, so timing decided it.
  - `leftHandAttackAnimations` left empty: `equipType 2` means the type can never occupy the left
    hand. The asymmetry that makes this safe is worth recording - **attack arrays do not fall back**
    (`GetRightHandAttackAnimations` tests `!= null`, and an empty array is not null), so any weapon
    type given an entry must carry at least one attack clip or its attacks resolve to a zero-length
    action. Base locomotion states *do* fall back, because `SetBaseState` skips null clips.

- **Weapon swing SFX** (2026-08-30) — five clips from
  `Melee Weapons Pack 1/Designed/Weapon Swing/Design 1` on the sword's attack, played at random per
  swing by `ActionAnimation.GetRandomAudioClip()`.
  - Wired through a new `weaponAnimations` entry keyed to `OneHandSword` rather than onto
    `defaultAnimations`, so the swoosh only plays with a sword equipped and not while unarmed.
    Only `rightHandAttackAnimations` is filled — `SetBaseState` skips states with a null clip, so
    idle and movement still fall back to the defaults rather than being blanked.
  - **`genericAudioSource` had to be created.** `PlayActionAnimationAudioClip` routes through
    `AudioManager.PlaySfxClipAtAudioSource(clip, GenericAudioSource)`, which returns early on a null
    source — so the field being unset drops the audio silently, with nothing in the console. There
    was no in-project example to copy: the kit's own `Male_CC` leaves it null too and relies on
    animation events instead, as the field's own tooltip suggests. Added an `AudioSource` on the
    character root, 3D (`spatialBlend 1`), `playOnAwake` off, rolloff 2-30m.

- **`Assets/1. Data/Prefabs/Weapons/SM_Wep_Sword_01_G.prefab`** (2026-08-30) — a copy of the Dark
  Fortress sword prefab in project space, with `DarkFortressSword001_G` repointed at it. The grip
  offsets live on the item, so they were unaffected by the swap.
  - The copy carries a defect from the source art: **two `MeshCollider` components** on the same
    object. The kit does melee hit detection through damage transforms, not weapon colliders, and a
    collider parented to a hand bone can disturb physics - worth removing both.

- **`DarkFortressSword001_G`** (2026-08-30) — a `WeaponItem` using
  `PolygonDarkFortress/Prefabs/Weapons/SM_Wep_Sword_01`, `weaponType: OneHandSword`, socket
  `WeaponR`, registered in `GameDatabase_G`.
  - Grip fitted through the item's own `EquipmentModel` transform fields rather than by moving the
    `WeaponR` anchor: `localPosition (0.103, 0.020, -0.004)`, `localEulerAngles
    (-57.98, 247.952, -245.48)`. Adjusting the anchor instead would shift every weapon ever
    equipped; the item is the right level for a per-weapon fit, at the cost of each new weapon
    needing its own.
  - `localScale` stays at 1: the Dark Fortress sword is authored at real-world scale (mesh 1.466
    units tall) and the FixedScale rig gives `WeaponR` a world scale of 1.
  - `moveSpeedRateWhileAttacking` set to 1, from the field's default of **0**. It multiplies
    movement speed directly (`moveSpeed *= MoveSpeedRateWhileAttacking` in
    `BaseCharacterEntity_MoveFunctions`), so the default is a hard freeze for the length of every
    swing. `MovementRestriction` only covers jump/dash/turn, so this rate is the only thing gating
    movement while attacking.
  - Note that `SyntySword001_G` still has the default 0 and no grip offsets, so it freezes movement
    and sits wrong in the hand. The two prefabs have different pivots, so its offsets cannot be
    copied from this one.

- **Right-hand attacks reduced to one** (2026-08-30) — back to `HumanM@Attack1H04_R` alone;
  `01_R` and `05_R` removed. The kit randomises between entries, and a single clip reads more
  consistently.

- **`Assets/Scripts/Gameplay/ActionLayerMaskUpdater.cs`** (2026-08-30) — hands the legs back to
  locomotion when the character starts moving part-way through an attack.
  - `AnimationPlayableBehaviour.PlayAction` chooses the action layer's avatar mask **once**, at the
    instant the swing starts, and never revisits it. Begin an attack standing still — so the mask
    falls through to `EmptyMask` and the attack drives the whole body — then walk, and the legs stay
    welded to the attack clip for the rest of the swing while the character slides.
  - The component re-evaluates the same condition the kit uses
    (`MovementState.HasDirectionMovement()` and `IsGrounded`) each frame and swaps the mask on any
    action layer whose weight is above zero. It only writes on a change of state, not every frame.
  - Reads only public API — `PlayableCharacterModel.Behaviour`, `LayerMixer`, and the
    `ACTION_LAYER` / `EmptyMask` members of `AnimationPlayableBehaviour`. No kit file is modified,
    but a version that hides any of those breaks the build rather than failing quietly.
  - Layers below `ACTION_LAYER` (base locomotion and left-hand wielding) are never touched.

- **Right-hand attack variety** (2026-08-30) — `HumanM@Attack1H01_R` and `HumanM@Attack1H05_R` added
  alongside `04_R`, since the kit picks among the entries at random. All three are self-contained
  with zero hip drift; 01 and 05 swing harder (arm movement 0.591 and 0.576 against 04's 0.520) at
  the cost of more leg movement. Trigger rates measured per clip from peak hand speed: 0.317, 0.267
  and 0.283.

- **Upper-body attack layering** (2026-08-30) — `Assets/1. Data/AvatarMasks/SyntyUpperBody.mask`
  plus `avatarMaskWhileMoving` on the attack states, so attacks play on the torso while the legs
  keep strafing. Standing still is left unmasked, so the attack's own (slight) footwork still reads.
  - `PlayAction` picks the mask once, at the start of the swing:
    `avatarMaskWhileMoving` applies only when grounded **and** moving, then falls through
    `actionState.avatarMask` -> `CharacterModel.actionAvatarMask` -> `EmptyMask`. Leaving the last
    two null is what gives full-body-when-standing for free.
  - The kit's own `TopMask.mask` could not be reused: its humanoid flags are right, but its
    transform paths are the demo rig's (`Root_M/Hip_L/...`), which do not exist on the Synty
    hierarchy.
  - The mask deactivates the lower body derived from the **avatar's bone mapping**, not from name
    guesses. A first attempt filtered on name prefixes and silently missed `LowerLeg_*` and `Ball_*`
    — the shins and toe-balls stayed active while the thighs and ankles were masked, which would
    have looked broken. 15 transforms are masked out now, the complete leg chains plus
    `Root`, `Hips` and `Hips_Attachment`.

- **Attack animation switched to Kevin Iglesias `Human Animations`** (2026-08-30) — right hand
  `HumanM@Attack1H04_R`, left hand `HumanM@Attack1H04_L`, replacing the Synty sword combo clips.
  - Why the Synty ones had to go: measured against the idle pose, every clip in
    `AnimationSwordCombat` starts at 0.147 (the pack's combat-ready stance) and ends 0.30-0.49 away
    from idle. They are combo *links* — `LightCombo01B` starts at 0.304, exactly where `A` ends.
    The kit plays one randomly chosen clip per swing, so B or C always begin from a pose the
    character is not in. Structural mismatch, not a bad clip choice.
  - `Attack1H04_R` was picked by measurement across the five 1H attacks: start pose equals end pose
    (0.000, so no snap), zero hip drift, and the least lower-body movement of the set (0.188) while
    keeping a strong arm swing (0.520) — the best upper-to-lower ratio at 2.8:1, which is what
    "subtle legs when standing" needs.
  - Trigger rate 0.317, taken from the frame of peak hand speed rather than the usual flat 0.5.

- **`SyntyPlayerCharacter` rebuilt on the FixedScale base** (2026-08-30) — the character was built
  from `PolygonFantasyHeroCharacters/Prefabs/ModularCharacters.prefab`, whose rig `Root` sits at
  `localScale 0.01`. Anything parented to a hand bone therefore rendered at 1/100 scale, so
  instantiated weapons were invisibly small. Synty ships the same character re-imported at unit
  scale under `Prefabs/FixedScale/`, and that is what the character now uses.
  - Both bases produce the same real-world character: arm-span bounds measured 2.049 either side of
    the swap. Only the internal bone scale differs, along with the avatar asset
    (`Models/FixedScale/ModularCharacters.fbx` instead of `Models/ModularCharacters.fbx`). Both are
    Humanoid, so every clip retargets unchanged.
  - Done as surgery rather than a rebuild from scratch: only the `Modular_Characters` and `Root`
    children were replaced. The nine `_TpsCamTarget` / `_CombatText` / `_MeleeDamage` style kit
    anchors were kept, which is what preserved `PlayerCharacterEntity`'s nine transform references
    and every kit component's settings.
  - Of the 467 references into the replaced subtree, 458 were `PlayableCharacterModel.
    equipmentContainers` — rebuilt in one pass by the equipment builder — and 9 were those kit
    anchors, which survived.
  - **The animation setup survived untouched**: `defaultAnimations` and `LocomotionPhaseSync.
    clipPhases` hold clip references, not transforms. Verified after the swap - idle, the eight-way
    locomotion set, three attacks and all 24 measured clip phases still in place.
  - Weapon anchors were recreated at `localScale 1`. On the old base they needed `localScale 100` to
    cancel the rig scale; that hack is now gone, which is the point of the exercise.
  - Consequence: the character's *appearance* reset to the modular defaults (10 active renderers
    where there were 11), because the FixedScale preset enables a different set of parts and the
    builder's "reset to default state" pass then shows the bare defaults. The look needs picking
    again; nothing else was lost.

- **`SyntyEquipmentContainerBuilder.Build(...)`** (2026-08-30) — a static entry point mirroring the
  locomotion builder's `Assign(...)`, so the container pass can be run headlessly. Added because
  rebuilding a character after a rig swap needs it without a human clicking the window.

- **Attack animations on `SyntyPlayerCharacter`** (2026-08-30) — `rightHandAttackAnimations` and
  `leftHandAttackAnimations` were both empty, which threw on every swing:
  `DefaultCharacterAttackComponent.AttackRoutine` takes `triggerDurations` from
  `Entity.GetAnimationData(...)` and walks it, so with no attack configured there is nothing to
  walk. Filled from `Assets/Synty/AnimationSwordCombat` — right hand gets the three-hit
  `LightCombo01` A/B/C, left hand gets A.
  - Each attack `.fbx` in that pack holds four clips, not one: the whole swing (`A_Attack_X_Sword`)
    plus `WindUp`, `Hit` and `FollowThrough` phases that sum to it. The kit plays one clip per
    attack, so the whole-swing clip is the one wired; selecting it needs an **exact name match**,
    since "the first clip in the file" picks a phase fragment instead.
  - The phase clips give the trigger rate exactly rather than by eye: `WindUp.length / full.length`
    is the moment contact begins. The three differ enough to matter — 0.417, 0.250 and 0.500 — so
    the usual flat 0.5 would have been wrong on two of the three.
  - The pack is Humanoid, same as the Synty avatar, so it retargets without a rig change.
  - `actionAvatarMask` is still null, so attacks play full-body. That is safe — `PlayAction` falls
    back through `actionState.avatarMask` and the model's `actionAvatarMask` to `EmptyMask` — but it
    means the legs stop strafing mid-swing.
  - A/B/C are a combo, but `DefaultCharacterAttackComponent.doNotRandomAnimation` defaults to false,
    so the kit picks among them at random rather than in sequence. The component is not on the
    prefab (it is added at runtime), so making it a real combo means adding it explicitly and
    setting that flag.
  - Still empty and worth filling from the same pack later: `hurtState` (Hit/HitReact),
    `deadState` (Death), `skillActivateAnimation`, `rightHandChargeState`, `pickupState`. None throw;
    they just play nothing.

- **`GameInstance.uiSceneGameplayPrefab` pointed at `CanvasGameplay_G`** (2026-08-30) — in
  `Demo/Scenes/00Init.unity`. The `_G` canvas fork existed but nothing referenced it, so the game
  still instantiated the kit's `CanvasGameplay` and none of the forked dialog chain reached the
  running game. The new Legs and Cloak equip slots appeared anyway only because the same slot
  objects had also been added to the kit's `UIItemsDialog.prefab`, whose
  `UIEquipItems.otherEquipSlots` was never wired — visible slots that nothing was routed to.

- **UI dialog chain forked out of the kit tree** (2026-08-30) — `CanvasGameplay_G` now uses
  `Assets/1. Data/Prefabs/UI Prefabs/UIDialogs_G.prefab`, a fork of `UIDialogs_Standalone` whose
  `UIItemsDialog` child points at `1. Data/Prefabs/UI Prefabs/UIItemsDialog.prefab`. No kit prefab
  is modified by the swap.
  - Forking `UIDialogs_Standalone` does not fork the ~25 dialogs inside it: they remain instances of
    their own kit prefabs (`UIQuestDialog`, `UINpcDialog`, ...), so kit fixes to those still flow
    through. Only the one dialog actually replaced is detached.
  - Swapped with `PrefabUtility.ReplacePrefabAssetOfPrefabInstance` (`ObjectMatchMode.ByHierarchy`)
    rather than deleting and re-adding the instance. Deleting would have broken every reference
    pointing into the subtree; 38 such references exist from elsewhere in the canvas, and all 38
    survived — including `UIGenericLayout/Menu/ButtonItems.onClick -> UIItemsDialog.Toggle`.
  - The instance is still *named* `UIDialogs_Standalone` even though it now comes from
    `UIDialogs_G` (`changeRootNameToAssetName` left false). Renaming is safe —
    `UIEscapeWindowsHandler.excludingWindows` is a `List<UIBase>`, so it matches by reference, not
    by path — but the name was left alone to keep the swap minimal.

- **`Legs` and `Cloak` equip slots bound in `UIItemsDialog`** (2026-08-30) — the `EquipSlotLegs` and
  `EquipSlotCloak` objects already existed with `UICharacterItem` components, but
  `UIEquipItems.otherEquipSlots` still listed only six entries. The GameObjects alone do nothing;
  that array is what binds a slot to an armor type and gives it an equip position. Now eight:
  Head, Body, Gloves, Shoes, Ring, Ring, Legs, Cloak.

- **Project game data moved out of the kit's `Demo/` tree** (2026-08-30) — created
  `Assets/1. Data/GameData/` mirroring the kit's own category names (ArmorTypes, Items/Armors/...,
  Skills, Quests and the rest), and moved the first slice into it: the `Legs` and `Cloak` armor
  types and the `Legs001_G` armor item.
  - Moved with `AssetDatabase.MoveAsset`, which preserves GUIDs, so the `GameDatabase` item list and
    the item's armor-type reference survived untouched — verified after the move.
  - Deliberately **not** under a `Resources/` folder, unlike the kit's copy. The project uses the
    explicit-list `GameDatabase`, not `ResourcesFolderGameDatabase`, so game data does not need to
    be in `Resources/` — and everything under one is force-included in every build whether it is
    referenced or not.
  - Empty folders carry a `.gitkeep`. Git does not track empty directories, so without one the
    folder `.meta` would arrive at a fresh clone as an orphan and Unity would delete it, taking the
    skeleton with it. Unity ignores files beginning with `.`, so they generate no `.meta` of their
    own. Delete each one as its folder gets real content.
  - What cannot move: *edits* to kit-named files, such as the equip slots being added to
    `UIItemsDialog.prefab`. Those stay in `Demo/` and rely on this log to be re-applied after an
    Asset Store re-import. The `Demo*` trees are not covered by the GitHub kit update.

- **Equipment sockets renamed `Pants` -> `Legs` and `Back` -> `Cloak`** (2026-08-30) — in
  `SyntyEquipmentContainerBuilder`'s slot map and on `SyntyPlayerCharacter`, so socket names follow
  the kit's armor-type convention, which is body parts rather than garments (Body, Head, Gloves,
  Shoes). Nothing required them to match the armor types — `ArmorType.EquipPosition` picks the
  inventory slot and `EquipmentModel.equipSocket` picks the mesh container, and the kit never
  compares the two — but one arbitrary name out of step was worth removing while only three
  references existed.
  - Renamed in place rather than by re-running the builder: `ApplyContainers` matches by socket
    name and only replaces same-named entries, so a run producing `Legs` would have added it
    alongside the old `Pants` container instead of superseding it.
  - Safe whenever it is done: equip position is derived from the item's armor type at lookup time
    in `IndexOfEquipItemByEquipPosition`, never stored on the character, so renaming cannot orphan
    equipped gear.

- **`Legs001_G`** (2026-08-30) — given an `EquipmentModel` (`equipSocket: Legs`,
  `useInstantiatedObject`, `instantiatedObjectIndex: 1`) and added to the `GameDatabase` item list.
  Without the first it equips invisibly; without the second it does not exist at runtime, since the
  database holds explicit references rather than scanning `Resources/`. Its `Legs` armor type comes
  along automatically through `ArmorItem.PrepareRelatesData()`.
  - Still outstanding: no slot for equip position `Legs` in `UIEquipItems.otherEquipSlots` on
    `UIItemsDialog.prefab`, so the item has nowhere to be displayed.

- **`.gitignore`** (2026-08-29) — the BLINK icon pack is no longer committed.
  `Assets/2. Art/Blink/` is ~214 MB across 608 PNG icons and 15 PSD sources, all of it purchased
  and re-downloadable, and every PNG/PSD would otherwise go through Git LFS. It was still
  untracked when the rule was added, so nothing had to be removed from history.
  - Scoped to the vendor folder rather than a blanket `*.png` rule. A global PNG rule would not
    have removed the 176 kit PNGs already tracked under `Assets/UnityMultiplayerARPG/` — ignore
    rules do not apply to tracked files — and would silently swallow future PNGs worth committing.
  - `Assets/2. Art.meta` stays tracked so hand-made art can be added next to the pack. Until
    something else lives in `Assets/2. Art/`, a fresh clone has that meta with no folder beside
    it and Unity will drop it on import; adding any own art there makes it moot.
  - Icons referenced by `GameDatabase.asset` will show as missing sprites until the pack is
    re-imported into the same path.

- **`.gitignore`** (2026-08-29) — Synty Asset Store content is no longer committed. `Assets/Synty/`
  alone is ~1.8 GB of FBX meshes, animation clips and UI sprite sheets (`InterfaceCore`,
  `InterfaceFantasyWarriorHUD`), all of it re-downloadable from the Synty/Asset Store, and every
  `.fbx`/`.png`/`.tga` in it would otherwise go through Git LFS. Nothing under `Assets/Synty/` was
  tracked yet, so no history rewrite was needed — the rules just stop it from ever being added.
  - The folder `.meta` files are ignored alongside the folders. Committing `Assets/Synty.meta`
    without `Assets/Synty/` would leave an orphan meta that Unity deletes on next import.
  - Also covers `Assets/Polygon*/`, `Assets/Interface*/` and `Assets/AnimationBaseLocomotion/`,
    the paths Synty packages land in when imported to the project root instead of `Assets/Synty/`.
  - Consequence for a fresh clone: re-import the Synty packages **before** opening the project,
    or prefabs under `Assets/1. Data/Prefabs` that reference them come up with missing meshes and
    sprites. Synty package GUIDs are stable across re-imports, so the references reconnect.

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
  - `excludingWindows` holds two entries, both found by inspecting what auto-collection actually
    caught in the editor: `UIDialogs_Standalone/--Character` (a stray `UICharacter` placeholder —
    no children, no graphics, referenced by nothing) and `UIMailLayout/NotificationComponent`
    (`UIMailNotification` + `RepeatingEvent`, the mail-notification poller). Both are authored
    active, unlike all 44 real windows, which are authored inactive; hiding the poller would have
    stopped mail notifications for the rest of the session.
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
  `UICraftingLayout`; object: `UISettingDialog`; system menu: `UISystemDialog`;
  excluded: `--Character`, `NotificationComponent` (wired in the editor, 2026-08-29).
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

- **Occasional huge inward jump when zooming the camera with the scroll wheel** (2026-08-31) —
  `Assets/UnityMultiplayerARPG/Core/CameraAndInput/Scripts/Input/InputManager.cs`, `GetAxis`:
  when an Input System action exists for an axis, the legacy Input Manager is no longer consulted
  as a fallback for that axis.
  - The project runs `activeInputHandler: 2` ("Both"), and `GetAxis` returned the Input System
    action's value only when it was non-zero *that frame*, otherwise falling through to
    `Input.GetAxis(name)`. Both backends see the same wheel notch at very different scales:
    `<Mouse>/scroll/y` reports ±1 per notch here (not the 120 the kit's `Scale(factor=0.001)`
    processor assumes) → **0.001**, while the legacy `Mouse ScrollWheel` axis has sensitivity
    `0.1` → **0.1**. A 100x difference.
  - `FollowCameraControls` multiplies that axis by `zoomSpeed`, which is `-100` on
    `Assets/TopDownController/Demo/Prefabs/TopDownGameplayCamera.prefab` (the camera reached via
    `GameInstance.defaultControllerPrefab` → `TopDownAimController`). So a normal frame moved the
    camera 0.1 units while a fall-through frame moved it **10 units** — over half of the prefab's
    `minZoomDistance 6` .. `maxZoomDistance 25` range, in one frame. Scrolling fast made
    fall-through frames more likely, which is why it only happened sometimes. Zooming out leapt
    just as far but clamped at `maxZoomDistance`, so it was much less visible.
  - The mobile simulated-axis path is untouched; only the legacy branch is skipped, and only when
    `TryGetInputAction` actually found an action. Side effect: an axis whose action exists but has
    no binding for the player's device no longer silently borrows the legacy axis.
  **This edits a stock kit script** — a kit reimport would revert it.
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
