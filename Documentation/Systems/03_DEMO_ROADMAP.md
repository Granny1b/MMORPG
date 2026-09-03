# Roadmap to a playable demo

**Status:** plan. Written 2026-09-03. Nothing here is implemented.

## Purpose

Order the remaining work to a demo that can be handed to someone who has never seen the project,
and that they can play for ten minutes without being told what to ignore.

Every claim about the kit below carries a `file:line` citation so it can be re-checked after a kit
update. Counts were measured from the assets on disk on the date above.

## Where the project actually stands

The gap is not evenly distributed, and the distribution is the whole point of this document.
Measured from `Assets/1. Data/GameDatabase_G.asset`:

| Registered list | Count | Line |
|---|---|---|
| `monsterCharacters` | **0** | `:85` |
| `monsterCharacterEntities` | **0** | `:17` |
| `skills` | **0** | `:74` |
| `statusEffects` | **0** | `:81` |
| `quests` | **0** | `:89` |
| `harvestables` | **0** | `:86` |
| `itemCraftFormulas` | **0** | `:58` |
| `items` | 25 | `:32` |
| `mapInfos` | 1 | `:87` |
| `playerCharacters` | 1 | `:82` |

Of the 25 items, 15 are cloaks. The world is `Assets/1. Data/Scenes/Prototype_World_01.unity` at
19 GameObjects: terrain, lighting, a NavMesh surface. `MapInfo.startPosition` is `{0, 0.1, 0}`
(`MapInfos/Prototype_World_01.asset`), an untouched placeholder.

**The character controller is the most finished thing in the project and there is nothing to point
it at.** `TopDownAimController` does cursor aiming, strafe states and UI-safe attack suppression;
`DirectionalRollDash` does a four-clip directional roll with a root-motion-matched distance curve
and a cancelable get-up. Both are further along than anything they interact with.

That asymmetry sets the ordering: the two items on the wish list that are *polish on existing
systems* (roll cost, camera occlusion) are cheap and well-understood, and the one that is
*net-new content* (enemies) is the long pole. Do not let the cheap, satisfying work crowd out the
long pole.

## The critical path

The demo loop is **spawn → gear → find enemies → fight → loot → repeat.** Prove that loop
end-to-end with one of everything before widening any part of it. A second enemy tier is worth
nothing until the first one can be killed and drops something.

---

## Phase 0 — Confirm the skeleton art. Blocking.

Everything downstream rests on having a skeleton model with a humanoid rig. Purchased art is
excluded by `.gitignore` (see CLAUDE.md, fresh clone setup), so this **cannot be answered from the
repository** and has to be answered by looking at the machine.

Three questions, in order of how much they change the plan:

1. **Is there a skeleton mesh with a humanoid rig on disk?** If the Synty packs already installed
   include one, then the existing pipeline applies unchanged — `SyntyLocomotionAnimationBuilder`,
   the avatar masks under `Assets/1. Data/AvatarMasks/`, and the animation controllers are all
   reusable, and a skeleton becomes a re-skin of solved work. If not, this is a purchase decision
   that gates several weeks, and it should be made in week 1, not week 3.
2. **Does the pack contain an armored variant and a larger variant?** This is the difference
   between "three prefabs" and "one prefab plus two data assets", which is roughly a week.
3. **Does it have attack clips, or only locomotion?** Relevant because of a kit trap: attack
   animation arrays do not fall back. `GetRightHandAttackAnimations` tests `!= null`, and an empty
   array is not null, so a weapon type with an animation entry but no attack clip resolves to a
   zero-length action and the monster appears to do nothing (CLAUDE.md, gotchas).

Answer these before writing any code. A wrong assumption here invalidates Phases 1 and 2.

---

## Phase 1 — One skeleton, killable, dropping one item

The vertical slice. Goal is not a good enemy; it is proof that the loop closes.

**Fork the kit's orc rather than authoring a blank asset.**
`Assets/UnityMultiplayerARPG/Demo/GameData/Resources/MonsterCharacters/OrcWarrior.asset` is a
complete, working, correctly-populated `MonsterCharacter`. `MonsterCharacter` has roughly forty
serialized fields and a blank asset misses several of them silently. Duplicate it to
`Assets/1. Data/GameData/MonsterCharacters/Skeleton_G.asset` and edit down. `OrcArcher.asset` and
`BigOrcWarrior.asset` are the ranged and elite references for later.

Steps:

1. Fork `OrcWarrior.asset` → `Skeleton_G.asset` under `Assets/1. Data/GameData/MonsterCharacters/`.
2. Build the entity prefab: `BaseMonsterCharacterEntity` plus a `PlayableCharacterModel` and a
   movement component. The monster nameplate is reusable as-is —
   `Demo/Prefabs/Gameplay/RelatesObjects/MonsterCharacter/CanvasMonsterCharacterUI.prefab`.
3. **Register in `GameDatabase_G` in both lists.** `monsterCharacters` (the data) and
   `monsterCharacterEntities` (the prefab). Registering only the first is the classic failure:
   the data resolves, the entity cannot spawn, and nothing is logged.
4. Drop a `MonsterSpawnArea` (`Core/Scripts/Gameplay/Area/MonsterSpawnArea.cs`) into the scene.
5. One `ItemDropTable` asset, wired to the monster's `itemDropManager` (`MonsterCharacter.cs:89`).
   `Demo/GameData/Resources/MonsterCharacters/_ItemDropTable.asset` is the shape to copy.

**Set `moveSpeedRateWhileAttacking` to 1** (`MonsterCharacter.cs:77`). This is the same trap
CLAUDE.md flags for weapons: it defaults to 0 and multiplies move speed directly, which is a hard
freeze for the length of every swing. On a monster it reads as the AI being broken.

**Exit test:** walk up to it, it aggros, it hits you, you kill it, an item falls, you pick it up.
Until that sequence works there is no demo, only assets.

---

## Phase 2 — The three tiers

Cheap once Phase 1 lands, because the kit generates the level curves for you.

`MonsterCharacter` carries inspector buttons that take stats authored at `defaultLevel`
(`MonsterCharacter.cs:31`) and generate the per-level progression: `AdjustStats()` (`:434`),
`AdjustDamageAmount()` (`:397`), `AdjustAttributes()` (`:817`), `AdjustArmors()` (`:871`),
`AdjustResistances()` (`:844`), `AdjustRandomExp()` (`:898`), `AdjustRandomGold()` (`:933`).
So tier design is: decide the numbers you want at one level, press the buttons.

Proposed shape. `characteristic` values come from the enum at `MonsterCharacter.cs:8-14`
(`Normal`, `Aggressive`, `Assist`, `NoHarm`):

| | Skeleton | Armored Skeleton | Skeleton Elite |
|---|---|---|---|
| Role | trash, arrives in threes | slow wall, punishes greed | zone mini-boss, one of them |
| `characteristic` | `Aggressive` | `Aggressive` | `Aggressive` |
| Identity | low HP, quick, short reach | high armor, slow swing | high HP, one telegraphed skill |
| `visualRange` (`:50`) | short | short | long |
| Drop | gold and junk | an armor piece | guaranteed weapon |
| New art needed? | yes | **no — material + scale** | ideally yes |

Two recommendations here.

**Make the armored skeleton a material and stat variant, not new art.** Players read "armored" from
behaviour — it does not stagger, it takes noticeably longer to kill — far more than from silhouette.
Spend the art budget on the elite, which is the thing a demo player remembers and talks about.

**Only the elite gets a skill, and treat that as real work.** `skills` on the monster
(`MonsterCharacter.cs:60`) references entries in the database's `skills` list, which is currently
empty (`GameDatabase_G.asset:74`). The elite's skill would be the project's *first* skill asset:
new ground, not a variant, with its own animation, targeting and effect questions. If the schedule
tightens, the elite is "bigger numbers plus a distinct silhouette" and the skill is cut.

`Characteristic = Assist` is worth remembering for later — an armored skeleton that pulls its
neighbours makes camps read as camps instead of as three separate fights — but it is a Phase 6
consideration, not now.

---

## Phase 3 — Roll cost

Small, self-contained, independent of the enemy work, and the highest feel-per-hour item on the
list. Schedule it alongside Phase 1 or 2 rather than after them.

`DirectionalRollDash` is currently free and unlimited. Nothing in the kit gates it for you:
`AllowToDash()` is only a ground check (`CharacterControllerEntityMovement.cs:436` →
`AllowToJumpOrDash()` → `Physics.CheckSphere`), and `CanDash_Implementation()`
(`BaseCharacterEntity_MoveFunctions.cs:166`) covers only `CachedData.DisallowDash` and the
attacking/skill/reload/charge restrictions the project already sets. Cost and cooldown are entirely
ours, and need no kit edit.

**The hook already exists.** `onCanDashValidated` sits in the same event set as
`onCanMoveValidated`, which `DirectionalRollDash` already subscribes to in `Awake` and unsubscribes
in `OnDestroy`. The pattern is in the file; extend it.

### Recommendation: both cooldown and stamina, doing different jobs

- **Cooldown, ~0.6–0.8 s** measured from the roll's start, is the anti-spam floor. It kills the
  roll-roll-roll travel exploit and is independent of build or stats.
- **Stamina, ~25 of 100**, is the resource layer: four rolls from full, then you are dry and have
  to disengage.

Neither alone is right. Cooldown alone permits an infinite chain at a fixed rate, so rolling is
still the fastest way to cross the map. Stamina alone permits four instantaneous rolls back to
back, which looks like a bug. Together they read as intended.

The `rollDuration` of 1.167 s already imposes a natural floor while the clip plays, so the cooldown
is really about the window *after* the get-up. Start at 0.7 s and tune against the skeleton's swing
timing once Phase 1 exists — the correct number is "you can dodge a skeleton swing, but not two in
a row without spacing", and that cannot be tuned before there is a swing to dodge.

### The stamina plumbing exists and is currently inert

`Warrior_G.asset` has `stamina: 100` and `staminaRecovery: 0`. The assigned gameplay rule is the
kit's `Demo/GameData/SimpleGameplayRule.asset` (`00Init.unity:792` resolves to guid
`3bfe680a81b27ce4ba9642ddff7baa17`), which sets `staminaRecoveryPerSeconds: 3` and
`staminaDecreasePerSeconds: 5`. `GetRecoveryStaminaPerSeconds` returns the rule value plus the
character stat (`DefaultGameplayRule.cs:294-296`), so stamina **does** regenerate at 3/sec.
`GetDecreasingStaminaPerSeconds` (`:323-336`) drains it only while sprinting, and this project has
no sprint bound, so today nothing consumes stamina at all.

At 3/sec and 25 per roll: four rolls from full, a single roll back in ~8 s, full bar in ~33 s.
That is a defensible starting point, but the refill is slow enough that it will probably want to go
up once there is combat to test against. Tune the rule, not the character asset.

Two pieces of work:

- **Consumption.** Deduct in `DirectionalRollDash.BeginRoll`; veto from `onCanDashValidated` when
  current stamina is below cost or the cooldown has not elapsed.
- **Regeneration and the post-roll delay.** **Subclass `BaseGameplayRule`; do not edit
  `DefaultGameplayRule` and do not edit `SimpleGameplayRule.asset`** — the first is a `Core/` edit
  that a kit update destroys, the second is a `Demo/` edit that an Asset Store re-import destroys
  (CLAUDE.md, hard rules 1 and 2). This is exactly step 6 of the ladder in `EXTENDING.md`:
  a `GrannyGameplayRule : DefaultGameplayRule` overriding `GetRecoveryStaminaPerSeconds`
  (`BaseGameplayRule.cs:104`), an asset at `Assets/1. Data/GrannyGameplayRule_G.asset`, assigned on
  `GameInstance`. A short regen delay after a roll, so you cannot roll at exactly the regen rate
  forever, goes in the same override.

### Network note

The dash check runs inside the movement update — `BuiltInEntityMovementFunctions3D.cs:666` tests
`!Entity.CanDash() || !Entity.AllowToDash()` — which executes on both the owner client and the
server, so an `onCanDashValidated` veto naturally applies on both sides. Deduct authoritatively on
the server and let the owner client predict. Do not deduct from `LateUpdate`; it runs on every
copy of the entity, including remote observers.

### While you are here

Put a stamina bar on the HUD. `UICharacter` already knows about stamina, so this is prefab work in
the forked `CanvasGameplay_G`, not code. A resource the player cannot see is a resource that reads
as a random failure to roll.

---

## Phase 4 — Start zone and camera occlusion

Paired deliberately: occlusion is untestable in an empty scene and pointless without geometry, and
a start zone built without knowing how the camera handles walls gets laid out wrong.

### 4a. Start zone blockout

Grey-box first, art later. What the demo needs is a spawn point, a readable path, and escalation:
two or three skeleton camps of increasing size, then the elite. Set `MapInfo.startPosition` to a
real place. Keep the first ninety seconds free of anything that can kill the player.

Deliberately place geometry that *will* occlude — a wall the path runs behind, a doorway, a
colonnade — because that is the test case for 4b, and because a top-down game with no vertical
geometry never needs occlusion in the first place.

### 4b. Camera occlusion

**What the kit gives you, and what it does not.** `FollowCamera` already has camera *collision*:
`enableWallHitSpring`, `minDistanceToPerformWallHitSpring`, `wallHitSpringPushForwardDistance`,
`wallHitSpringRadius`, `wallHitLayerMask`, `wallHitQueryTriggerInteraction`
(`FollowCamera.cs:37-42`). That pulls the camera in when it would clip through geometry. It costs
nothing to enable and should be turned on first — it may resolve more cases than expected, and it
changes what the fade system still has to handle.

There is **no occlusion fade anywhere in the kit.** A search of `Core/CameraAndInput/` for
occlusion, wall-hiding, transparency and fading returns nothing. This system is genuinely ours.

Three approaches:

| Approach | Mechanism | Cost | Verdict |
|---|---|---|---|
| Disable renderers | `Renderer.enabled = false` on cast hits | trivial | pops hard; reads as a rendering bug, not a feature |
| **Fade to transparent** | keep a transparent variant of each occluder material, swap and lerp alpha | moderate — a URP Lit opaque→transparent swap also moves render queue and changes sorting | **recommended** |
| Screen-space dither clip | shader clips pixels near the player's screen position | highest — every occluder material must use your shader | best looking; do it later only if the demo needs it |

**Recommended shape.** A `CameraOcclusionFader` component on the camera rig:

- `Physics.SphereCastNonAlloc` from camera to player, against a **dedicated Occluder layer** so you
  pay only for tagged geometry and never for terrain or props that cannot block.
- A small dictionary of currently-faded renderers with their original state.
- Alpha lerped in and out via `MaterialPropertyBlock` — no per-frame material instantiation, which
  is where naive implementations leak.
- Restore on exit, and on disable, so a renderer left transparent by a scene change is impossible.
- Cast every 2–3 frames rather than every frame if profiling asks; at top-down camera speeds the
  difference is invisible.

**Add it as a side component, do not subclass `FollowCameraControls`.** That file already carries a
project patch (`SaveCameraPrefs` moved out of the per-frame path, CLAUDE.md) and every kit update
requires re-applying it; subclassing compounds that divergence. A component reading public kit API
is the pattern `LocomotionPhaseSync` and `ActionLayerMaskUpdater` already establish, and it survives
kit updates untouched. Put it in `Assets/Scripts/Gameplay/` or beside the camera prefab in
`Assets/TopDownController/`.

**The layer is the design decision, not the code.** Getting this right means deciding at blockout
time which geometry is an occluder. Doing it afterwards means re-tagging a finished scene.

---

## Phase 5 — Gear, world loot, chests

Mostly data by this point, with one real exception.

### Starting gear

`Warrior_G.asset` `startItems` (`:228`) has two entries. A demo wants a weapon, chest, legs, boots
and a few potions. The armour and weapons all exist — but **`GameData/Items/Potions/` is empty and
`statusEffects` is empty (`GameDatabase_G.asset:81`)**, so a healing potion is not a data tweak.
It needs a status effect asset first. Budget it as real work or ship the demo with no consumables.

### World loot — the cheapest win on the list

Two spawn areas already exist and need no code at all:
`ItemDropSpawnArea` (`Core/Scripts/Gameplay/Area/ItemDropSpawnArea.cs`) and
`ItemDropByWeightTableSpawnArea`. Place them in the scene pointing at an `ItemDropEntity` prefab
and the world has pickups. Hours, not days.

### Chests

**There is no chest entity in the kit.** The closest existing thing is `HarvestableEntity`
(`Core/Scripts/Gameplay/HarvestSystems/HarvestableEntity.cs`) with `HarvestableSpawnArea` — an
object you hit to get items out of.

**Recommendation: ship breakable chests, not openable ones.** A chest mesh on a harvestable that
takes one or two hits to break is a satisfying, animated, already-networked loot source with zero
new code, and it fits a combat-first demo better than a container UI does. An openable chest needs
a new entity type, a new interaction, and a new window — days of work for a worse fit.

If an openable container is wanted later, `ItemDropEntity` is closer than it looks: it already
implements `IPickupActivatableEntity` (`ItemDropEntity.cs:17`), carries a `Looters` set
(`ItemDropEntity.cs:67`) and holds a list of `DropItems` (`:65`). A chest is an item-drop with a
mesh and several items in it.

---

## Phase 6 — The polish that makes it read as a game

The gap between "systems work" and "demo".

- **Hit feedback.** Hit flash, damage numbers, brief hitstop on connect. The demo canvas already
  has damage-number UI to fork.
- **Death.** A death animation and a body that sinks or fades. Monsters that vanish on the last hit
  make combat feel unfinished more than any missing feature does.
- **Aggro legibility.** Something that shows a skeleton has noticed you, so the player learns
  `visualRange` instead of being ambushed by it.
- **Audio.** `genericAudioSource` being null means silent SFX with nothing logged
  (CLAUDE.md, gotchas). Check it before concluding the audio is unhooked.

### One scoping decision to make now

`00Init_MMO.unity` is **not wired to this project's assets** — it still references the kit's
`GameInstance.prefab`, the kit demo database, the stock controller and the stock canvas
(CLAUDE.md, entry points). **Recommendation: the demo is LAN/offline only.** Ship from
`Demo/Scenes/00Init.unity`, which is already wired to `GameDatabase_G`, `CanvasGameplay_G` and
`TopDownAimController.prefab`. Wiring the MMO flavour is roughly a week that shows a player nothing
they can see. The battleground and arena designs (documents 01 and 02) both depend on MMO, so that
week is real and worth spending — after the demo, not before it.

---

## Suggested sequence

1. **Confirm skeleton art** — blocking, do it first
2. **One skeleton, killable, drops** (Phase 1)
3. **Roll cost** (Phase 3) — small and independent; slot it in while enemy design settles
4. **The three tiers** (Phase 2)
5. **Start zone blockout** (Phase 4a)
6. **Camera occlusion** (Phase 4b)
7. **Gear and world loot** (Phase 5)
8. **Polish** (Phase 6)

Phase 3 deliberately runs ahead of Phase 2. It touches no enemy work, it is bounded, and it
converts the controller from "impressive tech demo" to "game with a resource to manage".

## What to cut, in order

If the schedule tightens, cut in this order and stop as soon as it fits:

1. Chests
2. The elite's skill — it becomes bigger numbers and a bigger silhouette
3. The armored skeleton — two tiers still reads as progression
4. Occlusion fade — **keep `enableWallHitSpring`**, which is free and already there
5. Potions and status effects

**Never cut:** one enemy that can be killed, loot that drops from it, and a roll that costs
something. Those three are the demo. Everything else is width.

## Gotchas that will bite during this work

- **Register in both lists.** A monster needs `monsterCharacters` *and* `monsterCharacterEntities`.
  Unregistered data does not exist at runtime, and nothing warns you.
- **`moveSpeedRateWhileAttacking` defaults to 0 on monsters too** (`MonsterCharacter.cs:77`),
  not just on weapons.
- **Set `id` explicitly on all three skeletons before creating them.** `BaseGameData.Id` returns
  the serialized `id` field or falls back to the asset name, and `DataId` is a hash of that string
  (`BaseGameData.cs:30`, `:180`). Every asset in this project leaves `id` empty, so asset names are
  currently load-bearing, and renaming `Skeleton_G` to `SkeletonWarrior_G` later would orphan every
  saved reference. Setting `id` now costs seconds; not setting it costs a migration.
- **Do not put new data under a `Resources/` folder**, even though the kit's own monsters live
  there. This project uses the explicit-list `GameDatabase`; anything under `Resources/` is
  force-included in every build (CLAUDE.md, where things go).
- **Attack animation arrays do not fall back** — an empty array is not null, and resolves to a
  zero-length action.
- **`[DevExtMethods]` hook names are strings.** A typo fails silently, and exceptions inside a hook
  are caught and logged rather than thrown. Watch the console when a hook seems not to fire.

## Open decisions

1. **Skeleton art** — owned, or a purchase? Blocking everything downstream.
2. **Armored skeleton** — new mesh, or material and scale variant? (Recommendation: variant.)
3. **Elite** — does it get the project's first skill, or just bigger numbers? (Recommendation:
   attempt the skill, cut it first if the schedule slips.)
4. **Chests** — breakable harvestable, or openable container? (Recommendation: breakable.)
5. **Demo target** — LAN/offline only, or wire up MMO? (Recommendation: LAN/offline.)
6. **Roll cost** — cooldown and stamina both, at ~0.7 s and ~25/100? (Recommendation: both.)

## Related

- `CLAUDE.md` — hard rules, layout, gotchas
- `Documentation/EXTENDING.md` — the extension ladder referenced throughout, with hook names
- `Documentation/Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md` — what is ours
- [01_BATTLEGROUND_QUEUE_DESIGN.md](01_BATTLEGROUND_QUEUE_DESIGN.md) and
  [02_ARENA_1V1_2V2_DESIGN.md](02_ARENA_1V1_2V2_DESIGN.md) — both post-demo, both MMO-dependent
