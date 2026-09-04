# Changelog

All notable changes to this project are recorded here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Paths are relative to the project root (`D:\1. Unity projekt\MMORPG Granny`).

## [Unreleased]

### Added

- **`Documentation/Systems/05_CLASSLESS_EQUIPMENT_SKILLS_AND_TALENTS_DESIGN.md`** (2026-09-04) —
  design for classless characters where equipment grants castable spells, plus three talent-tree
  architectures on top. Design only, nothing implemented.
  - **The classless half is already implemented in the kit and unused by the demo content.**
    `BaseEquipmentItem.increaseSkills` is a `SkillIncremental[]` on every weapon, armor, shield and
    accessory (`BaseEquipmentItem.cs:138-145`), scaled by item level through `IncrementalInt`, and
    `ItemRandomBonus.randomSkillLevels` rolls spells onto drops (`ItemRandomBonus.cs:19`). It
    aggregates through `CalculatedItemBuff.Build` (`:107-108`) and
    `CharacterDataExtensions_Stats.GetBuffs` (`:161`) into `CharacterDataCache.Skills` (`:29`).
    Every consumer already reads that merged cache rather than the learnt list — server-side cast
    validation at `CharacterDataExtensions.cs:1324`, `BaseSkill.IsAvailable` (`:499-502`), the
    hotkey bar, the hotkey assigner and the skill window. `UICharacterHotkey.cs:157` carries the kit
    author's own comment, `// Get all skills included equipment skills`. Recorded because the
    feature is invisible in the demo data and looks absent.
  - **A passive skill's `increaseSkills` is silently discarded, and this is the expensive one.**
    `GetAllStats` merges `buffSkills` into `resultSkills` once, at
    `CharacterDataExtensions_Stats.cs:989`, then walks passive skills *after* that merge and writes
    their granted skills back into `buffSkills` (`:1006`), which is never re-combined before
    `resultSkills` is emitted (`:1042-1043`). So line 1006 is dead code for the skill dictionary.
    Equipment, equipment sets, active buffs, summons, vehicles, titles and factions all work,
    because all of them are aggregated before line 989. There is no error and no warning, and the
    field is fully editable in the inspector. This kills the obvious talent design — a passive node
    that adds ranks to a gear-granted spell — and it fails quietly.
  - **Grant and gate are separate axes, and both already exist.** `item.increaseSkills` decides what
    is in the spellbook; `BaseSkill.availableWeapons` (`:87`), `availableArmors` (`:90`) and
    `requireShield` (`:84`) decide what is castable right now, checked in `CanUse`
    (`:1030-1080`) and only for `BasePlayerCharacterEntity` (`:1015`), so monsters are unaffected.
    Two data fields give a character who owns a spell permanently but must hold the right weapon to
    cast it, and make weapon-swap hotbars re-resolve for free.
  - **A skill asset must be either grantable or learnable, never both.** Granted and learnt levels
    are different additive sources that meet only at the merge, while `UICharacterSkill.cs:273`
    offers a level-up button based on the *merged* level — so a player can spend a skill point on a
    staff's spell and keep it after unequipping the staff. The fix is
    `requirementEachLevels[].disallow` (`BaseSkill.cs:424-430`), which short-circuits `CanLevelUp`
    and therefore stops both the UI and the server's `AddSkill`.
  - **Rejected: filtering the skill window to hide gear-granted skills.**
    `UICharacterSkills.UpdateData` populates from the cache (`:156`), so hiding them there hides
    from the player what their gear actually does, and it would not stop `AddSkill`, which validates
    through `CanLevelUp` and nothing else. One `disallow` flag fixes UI and server together.
  - **`SkillRequirement` is already a talent-tree node definition.** `skillLevels` are the DAG's
    edges, `skillPoint` the currency, `attributeAmounts` and `characterLevel` the gates, and
    `requirementEachLevels` is a list with one entry per rank, so multi-rank talents with escalating
    costs are pure data. `UISkillRequirement.cs:170-185` already renders prerequisites, and
    `ResetSkills`/`ResetAttributes` (`PlayerCharacterDataExtensions.cs:382`, `:453`) already respec.
    What is missing is only the tree *layout* — xNode is vendored but wired solely to
    `NpcDialogGraph`.
  - **Skill-specific talent scaling belongs in a `BaseGameplayRule` subclass.** `GetTotalDamage`
    (`BaseGameplayRule.cs:83`) receives the `BaseSkill` being cast and the instigator, which is the
    only seam that can multiply damage for one named spell; `DefaultGameplayRule` already walks
    `GetCaches().Skills` this way for passive combat effects (`:749`, `:810`).
  - **Rejected: registering talent-mapping assets in `GameDatabase_G`.** Same reasoning as doc 04 —
    `GameDatabase` holds a fixed set of typed arrays (`GameDatabase.cs:25-63`) with no generic slot.
    Reference the mapping list from the gameplay-rule asset instead.
  - **Set `id` explicitly on every skill asset before authoring the item side.** `DataId` hashes the
    asset name while `id` is empty (`BaseGameData.cs:30`, `:180`) and every project asset currently
    leaves it empty. Under this design a single spell is referenced from dozens of item assets, so
    the cost of a later rename scales with the catalogue.

- **`Documentation/Systems/04_CAMERA_SHAKE_DESIGN.md`** (2026-09-03) — design for a camera shake
  system with a locally callable API and server-decided shakes. Design only, nothing implemented.
  - **A server-decided shake mostly needs no networking.** The server already replicates a boss's
    skill to every observing client (`[AllRpc] RpcUseSkill`,
    `DefaultCharacterUseSkillComponent.cs:518`) and each of those clients already instantiates
    `skill.SkillActivateEffects` locally (`GameEntityModel.InstantiateEffect:263`, called at
    `DefaultCharacterUseSkillComponent.cs:276/285/292`), and `PoolSystem` fires the pooled prefab's
    serialized `onGetInstance` UnityEvent on every spawn (`PoolDescriptor.cs:16`, `:24`). A shake
    component on the effect prefab, wired to that event, is therefore server-decided, correctly
    interest-filtered and free. Recording this because the obvious design — a new RPC and a new
    message id — is the *second* tier, not the first.
  - **The camera pose is rewritten absolutely every frame, which is what makes the shake easy.**
    `FollowCamera` is `[DefaultExecutionOrder(int.MinValue)]` (`FollowCamera.cs:7`) and assigns
    `CacheCameraTransform.position/rotation` outright from `LateUpdate` (`:207-208`). So anything
    running earlier is silently discarded, and anything running later gets a clean base pose and
    cannot accumulate drift — no save/restore, no reset when the shake ends. The kit already relies
    on this once, for weapon recoil (`FollowCameraControls.cs:207`, applied at `:212-221`).
  - **Rejected: a parent "shake pivot" so the camera's own transform stays clean.** The pose is
    written in *world* space onto the camera's own transform (`FollowCamera.cs:45`, `:79`,
    `:207-208`), so a parent offset is ignored outright, and moving the `Camera` onto a child just
    relocates the problem. There is no clean transform to hide behind; post-processing the pose is
    the mechanism, not a workaround.
  - **Rejected: reusing `FollowCameraControls.Recoil`.** It is rotation-only (`:221`), a spring
    toward zero rather than a windowed envelope (`:219-220`), has one global amplitude with no
    per-source blending, and `ShooterRecoilUpdater.cs:271` already owns it for gun recoil. Sharing
    it would make weapon recoil and world shake fight over one accumulator. Both are additive
    offsets on a pose rebuilt each frame, so a second component sums with it for free.
  - **Camera shake shakes the *aim* in this project, not just the picture.**
    `TopDownAimController.TryGetCursorWorldPosition` builds its ray with
    `camera.ScreenPointToRay` (`:216`) and the result drives the character's replicated facing
    (`:200`). The shake lands in `LateUpdate` and the aim is read in `Update`, so the aim uses the
    previous frame's shaken pose — a real one-frame wobble on a stationary mouse. The fix is to
    cache the pre-shake pose and aim from that; the kit's own click-pick
    (`PlayerCharacterController_Inputs.cs:32`) keeps the jitter, accepted rather than patching
    `Core/`. This is the assumption to prove before building anything else.
  - **`[AllRpc]` handlers run on the server too**, so client-only presentation must be a no-op
    there and not merely unlikely to run: host connections dispatch by direct `HookCallback()`
    (`LiteNetLibRPC.cs:78-83`) and a dedicated server invokes the callback as well
    (`:95-97`). The design leans on one guard — the static camera reference being null — so that no
    call site anywhere needs an `IsClient` check.
  - **Never add a new `LiteNetLibBehaviour` to an existing networked prefab to carry the RPC.**
    `behaviourIndex` is the position in `GetComponentsInChildren<LiteNetLibBehaviour>()`
    (`LiteNetLibIdentity.cs:584`) and is hashed into every RPC and sync-element id
    (`LiteNetLibBehaviour.cs:1193-1207`), so inserting a component silently renumbers everything
    after it, child objects included. Same-build client and server still agree, which means this
    fails only across mismatched builds. A `partial class` on an existing behaviour moves no index.
  - **Camera shake profiles should not go in `GameDatabase_G`.** `GameDatabase` holds a fixed set
    of typed arrays (`GameDatabase.cs:25-63`) with no generic slot; a new type would need the
    `partial` field plus a `[DevExtMethods("LoadDataImplement")]` hook (`:18`, `:136`). That route
    works, but only the RPC tiers ever name a profile over the wire and a standalone profile set
    keyed by `GenerateHashId` (`BaseGameData.cs:180`, `:184`) covers it without putting
    presentation tuning in the asset that governs items, skills and quests. Also rejected: sending
    raw shake parameters (freezes tuning into the server build, grows with every new field) and
    sending an array index (breaks on reorder — hashed string ids do not).
  - **The tier-1 spawn hook is reliable for `GameEffect` and quietly is not for networked prefabs.**
    `PoolSystem.GetInstance` invokes `OnGetInstance()` after its if/else, so it fires on both the
    dequeue and the fresh-instantiate branch (`PoolSystem.cs:108`), with an uninitialised pool
    routed through `InitPool` and recursion (`:113-114`). `LiteNetLibAssets.GetObjectInstance`
    invokes it **only** when dequeuing (`:324`) and not when instantiating (`:331-334`), and
    `disablePooling` skips pooling entirely (`:274`, `:286`) — so a shake wired to a networked
    prefab's `onGetInstance` works until the pool exceeds `PoolingSize` (`:302`), i.e. fails exactly
    when the fight is busiest. Correct as the *reset* hook the kit uses it for
    (`AreaDamageEntity.cs:33`); wrong as a fire-on-spawn hook. `OnEnable` is not the fix either:
    pool pre-warm instantiates each instance active before deactivating it (`:304-306`), giving
    `PoolingSize` phantom firings at load, and `NetworkSpawn` calls `SetActive(true)` (`:465`)
    before `Initial(...)` assigns an object id (`:466`), so there is nothing to guard on. Use
    `OnStartClient` (`LiteNetLibAssets.cs:472-473`, dispatched at `LiteNetLibIdentity.cs:642-644`).
  - **Skill effects anchor to the caster's socket, not to the world.** `InstantiateEffect` requires a
    non-empty `effectSocket`, resolves it against the model's containers, spawns at that container
    and sets `FollowingTarget` to it (`GameEntityModel.cs:269-280`). So a stomp shakes outward from
    the boss, which is right, but a meteor's activate effect would shake outward from its *caster*,
    which is not. Ground-targeted abilities must take their origin from the `AreaDamageEntity`,
    network-spawned at the aim position (`SimpleAreaAttackSkill.cs:148-153`). A third anchor exists
    for "you personally were hit": `DamageableEntity.PlayHitEffects` off `[AllRpc]
    RpcAppendCombatText` (`:236`, `:303`).
  - **`SkillActivateEffects` lead the impact.** They spawn after the cast delay but *before* the
    action animation plays (`DefaultCharacterUseSkillComponent.cs:270-297`), so an un-delayed shake
    fires before the foot lands. Recording it because it reads as a physics bug, not a timing one.
  - **Tier 1 costs nothing on a dedicated server.** Skill effect instantiation is inside
    `if (IsClient)` (`DefaultCharacterUseSkillComponent.cs:271`) and hit effects behind
    `if (!IsClient) return;` (`DamageableEntity.cs:255`), so the server never builds the effect that
    would carry the shake.
  - **Falloff must be measured from the camera's follow target, not the camera.** The camera is
    offset back by `zoomDistance` (`FollowCamera.cs:183`), so measuring from it makes the same
    explosion feel weaker the further a player has zoomed out — a difficulty difference produced by
    a camera setting.
  - **The aim wobble camera shake causes is accepted, not fixed** (decided 2026-09-03). Shaking the
    camera shakes the cursor ray and therefore the character's replicated facing; the fix exists and
    needs no kit edit (cache the pre-shake pose, aim from that), but the wobble is bounded by the
    shake amplitude — degrees and centimetres — and only exists while a shake runs. This removed a
    build step and the `UnshakenPosition`/`UnshakenRotation` requirement from the shaker. The
    mechanism stays documented so a twitching character during a stomp is not re-investigated as a
    bug, and it puts a ceiling on rotation amplitude: the decision is only cheap while the numbers
    stay small.
  - **Radius needs two numbers, not one.** A single "shake within 30 m" starts fading the moment you
    step off the spawn point, so `innerRadius` (full strength, roughly the ability's visual
    footprint) and `outerRadius` (zero beyond, the advertised radius) with a tuned curve between.
    Rejected both closed-form falloffs: linear makes the boundary noticeable as it sweeps past, and
    true inverse-square spends almost everything in the first few metres and wastes the rest.
  - **`outerRadius` is capped by the source entity's network visible range**, 80 m by default
    (`BaseInterestManager.cs:10`, `:58`; per-prefab override at `LiteNetLibIdentity.cs:55-56`,
    `:172`). A larger radius does not error — players beyond the interest range never receive the
    effect at all, so the falloff curve silently claims strength that was already truncated by
    replication. Not a concern at 30 m; it is the thing that makes a zone-wide rumble a tier-3
    problem rather than a bigger number.
  - **Magnitude is one multiplication chain**, profile amplitude x envelope(t) x falloff(distance) x
    per-emitter scale x the player's accessibility setting, with every term after the first in 0-1.
    So a profile is tuned once for "epicentre, setting at 100%" and every other case derives, no
    per-call-site magic numbers, and no call path can bypass the accessibility slider.
  - **Falloff distance is sampled once at spawn, not tracked.** Cheaper (one distance test per client
    per effect) and better behaved: a shake whose strength changed as you walked would read as a bug.
    It also makes the caster-following behaviour of skill effects irrelevant to the shake.
  - **`[AllRpc]` reaches current subscribers only, and default interest is 80 m**
    (`LiteNetLibRPC.cs:86`, `BaseInterestManager.cs:10`, `:58`). That is the right filter for a
    stomp for free, and simultaneously a hard ceiling: a zone-wide rumble cannot ride an entity RPC
    and needs a manager message instead.
  - **The world-space UI camera is already handled.** `CharacterUICamera` is a child at local zero
    on `TopDownGameplayCamera.prefab` and `CopyCamera` copies lens properties only, never the
    transform (`Utils/CopyCamera.cs`), so shaking the root keeps nameplates welded to the world.
  - **Shake would be inert in MMO mode today.** `00Init_MMO.unity` still uses the kit's
    `GameInstance.prefab` with the stock camera prefab, so a shaker installed on our
    `TopDownGameplayCamera.prefab` never exists there. The server halves of all three tiers are
    unaffected; only the receiving end is missing.

- **`Documentation/Systems/03_BOSS_ENCOUNTER_DESIGN.md`** (2026-09-03) — survey of how complex a
  boss can be in this kit, and the design for phase-scripted encounters. Design only, nothing
  implemented.
  - **The kit already has exactly one phase primitive, and it is data-only.** `MonsterSkill.useWhenHpRate`
    gates a skill behind `entity.HpRate <= rate` inside `MonsterCharacter.RandomSkill`
    (`MonsterCharacter.cs:264`), so "new abilities below 50%" needs no code at all. Recording this
    because it is easy to miss and easy to reimplement by accident.
  - **That primitive has three hard limits, all in one method.** The gate is one-directional, so
    phase-one abilities can never be retired, only added to. A single `Random.value` is drawn once
    (`:258`) and compared against every skill's `useRate` in shuffled order, so the inspector's
    per-skill rates are not the real selection frequencies. And selection is a shuffle, so ordered
    rotations cannot be expressed. Real phases therefore need code, not tuning.
  - **`MonsterActivityComponent` is referenced from exactly one place in the entire kit**, the editor
    utility that builds a monster prefab (`Core/Editor/CharacterEntityCreatorEditor.cs:193`). Nothing
    resolves it at runtime. So a boss prefab can carry a subclass instead and the kit never notices —
    this is the seam the design is built on, and it costs no kit edit. Rejected the alternative of
    hooking `MonsterActivityComponent` via `[DevExtMethods]`: the class invokes no hooks, and its
    combat loop `UpdateAttackEnemy` is `private` (`:270`), so decoration cannot reach it.
  - **Per-encounter state must never live on the `MonsterCharacter` asset.** Proved by the kit's own
    `_tempRandomSkills` (`MonsterCharacter.cs:177`), a mutable list on the `ScriptableObject` that
    every spawned instance of that monster shares. Two copies of a boss would otherwise share a phase.
  - **Phase changes should ride a `Buff`, not direct field writes.** `Buff.isOverrideDamageInfo`
    replaces the character's basic attack wholesale (`CharacterDataCache.cs:457`, `:471`), and buffs
    already replicate, stack and surface in UI. Writing stats directly would need new network plumbing.
  - **There is no threat model anywhere in the kit, but the ledger it needs is already built and
    unused.** `threat`, `aggro`, `taunt`, `enmity` and `provoke` return zero hits across `Core/`,
    `MMO/`, `GuildWar/` and the demos, and target selection is the first survivor from an overlap
    query (`MonsterActivityComponent.cs:463`, `:526`) with a coin-flip re-roll on every hit (`:136`).
    But `BaseCharacterEntity_DamageFunctions.cs:11` keeps a per-attacker cumulative damage table fed
    on every damage application (`:208`), crediting summon damage to the summoner (`:246-248`) and
    clamping overkill (`:254`) — and ships `GetSortedReceivedDamageRecordsByDamage` (`:278`) and
    `...ByTime` (`:288`) which **nothing in the kit ever calls**. The only live consumer is reward
    attribution on death (`BaseMonsterCharacterEntity.cs:469`). Recording this because "build threat
    from scratch" is the wrong cost estimate: the data collection and sorting exist, the AI just never
    reads them. What is genuinely missing is threat decay, per-skill threat modifiers, healing threat,
    and a reset that is not death (the ledger clears only in `Killed`, `:71`).
  - **Taunt as a debuff works, because a `CharacterBuff` records who applied it.**
    `CharacterBuff.BuffApplier` returns an `EntityInfo`
    (`Core/Scripts/CharacterData/RelatesData/CharacterBuff.cs:10`), set inside `ApplyBuff` on both the
    refresh path (`BaseCharacterEntity_BuffFunctions.cs:83`) and the fresh-buff path (`:151`). So a
    taunt debuff is not just a flag, it names the taunter. Taunt-over-taunt is already correct too:
    with `maxStack <= 1` the old buff is removed and the new one records the new applier (`:105-112`).
    The only code needed is the lookup plus a target lock in the activity component.
  - **The two ways to land that debuff use different `BuffType` values and different asset fields.**
    `isDebuff` + `debuff` is applied on hit as `BuffType.SkillDebuff` (`BaseSkill.cs:906-909`) and can
    miss; `skillBuffType = BuffToEnemy` applies the `buff` field directly with no hit roll, as
    `BuffType.SkillBuff` (`Skill.cs:214-221`). Querying `IndexOfBuff` with the wrong one returns -1
    silently, which is exactly the kind of failure that looks like a broken taunt.
  - **`BuffApplier` is server-side only** — `SetApplier` runs inside `ApplyBuff`, which returns early
    on `!IsServer` (`BaseCharacterEntity_BuffFunctions.cs:11-12`). Fine for AI, since the monster AI is
    server-only anyway, but a "taunted by X" client indicator would need the identity replicated
    separately.
  - **The applier cache never evicts per entry.** `MemoryManager.CharacterBuffs` is keyed by the
    buff's unique id (`CharacterBuffCacheManager.cs:38-46`) and `BaseCacheManager.GetOrMakeCache` only
    inserts (`BaseCacheManager.cs:42-51`). One entry accumulates per buff instance ever applied, which
    a boss taunted every few seconds for hours will notice.
  - `BaseMonsterCharacterEntity.SetAttackTarget` is public (`:313`) and called from nowhere but the AI
    component, so a skill could redirect the boss directly instead. Rejected as the primary route: the
    buff gets duration, stacking, replication, UI and `restrictTags` taunt-immunity for free, where a
    direct call gets none of them.
  - **Skill-shot bosses need almost no code, because the AI already aims at a point.**
    `MonsterActivityComponent.cs:352-357` builds `AimPosition{type = Position}` and passes
    `targetObjectId: 0`, so with `hitOnlySelectedTarget` false by default (`Damage.cs:25`) the
    missile's `_lockingTarget` stays null. That field is a damage filter, not homing
    (`MissileDamageEntity.cs:399`), so the projectile hits whoever it collides with — a boss can miss.
  - **Cast time is literally the dodge window.** The aim position is stamped at cast start and carried
    unchanged through `FrameBasedDelay(CastingSkillDuration)`
    (`DefaultCharacterUseSkillComponent.cs:200`, `:267`) before `ApplySkillUsing` (`:372`). So a
    dodgeable boss attack needs only a non-zero `castDuration` and a projectile with travel time
    (`MissileDamageEntity.cs:128`) — no code at all.
  - **`applyDuration` on an area skill is a free telegraph window.** `AreaDamageEntity` sets
    `_lastAppliedTime` at spawn (`:92`) and only ticks when `applyDuration` has elapsed (`:101`), with
    membership maintained by `OnTriggerEnter`/`OnTriggerExit` (`:143-187`). Stepping out before the
    tick takes **zero** damage, not reduced damage. That is a WoW-style ground AoE with no code.
  - **`TargetObjectPrefab` on `BaseAreaSkill` is not a networked telegraph** — it is the local aim
    preview for the player's own area skills, referenced only by `DefaultAreaSkillAimController.cs:28-31`
    and `ShooterAreaSkillAimController.cs:25-28`. A monster casting the skill spawns nothing. The boss's
    visible warning must live on the `AreaDamageEntity` prefab, which is what network-spawns. Recording
    this because "I authored a boss AoE and there is no circle" has exactly one cause.
  - **`GetDefaultAttackAimPosition` is `virtual`** (`BaseSkill.cs:1276`) and defaults to the target's
    current position. Overriding it in a `Skill` subclass is the whole skill-shot design space — lead
    prediction, spread fans, scatter, aim-where-they-were — as content rather than architecture.
    `BaseAreaSkill` already overrides it to the feet rather than the aim transform (`:104`).
  - **Threat is deferred, not rejected** (2026-09-03). The skill-shot model answers the question threat
    exists to answer: who gets hit is decided by who failed to move. The open decision in the document
    is marked deferred rather than removed, and the damage-ledger findings above stand for whenever it
    is picked up.
  - **Two traps that would have cost a day each.** The AI halts entirely on
    `Identity.CountSubscribers() == 0` (`:148`), so an enrage timer driven from the activity component
    freezes when the arena empties; and `findEnemyDelayMax` is dead because
    `Random.Range(findEnemyDelayMin, findEnemyDelayMin)` passes `Min` twice (`:235`). The latter is a
    kit bug — override it, do not patch `Core/`.

- **`Documentation/Systems/02_ARENA_1V1_2V2_DESIGN.md`** (2026-09-03) — design for ranked 1v1 and
  2v2 arena on top of the battleground queue transport from doc 01. Design only, nothing implemented.
  - **Friend/foe is a property of the map, not of combat code.** `DamageableEntity.IsAlly`/`IsEnemy`
    delegate straight to `CurrentMapInfo` (`DamageableEntity.cs:443`, `:450`), which dispatches to
    four `protected abstract` members on `BaseMapInfo` (`BaseMapInfo.cs:330-333`). A map type
    therefore defines its own factions with no kit edit, which is what makes 2v2 friendly-fire
    suppression possible. `GuildWarMapInfo.cs:102` is the in-repo precedent, keying off `GuildId`;
    arena keys off a team index read through `EntityInfo.TryGetEntity`.
  - **Team index must be `Public` custom data, not `Server`.** Ally checks run on clients too, for
    nameplates and targeting, and only public custom data replicates. Doc 01 suggested `Server`
    visibility for the battleground team; that is corrected here for anything the client renders.
  - **Queue-pop latency is solved by configuration, not code.** `ClusterServer.HandleRequestSpawnMap`
    consumes a pre-allocated warm map server before asking a map-spawn server to boot one
    (`ClusterServer.cs:493-503`), and the pool is an inspector list on `MapSpawnNetworkManager`
    (`spawningAllocateMaps`, `:56`). Booting a Unity process per 1v1 was rejected as the default:
    arena matches are short and frequent, unlike battlegrounds.
  - **Leaderboards have a measured trap.** Rating fits custom character data with no schema change
    (`character_public_int32` is `(id, characterId, hashedKey, value)`), but the only indexes are
    `PRIMARY(id)` and `KEY(characterId)` (`mysql_main.sql:790-794`), so `WHERE hashedKey=? ORDER BY
    value DESC` is a full scan over every public custom value for every character. Chosen fix is a
    cluster-side cached top-N; adding a composite index is an ops runbook step, explicitly **not** an
    edit to `mysql_main.sql` or `MySQLDatabase_Migrate.cs`, which a kit update would revert.
  - **The dueling system is a reference, not a dependency.** `PlayerCharacterDuelingComponent` has
    the round shape worth copying — countdown separate from duration (`:336`), and disconnect-as-loss
    via `OnDestroy` (`:369`) — but it is built around a consensual open-world request/accept
    handshake, so arena drives its own match component instead. Arena maps must set
    `DisableDueling` true, or players can start side-duels inside a ranked match (`:101`).
  - **2v2 starts premade-only**, so team assignment falls out of existing party membership and the
    balancing question is deferred rather than guessed at.

- **`Documentation/Systems/01_BATTLEGROUND_QUEUE_DESIGN.md`** (2026-09-03) — amended with the four
  abstract ally/enemy members, missed in the original draft. Not cosmetic: `BattlegroundMapInfo`
  does not compile without implementing all four.


- **`Documentation/Systems/01_BATTLEGROUND_QUEUE_DESIGN.md`** (2026-09-03) — design for a
  battleground queue that spawns a dedicated instance once enough players have queued. Design only,
  nothing implemented yet.
  - **The kit's own instance warp cannot serve a queue.** `WarpCharacterToInstance`
    (`MMO/.../MapNetworkManager_PlayerActivity.cs:81`) requires the participants to be in one party,
    requires the caller to be the party leader, and requires members to be standing within
    `joinInstanceMapDistance` of the leader. A queue is by definition strangers on different map
    servers, so the built-in path was rejected outright rather than adapted.
  - **You also cannot join an instance by id.** `ClusterServer.HandleRequestSpawnMap` overwrites
    whatever `instanceId` the caller sends (`ClusterServer.cs:496`), so every `SpawnMap` request
    yields a fresh instance and there is no "join instance X" request type. Any design that assumes
    otherwise will not work.
  - **Chosen mechanism: spawn one instance, reuse its `peerInfo` for N players.** The spawn response
    carries a `CentralServerPeerInfo`, and the private `SaveAndWarpCharacterByPeerInfo`
    (`MapNetworkManager_PlayerActivity.cs:156`) will move any character to any peer. `MapNetworkManager`
    is `partial`, and parts of a partial class share private access, so a file of ours in
    `Assets/Scripts/` can call it directly — no reflection, no kit edit. This is the load-bearing
    trick of the design.
  - **Queue lives on the cluster**, beside parties and guilds, because no single map server can see
    players queued on other map servers. Registered from a `[DevExtMethods("OnStartServer")]` hook on
    the partial `CentralNetworkManager`, which exposes `ClusterServer` publicly.
  - **`BaseGameNetworkManagerComponent` preferred over `[DevExtMethods]`** for the map-server and
    match-runner pieces: compile-checked overrides instead of silent string hook names,
    inspector-configurable, and it can be attached to the instance manager prefab only so match logic
    never loads on world servers.
  - **Measured constraint: an empty instance quits after 30 s.** `TERMINATE_INSTANCE_DELAY = 30f`
    (`MapNetworkManager.cs:18`, checked at `:167`), timed from when the instance learns its map
    (`:1070`). All participants must finish save-warp-reconnect inside that window, which rules out
    pre-spawning instances to wait for a queue to fill. The constant is `const`, so raising it would
    be a stock-kit edit and is avoided by design.
  - **MMO-only.** LAN has no instances: `LanRpgNetworkManager.IsInstanceMap()` returns `false`
    (`:452`) and its `WarpCharacterToInstance` forwards to a normal warp with a kit `TODO` (`:445`).
    Phase 0 of the plan is therefore wiring `00Init_MMO.unity` to this project's assets, which
    `CLAUDE.md` already flags as unfinished.
  - Team assignment crosses the map transfer via custom character data rather than a schema change,
    since `SaveAndWarpCharacterByPeerInfo` does a full save on the way out.

### Changed

- **No attacking while rolling** (2026-09-02) — `DirectionalRollDash` now answers
  `onCanAttackValidated` with false while `IsRolling`, so the swing is refused before it starts
  rather than being cut off. The kit runs that validation on the server as well as on the attacker,
  and `IsRolling` is true on every copy because the roll broadcast drives it, so a client cannot
  attack by simply asking again. Together with `dashRestricted` already set on the five weapons,
  attack and roll now exclude each other in both directions.
  - The block ends when the roll does. Cancelling the get-up early by moving clears the roll, and
    with it the block, so the roll's tail is not dead time if you move out of it.
  - `blockSkillsWhileRolling` added alongside, wired to `onCanUseSkillValidated` and
    `onCanUseSkillItemValidated`, but left **off**: only plain attacks were asked for. Turn it on
    to stop a skill being used as a mid-roll attack.

- **Backward movement is full speed** (2026-09-02) — `CharacterControllerEntityMovement` on
  `SyntyPlayerCharacter` shipped every backward rate at 0.75, so walking or strafing away from the
  aim was a quarter slower than towards it. All eight backward fields set to 1
  (`standBackwardMoveSpeedRate` and `standBackwardSideMoveSpeedRate` plus the crouch, crawl and
  swim pairs). Only the stand pair can be reached today - crouch and crawl are vetoed by
  `DisabledMovementStates` - but the others are set too so the character does not change behaviour
  if a pose is ever re-enabled. Forward and side rates were already 1, so movement speed is now
  the same in every direction.
  - This is a per-character setting, not a global one: monsters and any other character prefab keep
    the kit's 0.75.

- **Roll: 20% longer, costs stamina, cannot be spammed** (2026-09-02) — three changes on
  `DirectionalRollDash` / `SyntyPlayerCharacter`.
  - **Distance.** `rollDistance` 3.7 -> 4.98, which is +20% *on the ground* (4.22 m -> 5.07 m,
    simulated at 60 Hz against the profile). Not a 20% bump of the field: speed is floored at
    walking pace through the roll's slow middle, so a fixed part of the travel does not scale.
    Duration, clip and animation speed unchanged.
  - **Cooldown**, the actual anti-spam: new `rollCooldown` (1.5 s) refuses the dash through
    `onCanDashValidated`. Measured from the roll's start and deliberately kept in its own field
    rather than reusing `_rollStartTime`, which the early get-up cancel clears - otherwise
    cancelling out of a roll would also clear its cooldown, which is exactly the spam being fixed.
    Safe to answer false mid-roll: the kit reads `CanDash` when *starting* a dash, and a roll in
    flight is carried by its force applier, not by that flag.
  - **Stamina.** New `staminaCost` (20) also gates `onCanDashValidated`. The pool is 100 and the
    gameplay rule recovers 3/s, so five rolls back to back and ~6.7 s to earn one back; sustained
    rolling drains 13/s against 3/s recovery. Deducted in `StartRoll` under `if (IsServer)` -
    `currentStamina` is a `ServerToClients` sync field, so only the server's write propagates, and
    the server's copy runs `StartRoll` exactly once when it relays the roll broadcast.
  - Both gates run on every copy: the local player refuses its own input, and the server refuses a
    client that asks anyway. Set `staminaCost` to 0 for a free roll on cooldown alone.

- **Rolls synced to other clients** (2026-09-02) — observers used to see the forward roll whatever
  direction you rolled: every copy of the character ran `DirectionalRollDash` locally, and only the
  owner has movement input, so the others fell back to "along facing". The clip choice is now
  broadcast instead of guessed.
  - `DirectionalRollDash` changed base from `MonoBehaviour` to the kit's
    `BaseNetworkedGameEntityComponent<BaseGameEntity>`, so it is a `LiteNetLibBehaviour` on the
    character's identity and can own RPCs. Two `[AllRpc]` methods: `RpcPlayRoll(byte clipIndex)`
    and `RpcStopRoll()` (the early get-up cancel), both reliable-ordered.
  - Only the copy holding the input (`BasePlayerCharacterController.Singleton.PlayingCharacterEntity`
    is this entity) picks the direction and the clip. It plays at once and broadcasts; every other
    copy plays nothing on its own and waits for the call, so there is no wrong-clip-then-correct pop.
  - The server echoes an All-RPC back to its sender, which would restart the clip mid-roll a round
    trip in. `StartRoll` therefore ignores a repeat of the same clip index within 0.3 s - long
    enough for any sane round trip, shorter than the earliest possible second roll (~0.82 s, the
    movement unlock). On a host the echo hooks immediately and is swallowed the same way.
  - Facing lock and the stand-up movement veto are now gated to the local player. Remote copies are
    positioned and rotated from the network, so running either there fought the synced transform.
  - `IsRolling` is set on every copy (the RPC drives it), so the `ActionLayerMaskUpdater` exemption
    that keeps a roll full-body works for observers too, not just the local player.
  - Behaviour ordering note: the identity now lists five behaviours, with `DirectionalRollDash`
    last. RPC ids are index-based, so every peer must run the same prefab build - which is already
    true for a LAN game shipped as one binary.

- **`.gitignore`** (2026-09-02) — the 5000 Fantasy Icons pack is no longer committed.
  `Assets/5000FantasyIcons/` is ~555 MB across 6,292 PNGs, purchased and re-downloadable, and
  every file would otherwise go through Git LFS. Nothing under it was tracked yet, so the two rules
  (folder + `.meta`) are enough. Same convention as the Synty, BLINK and SFX entries: icons
  referenced from items will show as missing sprites after a fresh clone until the pack is
  re-imported to the same path.

- **Roll get-up is cancelable** (2026-09-02) — the stand-up lock used to hold input until the
  full 1.167 s clip had played while the movement was done by ~0.75 s, a visible half-second
  freeze. `DirectionalRollDash` gained `movementUnlockAt` (normalized clip time, 0.7 = 0.82 s)
  and `cancelClipWhenMoving` (on): input is accepted again from the unlock point, and a held
  movement key there stops the roll clip (`PlayableCharacterModel.StopCustomAnimation`) so the run
  blends in at once; standing still lets the get-up finish. The cancel only fires once the dash
  force is gone, so it cannot cut the travel short. Raise `movementUnlockAt` toward 1 for the
  full get-up every time, lower it for a snappier roll.

- **Directional roll: real input, facing held, four clips** (2026-09-02) — the first cut still
  rolled forward along facing. Root cause: `PlayerCharacterController` stamps every WASD movement
  as plain `MovementState.Forward` (no Left/Right/Backward flags), so the direction rebuilt from
  the synced flags was always "forward". `DirectionalRollDash` reworked:
  - **Direction** now comes from the raw axes through the controller's own public
    `GetMoveDirection(h, v)` on the owner client (`BasePlayerCharacterController.Singleton`), so
    it is camera-relative exactly like walking. Without that input (server copy of a remote
    player) it falls back to facing.
  - **Facing held.** The kit re-targets the yaw to the dash direction every movement tick
    (`_targetYAngle` in `BuiltInEntityMovementFunctions3D`, no `CanTurn` gate). The component
    captures the yaw when the roll begins and re-applies it from `LateUpdate` through
    `SetLookRotation(..., immediately: true)`, after the movement update and before rendering.
    The character keeps aiming at the mouse and rolls sideways or backwards.
  - **Four clips.** The model's `dashStartState` is now empty; instead the four in-place Synty
    rolls sit in `PlayableCharacterModel.customAnimations[0..3]` (F, B, L, R; speed 1, 0.1 s
    transition, no mask) and the component calls `PlayCustomAnimation` with the one matching the
    angle between facing and roll direction: forward within 45 degrees, backward beyond 135,
    left/right between.
  - **Full body while moving.** The project's `ActionLayerMaskUpdater` trims any action layer to
    `SyntyUpperBody` while the character moves on the ground, which would have turned the roll
    into an upper-body flail. It now leaves the mask alone while `DirectionalRollDash.IsRolling`.
  - **No dash mid-swing.** `movementRestrictionWhileAttacking.dashRestricted` set on all five
    project weapons: `PlayCustomAnimation` refuses to play while an action runs, which would have
    left a clip-less slide.
  - Remote observers (other LAN clients) do not receive the direction, so they see the roll
    along facing with the forward clip; syncing the choice is a later job.
  - Compile note: `PlayableCharacterModel` lives in `MultiplayerARPG.GameData.Model.Playables`,
    not `MultiplayerARPG` - the first compile failed on that and Unity kept the old assembly.

- **Roll follows WASD, moves with its animation, longer** (2026-09-02) — new
  `Assets/Scripts/Gameplay/DirectionalRollDash.cs` on `SyntyPlayerCharacter`, an
  `IEntityMovementForceUpdateListener` the movement system picks up from the same object. It
  works inside the kit's dash rather than around it, on the movement tick, so client and server
  agree.
  - **Direction.** The kit always dashes along facing. On the tick the movement creates the Dash
    force applier, the listener re-aims it along the held keys, rebuilt from the entity's synced
    `MovementState` flags (Forward/Backward/Left/Right relative to facing, 8 sectors). No keys =
    roll forward. The kit still turns the character into the roll, so it is a forward roll aimed
    where you move - the pack's B/L/R rolls stay unused.
  - **Speed from the authored curve.** The in-place clip has no travel; the root-motion twin
    does. Its `RootT` curve was sampled in the editor and baked into the component's
    `distanceProfile` (21 keys, linear): 4.42 m, front-loaded - 50% of the travel by 0.32 of the
    clip, 80% by 0.51, 90% by 0.64, then the stand-up. Each tick the applier's speed becomes the
    profile's slope times `rollDistance` (3.7 m), deceleration 0, duration = clip (1.167 s).
  - **The kit's walking-speed cut.** `UpdateForces` removes any dash whose speed is under
    `GetMoveSpeed(Forward, None)` - 5 m/s for `Warrior_G`, which the roll only exceeds in its
    first third (it averages 3.8 m/s). Left alone it would cut at 45% of the clip and deliver
    3.3 m, shorter than before. The speed is therefore floored at walking speed until
    `releaseAtTravel` (0.9) of the profile is covered, then dropped to 0 to release. Simulated at
    60 Hz: dash ends at 0.75 s (64% of clip), about 4.2 m delivered (old value 3.5 m). Raise
    `rollDistance` for more; the floor adds roughly 0.5 m on top of it.
  - **Stand-up lock.** The dash-start clip plays in full even after the applier is gone, and the
    legs are planted for the last third. From the moment the dash is released until
    `rollDuration` has elapsed, `onCanMoveValidated` answers false, so walking input cannot slide
    the feet. Never vetoed while the dash is alive - the kit cancels a dash whose entity cannot
    move - and a second Space press during the tail is swallowed for the same reason.
  - `dashingForceApplier` on the prefab is now speed 8 / deceleration 0 / duration 1.167; only
    the initial value matters since the listener rewrites it every tick.

- **Jump replaced by a dodge roll** (2026-09-02) — built on the kit's own dash: the controller
  already turns the `Dash` button into `MovementState.IsDash`, the movement layer pushes the
  character along its facing with `dashingForceApplier` and forces the turn, and the model plays
  `dashStartState` in full as a special move. No new gameplay code.
  - Animation: `A_DodgeRoll_F_Sword` from `Synty/AnimationSwordCombat/Animations/Polygon/Dodge`
    (the in-place variant, not `_RootMotion`; humanoid, 1.167 s) as `defaultAnimations.dashStartState`
    on `SyntyPlayerCharacter`, speed rate 1, 0.1 s transition. Loop and end left empty. The pack
    also has B/L/R rolls, but the kit's dash only goes forward, so they are unused.
  - Distance: `dashingForceApplier` set to speed 6.0, deceleration 5.14, duration 1.167 - a
    linear decay to zero exactly when the clip ends, 3.5 m per roll. Change `rollDistance` in
    the changelog math (speed = 2·d/t, decel = speed/t) if it should go further.
  - Input, three layers because Input Handling is *Both*: (1) new project asset
    `Assets/1. Data/InputActions_G.inputactions`, a copy of the kit's `Demo/InputActions` with the
    `Jump` bindings (Space, gamepad South) removed and `Dash` rebound from `=` to Space plus
    gamepad South; the `InputSettingManager` in `00Init` now points at it. (2) The same component's
    key table: `Jump` Space -> None, `Dash` `=` -> Space. (3) `ProjectSettings/InputManager.asset`:
    both legacy `Jump` axes had their positive button blanked (Space, joystick button 3) - the
    kit checks the legacy axis before the key table, and the controller tests Jump *before* Dash
    with an else-if, so a live legacy Jump would have swallowed Space.
  - Safety: `DisabledMovementStates` gained `disableJump` (on), vetoing `onCanJumpValidated` so
    no other controller or UI button can jump either.

- **Sprint, crouch and crawl disabled on the Synty character** (2026-09-02) — new
  `Assets/Scripts/Gameplay/DisabledMovementStates.cs` on `SyntyPlayerCharacter`, three bools all
  on. It subscribes to the entity's `onCanSprintValidated` / `onCanCrouchValidated` /
  `onCanCrawlValidated` and answers false, and `EntityMovementFunctions.ValidateExtraMovementState`
  drops any requested state back to None when those say no - so the veto holds for every
  controller, key binding and UI button without touching kit or addon code.
  - State before: sprint was live - `PlayerCharacterController` toggles it on Left Shift
    (`InputSettingManager` in `00Init`, keyCode 304) and `SimpleGameplayRule` applies 1.5x speed.
    Crouch (Left Ctrl) and crawl (Z) were bound but unreachable: neither `PlayerCharacterController`
    nor `TopDownAimController` reads them; only the Shooter controller does. The bindings are left
    in place; they are inert now.
  - The controller still flips its private `_isSprinting` toggle on Shift, which costs nothing:
    the movement layer overrides the state each tick, and stamina only drains on the applied state.

- **Loading screen: on top, fading, no camera gap** (2026-09-02) — three fixes on
  `CanvasLoading_G.prefab` and `UILoadingScreenView`.
  - **Sorting.** The visible problem was not the root order: Synty's `Screen_FantasyMenus_Loading_01`
    carries a nested `Background` Canvas with *Override Sorting* on at order **-100** (and
    `AssetDemo_FX` at 0), which put the backdrop underneath every gameplay canvas while the crest
    and bar floated above them. Both overrides cleared on the nested instance so they inherit the
    root, and the root raised from 100 to 32000 (`Canvas.sortingOrder` is a short; the project's
    other canvases sit at 0-2).
  - **Fade.** A `CanvasGroup` on the root, driven by the view on unscaled time: 0 -> 1 over 0.35 s
    on show, 1 -> 0 over 0.5 s on hide, `blocksRaycasts` on while visible. The Synty In/Out clips
    only animate the Content/Vignette groups, so the backdrop used to pop. The loaders'
    `finishedDelay` (0.6 s) already outlasts the fade-out.
  - **"No cameras rendering".** The home scene's camera dies with its scene and the gameplay
    camera only spawns with the character, so the editor showed its notice for the gap (a build
    would show a stale frame under the overlay). A `FallbackCamera` child - URP base camera,
    culling mask 0, solid black clear, depth -100, post-processing off, no AudioListener - is
    enabled by `Show()` and released from `LateUpdate` once the loaders deactivate the screen root.
    Real cameras render over it by depth, so it costs one clear while the screen is up and nothing
    otherwise.

- **Loading screen on the Synty Fantasy Menus template** (2026-09-02) — new
  `Assets/1. Data/Prefabs/UI Prefabs/CanvasLoading_G.prefab` replaces the kit's `CanvasLoading`
  instance in `Demo/Scenes/00Init.unity` (a stock-scene edit; re-apply after a Demo reimport).
  - Root: Canvas (overlay, sort order 100), CanvasScaler at Synty's 3840x2160 design resolution
    matching height, plus three project components. The Synty
    `Screen_FantasyMenus_Loading_01` sits underneath as a *nested prefab instance*, so pack
    updates flow through; the only override on it is a `TextWrapper` added to
    `Label_LoadingPercentage`. It starts inactive and is the loaders' `rootObject`.
  - `Assets/Scripts/UI/Loading/UILoadingScreenView.cs` owns the Synty-specific bits: the
    screen Animator's `Active` bool (true = In state, false = Out, 0.5 s) and a random tip into
    `Label_Tooltip` on every show, never the same tip twice in a row. Five starter tips on the
    component; edit them on the prefab.
  - `UINetworkSceneLoading_G : UINetworkSceneLoading` handles map loads. The `NetworkManager`'s
    `LiteNetLibAssets.onLoadScene*` and `LanRpgNetworkManager.onSpawnEntities*` events were
    re-pointed from the old canvas to it (six persistent calls, method names unchanged, assembly
    type names filled in). `finishedDelay` 0.6 s so the Out animation finishes before the root
    is hidden.
  - `UISceneLoading_G : UISceneLoading` handles `GameInstance.LoadHomeScene()`. **Kit bug worked
    around:** `UISceneLoading.Singleton` has a private setter that nothing in the kit assigns, so
    `LoadHomeScene()` always took its "no loading UI" branch and the home scene loaded bare.
    The subclass sets the property through reflection in `Awake`; harmless once upstream fixes
    it. The Out animation is triggered from `SceneManager.sceneLoaded`, which fires inside the
    base method's `finishedDelay` wait.
  - Progress text and slider are bound on both loaders (`Label_LoadingPercentage`,
    `Slider_Horizontal`, non-interactable). The status label is deliberately not bound: the kit
    would overwrite the "LOADING GAME" header with "Scene Loading...". The background still shows
    Synty's sample main-menu screenshot (`SPR_Screenshot`) - swap it for a shot of the world.

- **Gameplay UI hotkeys rebound** (2026-09-02) — `UISceneGameplay.toggleUis` on
  `CanvasGameplay_G.prefab`: Hero (`UICharacterDialog`) C -> N, Inventory (`UIItemsDialog`)
  I -> B, Quests (`UIQuestDialog`) Q -> L, Party (`UIPartyDialog`) P -> O, Skills (`UISkillsDialog`) T -> P, Friends (`UIFriendDialog`) L -> J.
  Guild (G) unchanged. Opening Friends still logs "request type 137/178 not registered" and a
  "service not available" dialog in LAN mode, since `LanRpgNetworkManager` registers no friend
  request handlers - the binding is kept for when one exists.

- **Weapon grip placement from play-mode tuning** (2026-09-02) — values read off the instantiated
  `(Clone)` objects in the Inspector and written to the items' `EquipmentModel`:
  sword `localPosition (0.116, 0.03, -0.01)`, `localEulerAngles (-4.329, 269.014, -254.013)`;
  shield `localPosition (0, 0, 0)`, `localEulerAngles (-92.142, -3.617981, -88.71701)`. The sword
  values went onto every right-hand Fantasy Hero weapon - `OneHandSword001_G`, `SyntySword001_G`,
  `TwoHandSword001_G`, `Staff001_G` - since the pack shares one grip pivot; the greatsword and
  staff may still want a nudge. `DarkFortressSword001_G` keeps its own offsets.
  `BaseCharacterModel` applies these as local position/euler on instantiate, so the numbers are
  exactly what the Inspector shows on the clone.

- **Attack while moving, turning locked during the swing** (2026-09-02) — on all five project
  weapons: `moveSpeedRateWhileAttacking` 0 -> 1 (0 froze the character for the whole animation)
  and `movementRestrictionWhileAttacking.turnRestricted` on. The lock holds on both rotation
  paths of `CharacterControllerEntityMovement`: `TopDownAimController` feeds aim through
  `SetLookRotation`, which returns early while `CanTurn()` is false, and the movement-direction
  turn in `BuiltInEntityMovementFunctions3D` is gated the same way. The kit has no "slow turn
  while attacking" - it is a hard lock or nothing - so lock it is. The rate is per item; drop it
  below 1 for a heavier feel on the greatsword.

- **Warrior starter set** (2026-09-02) — seven new items under `Assets/1. Data/GameData/Items/`,
  all registered in `GameDatabase_G` (25 items now) and wired into `Warrior_G`: right hand
  `OneHandSword001_G`, left hand `Shield001_G`, armor `Chest001_G` / `Legs001_G` / `Boots001_G` /
  `Gloves001_G`, and `TwoHandSword001_G` + `Staff001_G` in the start inventory.
  - Armor pieces are copies of `Legs001_G` with the socket and armor type swapped
    (`Body`, `Gloves`, `Boots`) and instantiated-object index 1 on every piece, so the four show
    the same Synty outfit variant as the existing legs. `Legs001_G`'s title changed from
    "Legs001_G" to "Legs 01" and its "Test legs" description replaced, to match the set.
  - Weapons are copies of `SyntySword001_G` pointed at `PolygonFantasyHeroCharacters` prefabs:
    `SM_Wep_Sword_01` (OneHandSword, 8-12), `SM_Wep_Sword_Large_01` (TwoHandSword, 14-20,
    weight 4), `SM_Wep_Staff_01` (OneHandStaff, 6-10). Grip offsets zero, scale zero (the kit reads
    zero as "prefab scale"). `OneHandSword001_G` uses the same mesh as `SyntySword001_G`, which is
    now redundant and can go once nothing else needs it.
  - `Shield001_G` is a copy of the demo `Shield001` (`ShieldItem`) pointed at `SM_Wep_Shield_01`
    on the `Shield` socket, demo drop model and equipment set cleared; it keeps the demo refine
    table the way `Legs001_G` does.
  - No icons on the new items: neither art pack carries anything identifiable as a sword,
    shield, staff or armor piece by name, so they are left empty rather than guessed.
  - The empty `Items/Armors/Shoes/` folder was replaced by `Items/Armors/Boots/` to follow the
    armor-type rename, and `.gitkeep` removed from Body, Gloves and Shields.
  - `SerializedProperty.intValue` on the float `weight` field logs "type is not a supported int
    value" and skips the write instead of throwing, so an editor script that sets it must use
    `floatValue` - found out the hard way on the greatsword.

- **Armor type `Shoes` renamed `Boots`** (2026-09-02) — now
  `Assets/1. Data/GameData/ArmorTypes/Boots.asset`, GUID kept, `defaultTitle` and `equipPosition`
  set to `Boots`, so the demo `Shoes001/002` items and the `UIEquipItems.otherEquipSlots` binding
  in `UIItemsDialog.prefab` still resolve it. The two "Shoes" labels and the `EquipSlotShoes`
  object in that prefab were renamed to Boots to match.

- **Synty leg slots split: `Legs` = hips mesh, `Boots` = leg meshes** (2026-09-02) — Synty's
  `Male_10_Hips` mesh covers pelvis and thighs and the `Male_11/12_Leg_*` meshes cover shin and
  foot, so a `Legs` socket spanning all three showed boots and shins whenever pants were equipped.
  `SyntyEquipmentContainerBuilder`'s slot map now wires `Legs` to the hips part alone and a new
  `Boots` socket to the two leg parts, and the builder was re-run on `SyntyPlayerCharacter`
  through its `Build()` entry point (18 sockets rebuilt, `WeaponR`/`WeaponL`/`Shield` untouched,
  no other container changed in the diff). `Legs` became a single-object container (indices
  0-28, default `Chr_Hips_Male_00`), `Boots` an object-group container (indices 0-19, default 0).
  `Legs001_G` keeps index 1, which now selects `Chr_Hips_Male_01` only. Boot items equip with
  socket `Boots` and the leg-mesh number as index.
  - Stock Synty quirk worth knowing: the `Chr_LegLeft_*` meshes sit under `Male_11_Leg_Right` and
    `Chr_LegRight_*` under `Male_12_Leg_Left`. Harmless here, since each Boots group holds both.

- **15 cloak items** (2026-09-02) — `Assets/1. Data/GameData/Items/Armors/Cloak/Cloak001_G` to
  `Cloak015_G`, copied from `Legs001_G`: armor type `Cloak`, socket `Cloak`, instantiated-object
  index = the `Chr_BackAttachment_NN` mesh number, titles "Cloak 01" to "Cloak 15", description
  cleared, no icon yet; sell price, weight and refine table inherited from the template. All
  registered in `GameDatabase_G`, which now lists 18 items. The Cloak folder's `.gitkeep` removed.

- **`GameDatabase_G` emptied of prototype content** (2026-09-02) — the explicit-list database now
  holds project data only: `SyntyPlayerCharacter` as the single player entity, the new `Warrior_G`
  class, the three project items (`SyntySword001_G`, `DarkFortressSword001_G`, `Legs001_G`),
  `Prototype_World_01` as the only map, and the type tables those depend on: attributes
  Str/Dex/Int/Vit, currency Fame, damage elements Fire/Ice/Lightning/Poison, armor types
  Body/Cloak/Gloves/Head/Legs/Ring/Shoes, weapon types OneHandSword/TwoHandSword/OneHandStaff/Bow,
  ammo type Arrow (Bow's required ammo). Everything else — 57 demo items, four demo classes and
  entities, monsters, the Alpaca vehicle, skills, craft formulas, quests, factions, gachas, guild
  skills and icons, status effects, harvestables, Map001/Map002/Map_GuildWar — was removed from
  the list only; the assets stay on disk for the demo scenes.
  - Shield is not a WeaponType in the kit. `ShieldItem` is its own item class with no type asset,
    so there was nothing to keep; the `Shield` socket on the Synty model is already in place.
  - Kept type assets moved from `Demo/GameData/Resources/<Category>/` to
    `Assets/1. Data/GameData/<Category>/` with `AssetDatabase.MoveAsset`, GUIDs verified unchanged,
    so demo items, demo classes and `Demo/GameData/GameDatabase.asset` still resolve them. They no
    longer sit under a `Resources/` folder, and `TwoHandSword` — which carries the project's 2H
    attack setup — is now outside the store-only `Demo/` tree a kit reimport would overwrite.
  - `.gitkeep` removed from the six category folders that now have content.

- **`Warrior_G` player class** (2026-09-02) — `Assets/1. Data/GameData/PlayerCharacters/Warrior_G.asset`,
  copied from the demo Warrior so base stats and per-level growth carry over, then stripped: no
  skills, no start inventory, right hand `SyntySword001_G`, armor `Legs001_G`, start map
  `Prototype_World_01`. The map's start position `(0, 0.1, 0)` was checked in the scene: terrain
  height 0 there, on the NavMesh, and the scene's own `SpawnPoint` object sits at the origin. Still
  uses the demo Warrior icon sprite. Set as the only entry in `SyntyPlayerCharacter`'s
  `characterDatabases`, replacing Warrior/Mage/Archer/Novice.

- **Own `NewCharacterSetting_G`, `NpcDatabase_G` and `WarpPortalDatabase_G`** (2026-09-02) — all
  empty (0 start gold, no start items, no NPC or portal maps) in `Assets/1. Data/`, and set on the
  GameInstance in `Demo/Scenes/00Init.unity` in place of the demo ones, which gave every new
  character 2000 gold and ~30 demo items including guns, bullets and building pieces. This is an
  edit to a stock kit scene — re-apply after a Demo reimport. Still pointing at demo assets on that
  GameInstance: gameplay rule, network setting, social setting, cash shop, home scene `01Home`.
  - Existing LAN saves were created with the removed demo classes. The kit strips unknown items,
    skills and attributes on load but the class itself is gone, so create a new character instead
    of reusing them.

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

- **`Documentation/EXTENDING.md`** and a new "Adding functionality" section in `CLAUDE.md`
  (2026-09-03) — how to add features without editing vendored kit code, which is the workflow
  every other rule in `CLAUDE.md` depends on. The kit's own page on this is at
  `https://suriyun-production.github.io/mmorpg-kit-docs/#/pages/037-dev-extension`; the tables here
  were generated from this repo's source instead, which is newer than the published docs.
  - A ten-step decision procedure, cheapest mechanism first: data asset, subclass a data type,
    plain `MonoBehaviour`, `[DevExtMethods]` hook, entity event, ScriptableObject service swap,
    custom character data, controller subclass, handler interface swap, and only then patching the
    kit. The short form lives in `CLAUDE.md`, the reference and code examples in `EXTENDING.md`.
  - **The complete hook table**, 28 classes, generated by grepping every
    `InvokeInstanceDevExtMethods` and `InvokeStaticDevExtMethods` call site. Worth having written
    down because the names are strings: `BaseGameNetworkManager` alone exposes 27 of them, and
    there is no way to discover them from an IDE.
  - Two failure modes recorded because both are silent: **a misspelled hook name never runs**, with
    no compile error and no warning, and **exceptions inside a hook are caught and logged** by
    `DevExtUtils` rather than propagating, so a broken hook does not announce itself.
  - The worked example is a persistent per-character death counter, chosen because it needs a hook,
    an event, custom data and persistence at once, and needs no schema change. Also notes that the
    custom data helpers are wrapped in `#if !DISABLE_CUSTOM_CHARACTER_DATA`, so defining that
    symbol would turn every call into a silent no-op returning the default.
  - Every class, method and event named in the document was verified to exist, and the last section
    gives the grep commands to regenerate each table after a kit update rather than hand-editing.

- **`CLAUDE.md`** (2026-09-03) — an operating manual for AI agents working in this repo, loaded
  automatically at the start of every Claude Code session. Carries the hard rules (never edit
  `Core/` or `MMO/`, log every change here, new work goes in `1. Data` / `Scripts` /
  `TopDownController`), the ownership map, where new data and behaviour belong, the changelog
  convention, the list of kit files we have patched in place, both entry scenes, and the gotchas
  that have already cost time.
  - Written instead of the 42 remaining subsystem documents that were planned for
    `Documentation/Systems/`. The reasoning is worth recording, since the obvious move is to
    document everything: **2,507 of this project's 2,514 C# files are vendored kit code**, and
    `Core/` and `MMO/` are replaced wholesale by a GitHub mirror, as they were in `f2e39d8`. Prose
    describing them goes stale in one discontinuous step, silently, and an agent can read the
    source itself in seconds. What an agent cannot recover from source is why a choice was made,
    what was rejected, and which files are ours. That is what this file and this changelog hold.
  - Four documents from that pass were kept: `PROJECT_OVERVIEW.md` and, most usefully,
    `Documentation/Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md`, which is the full
    ours-versus-vendored inventory plus the re-apply checklist after a kit update. The three
    kit-subsystem documents (`01`, `03`, `04`) are explicitly marked in `CLAUDE.md` as a snapshot
    that will not be maintained, so nobody trusts them over the code later.
  - Found while writing it: **renaming a game data asset silently changes its `DataId`**.
    `BaseGameData.Id` returns the serialized `id` field or, when that is empty, the asset name
    (`BaseGameData.cs:30`), and `DataId` is a hash of that string (`:180`). Every asset in
    `1. Data` leaves `id` empty, so asset names are load-bearing and a rename would orphan any
    saved item, skill or quest referencing them. Recorded as a gotcha; not fixed.

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

- **`Documentation/Systems/01_CORE_ARCHITECTURE.md`, `03_NETWORKING_FOUNDATION.md` and
  `04_MMO_SERVER_ARCHITECTURE.md`** (2026-09-03) — 1,694 lines describing vendored kit
  subsystems, deleted the same day they were written. They were accurate, and that was the
  problem: they describe `Core/` and `MMO/`, which are replaced wholesale by a GitHub mirror, so
  they would have gone stale in one silent step while reading as current. Kept instead are
  `PROJECT_OVERVIEW.md` and `00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md`, which describe our
  decisions and the vendor boundary rather than the kit's internals.
  - Consequence handled: the two survivors carried 83 links into the documentation set that was
    planned but never built. The overview's 46-row catalogue of documents was replaced with a
    46-row **map from system to source directory**, which is more useful and degrades to a wrong
    path rather than to wrong prose. All remaining references now point at source. Verified
    mechanically: every markdown link resolves and all 59 source paths in the map exist.
  - The rule this establishes, recorded in `CLAUDE.md`: documentation holds what cannot be
    recomputed from the code. Anything an agent could derive by reading the source in a few
    minutes should be generated on demand, not stored and maintained.

- **`Assets/TopDownController/Scripts/TopDownPlayerCharacterController.cs`** and
  **`Demo/Prefabs/TopDownPlayerCharacterController.prefab`** (2026-08-28) — the addon's own
  controller forced `controllerMode = PlayerCharacterControllerMode.PointClick` every frame in
  `ManagedUpdate`, making WASD impossible. Replaced by the stock controller in WASD mode.
- **`Assets/TopDownController/Demo/Scenes/00Init_TopDownDemo.unity`** and its `.lighting` file
  (2026-08-28) — the addon's demo scene, orphaned once the point-click prefab was deleted.
  Verified absent from Build Settings and unreferenced before removal.

- **Demo shooter weapon and ammo types deleted** (2026-09-02, by hand in the editor) —
  `Demo/GameData/Resources/WeaponTypes/{Grenade,MachineGun,Pistol,Sniper}.asset` and
  `Demo/GameData/Resources/AmmoTypes/{MachineGunBullet,PistolBullet,SniperBullet}.asset`. On the
  next save the kit's validation dropped the now-null references from `Demo/GameData/GameDatabase.asset`
  and reordered two of its armor-type entries. The demo gun items (Pistol001, Sniper001,
  MachineGun001, Grenade001 and their bullets) now point at missing types; they are not in
  `GameDatabase_G`, so only the demo scenes are affected. Restorable with
  `git checkout -- <path>` since the files were tracked.

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
