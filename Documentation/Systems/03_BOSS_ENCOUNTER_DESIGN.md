# Boss Encounters and Phase Scripting — Design

**Status:** design only, nothing implemented. Written 2026-09-03.

## Purpose

This document answers a single question: **how complex can a boss be in this kit, and what does it
cost to get there?**

The short answer is that the kit gives you one crude phase primitive out of the box — a per-skill
"only use this below X% HP" gate — and nothing else. Everything a modern raid boss needs (phase
transitions, ordered rotations, hard enrage timers, threat, positional mechanics, add waves tied to
health thresholds) has to be built. The good news is that none of it requires editing kit code: the
monster AI is a plain `MonoBehaviour` on the monster prefab, and the monster data class is a
`virtual`-heavy `ScriptableObject`. Both are replaceable from `Assets/Scripts/`.

Every claim below carries a `file:line` citation against the source in this repository so it can be
re-checked after a kit update. Paths are relative to
`Assets/UnityMultiplayerARPG/Core/Scripts/` unless stated otherwise.

## Scope

Inside this document:

- Exactly what the stock monster AI does, and the ceiling that imposes.
- The one built-in phase mechanic, and the three ways it will disappoint you.
- Five extension seams, ranked by cost, that raise the ceiling.
- A recommended architecture for phased bosses, and why each piece sits where it does.
- Gotchas found in the source that will cost time if discovered late.
- A build order that puts the riskiest unknown first.

Outside this document:

- The extension mechanisms in general: `Documentation/EXTENDING.md`.
- Encounter content — which boss, which abilities, tuning numbers. Those are content decisions.
- Instanced raid maps. That transport problem is `Documentation/Systems/01_BATTLEGROUND_QUEUE_DESIGN.md`;
  a boss in an instance needs that document's machinery first.

## What the kit gives you today

### The whole monster AI is one 587-line component

`MonsterActivityComponent` (`Gameplay/CharacterSystems/MonsterCharacterSystems/MonsterActivityComponent.cs`)
is the entire brain. Its per-frame loop (`:146`) is:

1. Dead → stop, clear target, return.
2. Paused by `miniStunDuration` → stop, return (`:159-167`).
3. Summoned → attack, else find enemy, else follow summoner, else wander (`:173-183`).
4. Otherwise: leash checks (returned to spawn, in safe area, past `maxDistanceFromSpawnPoint`,
   `followTargetDuration` exceeded), then attack → find enemy → wander (`:187-215`).

`UpdateAttackEnemy` (`:270`) is the combat half: pick an action, turn to the target, and either
`Entity.UseSkill(...)` or `Entity.Attack(...)` when in range, else `SetDestination` toward it.

There is no behaviour tree, no state machine, no blackboard, no utility scoring. There is one
`bool _alreadySetActionState` and one queued skill.

### The one built-in phase primitive

`MonsterSkill` (`GameData/Skill/MonsterSkill.cs`) has four fields, two of which matter here:

```csharp
[Range(0.01f, 1f)] public float useRate;        // roll chance
[Range(0f, 1f)]    public float useWhenHpRate;  // only when HP <= this
```

`MonsterCharacter.RandomSkill` (`GameData/Character/MonsterCharacter.cs:241`) is where a phase
effectively happens:

```csharp
float random = Random.value;
foreach (MonsterSkill monsterSkill in _tempRandomSkills)
{
    if (monsterSkill.skill == null) continue;
    if (random < monsterSkill.useRate &&
        (monsterSkill.useWhenHpRate <= 0 || entity.HpRate <= monsterSkill.useWhenHpRate))
    {
        skill = monsterSkill.skill;
        level = monsterSkill.skillLevel.GetAmount(entity.Level);
        _tempRandomSkills.Shuffle();   // shuffle for next time
        return true;
    }
}
```

So **"below 50% HP, new abilities unlock" is pure data, zero code**: give the phase-two skills
`useWhenHpRate = 0.5`. That is the honest answer to the original question, and for a lot of games it
is enough.

### Everything a skill can already do

Because a monster's abilities are ordinary `BaseSkill` assets, a boss inherits the entire player
skill system for free. From `GameData/Skill/Skill.cs` and `BaseSkill.cs`:

| Capability | Where |
|---|---|
| Melee / Missile / Raycast / Throwable / `Custom` damage | `GameData/Damage/Damage.cs:9-16` |
| Ground-targeted AoE with a telegraph prefab, area duration, tick interval | `BaseAreaSkill.cs:13-52` (`areaDuration`, `applyDuration`, `TargetObjectPrefab`) |
| Debuff on hit, plus `StatusEffectApplying[]` | `Skill.cs:48-50` |
| Knockback with force, deceleration and duration | `Skill.cs:53` |
| Buff self / nearby allies / target / enemy, or a toggle | `Skill.cs:21-32`, `:61` |
| Summon adds — count, level, duration, max stack | `GameData/Skill/SkillSummon.cs`, applied at `BaseSkill.cs:796` |
| Cast time, cooldown, movement rate while casting | `BaseSkill.cs:49`, `:34`, `:25` |
| Dash-attack, warp-to-target, resurrection variants | `SimpleDashAttackSkill.cs`, `SimpleWarpToTargetSkill.cs`, `SimpleResurrectionSkill.cs` |

Cooldowns are shared with players: using a skill writes a `CharacterSkillUsage`
(`DefaultCharacterUseSkillComponent.cs:178`) and the AI refuses a skill already on cooldown
(`MonsterActivityComponent.cs:295`, `:348`).

### Buffs can rewrite the boss mid-fight

This is the most underused lever in the kit for phases. A `Buff` can:

- `isOverrideDamageInfo` / `overrideDamageInfo` (`GameData/Buff/Buff.cs:50-52`) — **replace the
  boss's basic attack outright**, applied in `MemoryManagement/Caching/CharacterDataCache.cs:457`
  and `:471`. A melee boss becomes a ranged boss by gaining one buff.
- `isOverrideSkills` / `overrideSkills` (`:54-57`) — replace the resolved skill-level table.
- `increaseStats`, `increaseStatsRate`, `increaseArmors`, `increaseResistances`,
  `increaseDamages` (`:22-45`) — the entire "hardens at 50%" family.
- `damageOverTimes` (`:82`), `disallowMove` / `disallowAttack` / `disallowUseSkill` /
  `freezeAnimation` (`:86-100`) — a scripted, immobile transition.
- `noDuration` (`:67`) — a permanent phase marker that never ticks off.

Apply one from script with `BaseCharacterEntity.ApplyBuff(dataId, BuffType, level, applier, weapon)`
(`Gameplay/CharacterEntity/BaseCharacterEntity_BuffFunctions.cs:10`; `BuffType` at
`Core/SharedData/Scripts/CharacterData/RelatesData/CharacterBuff.cs:5`). Buffs replicate to clients
already, so the phase is visible without new network code.

## The three ways `useWhenHpRate` will disappoint you

Verified against `MonsterCharacter.cs:241-274`.

1. **The gate is one-directional.** The condition is `HpRate <= useWhenHpRate`. There is no
   "only above X%". You can unlock phase-two abilities, but you cannot retire phase-one abilities.
   The boss accumulates its kit rather than changing it, so "different tactics" is really
   "additional tactics".
2. **One random roll is compared against every skill.** `float random = Random.value` is drawn once
   (`:258`) and then tested against each skill's `useRate` in shuffled order (`:264`). A skill with
   `useRate = 1.0` fires whenever it happens to sort first. Selection frequencies are therefore not
   the per-skill probabilities the inspector implies, and they interact.
3. **There is no ordering, no sequencing, no "cast this, then that".** Selection is a shuffle plus a
   single roll. A rotation, a pattern, an opener, or a mechanic that must resolve before the next
   one starts cannot be expressed.

A fourth, structural point: **`_tempRandomSkills` lives on the `ScriptableObject`**
(`MonsterCharacter.cs:177`). A `MonsterCharacter` asset is shared by every spawned instance of that
monster, so the shuffle order is global. Any per-encounter state you invent must live on the entity
or a component, never on the data asset.

## What the kit does not give you

| Missing | Consequence for a boss |
|---|---|
| **Threat / aggro table** | No threat *model*. `threat`, `aggro`, `taunt`, `enmity` and `provoke` return zero hits across the whole kit — `Core/`, `MMO/`, `GuildWar/` and the demos. Target selection is "first survivor found in an overlap query" (`MonsterActivityComponent.cs:463`, `:526`), and damage flips the target on a coin-flip (`:136`). But the *ledger* a threat table needs already exists — see the section below. |
| **Phase state machine** | No concept of a phase, transition, or encounter. |
| **Enrage / berserk timer** | No fight clock. |
| **Ability sequencing** | Random selection only, per the section above. |
| **Encounter lifecycle** | No pull, no reset, no wipe detection, no per-encounter loot lock. Leashing exists but is a distance/time leash (`:206`, `:212`), not an encounter reset. |
| **Boss UI** | No boss frame, no cast bar for enemies, no phase banner. `CanvasMonsterCharacterUI.prefab` is a nameplate. |
| **Multi-part bosses** | No linkage between entities. Two spawned monsters are unrelated. |

## Threat: not implemented, but half-built and unused

Worth its own section, because "there is no threat system" is true and still misleading.

**The model does not exist.** `threat`, `aggro`, `taunt`, `enmity`, `provoke` — zero hits across
`Core/`, `MMO/`, `GuildWar/` and every demo tree. No taunt skill type, no threat multiplier on any
skill or buff, nothing that overrides a monster's target.

**The ledger does exist, on every character entity.** `BaseCharacterEntity_DamageFunctions.cs` keeps
a per-attacker cumulative damage table:

```csharp
protected readonly Dictionary<string, ReceivedDamageRecord> _receivedDamageRecords = ...   // :11
```

- Fed automatically on every damage application — `RecordRecivingDamage(instigator, totalDamage)`
  (`:208`, implementation `:240`).
- **Summon damage is credited to the summoner** (`:246-248`), which is the behaviour you would have
  to write by hand otherwise.
- Overkill is clamped to the HP that was actually there (`:254`), so a killing blow cannot inflate a
  record.
- `ReceivedDamageRecord` carries `Instigator`, `Damage` and `UpdatedTime`
  (`Gameplay/CharacterEntity/ReceivedDamageRecord.cs:3-8`) — identity, magnitude and recency, which
  is exactly the tuple a threat table sorts on.
- Cleared on death (`:71`).

And the kit ships two sorted accessors that **nothing in the kit ever calls**:

```csharp
public void GetSortedReceivedDamageRecordsByDamage(...)   // :278
public void GetSortedReceivedDamageRecordsByTime(...)     // :288
```

The only live consumer of any of it is reward attribution when a monster dies
(`BaseMonsterCharacterEntity.cs:469`, via the unsorted `GetReceivedDamageRecords` at `:269`). So the
data is collected, the sorting is written, and the AI simply never looks at it.

**What that leaves to build**, in `FindEnemy` / `FindOneEnemyFromList` overrides at seam 4:

- Read `GetSortedReceivedDamageRecordsByDamage` instead of the overlap query, and defeat the
  coin-flip target switch at `:136`.
- Threat modifiers per skill, healing threat, and threat decay over time. `UpdatedTime` supports
  recency, but there is no decay curve.
- A reset that is not death — the ledger clears only in `Killed` (`:71`), so a boss that leashes
  home keeps every record.

**Taunt** is small once the above exists. `BaseMonsterCharacterEntity.SetAttackTarget` is public
(`:313`), validates the target, and is called from nowhere but the AI component (five sites, all in
`MonsterActivityComponent`). A custom `BaseSkill` subclass calls it and sets a lock flag your
activity component honours for N seconds. The lock is the real work: without it, the next hit
re-rolls the target.

## The five seams, ranked by cost

Ordered the way `CLAUDE.md` asks: stop at the first one that fits.

### Seam 1 — data only: `useWhenHpRate` (no code)

Skills tagged with `useWhenHpRate` on a `MonsterCharacter_G` asset. Buys you unlockable abilities at
health thresholds. Ceiling: the three limits above.

### Seam 2 — a side component on the boss prefab (small)

A plain `MonoBehaviour` in `Assets/Scripts/Gameplay/`, subscribing to entity events from a
`[DevExtMethods("Awake")]` hook or directly in its own `Awake`, watching
`DamageableEntity.onCurrentHpChange` (`Gameplay/DamageableEntity.cs:44`) and applying a phase buff
when a threshold is crossed. This is the pattern `LocomotionPhaseSync` and `ActionLayerMaskUpdater`
already use in this project.

Buys you: phase transitions with stat/damage/appearance changes, transition invulnerability
(`BaseCharacterEntity.IsInvincible`, `Gameplay/CharacterEntity/BaseCharacterEntity.cs:160`), scripted
summons, announcements. **Does not** change how the AI picks skills — you are decorating the stock
brain, not replacing it.

### Seam 3 — subclass `MonsterCharacter` (small, high leverage)

`MonsterCharacter` is `partial`, not sealed, and `RandomSkill` is `virtual`
(`MonsterCharacter.cs:23`, `:241`). Subclass it, add `[CreateAssetMenu]`, and override `RandomSkill`
with real selection logic — ordered rotations, min/max HP windows, per-phase skill lists, cooldown
awareness, target-count conditions.

Two facts make this cheap:

- The editor follows subclasses: `[CustomEditor(typeof(BaseGameData), true)]`
  (`Core/Editor/BaseGameDataEditor.cs:6`), so your extra fields get the same `Category`-tabbed
  inspector as the kit's.
- The database field is typed `MonsterCharacter[]` (`GameData/Database/GameDatabase.cs:58`), so a
  subclass drops into `GameDatabase_G` with no changes.

The entity's `CharacterDatabase` property is typed `MonsterCharacter`
(`Gameplay/CharacterEntity/MonsterCharacterEntity/BaseMonsterCharacterEntity.cs:63`), so cast to
your type inside the override — or better, pass state in through the entity, per the architecture
below.

### Seam 4 — replace `MonsterActivityComponent` (medium, the real answer)

**`MonsterActivityComponent` is referenced from exactly one place in the whole kit**: the editor
utility that builds a monster prefab (`Core/Editor/CharacterEntityCreatorEditor.cs:193`). Nothing
looks it up at runtime, nothing requires it, no interface is registered. It is an ordinary
`MonoBehaviour` sitting on the prefab.

That means a boss prefab in `Assets/1. Data/` can simply carry `BossActivityComponent` instead, and
the kit never notices. Subclass `MonsterActivityComponent` and override the `virtual`/`protected`
seams (`UpdateEnemyFindingActivity`, `RandomWanderDestination`, `FindEnemy`,
`FindOneEnemyFromList`, `ClearActionState`, `GetAttackDistance`), or subclass
`BaseMonsterActivityComponent` (`BaseMonsterActivityComponent.cs:3`) and write the loop from
scratch.

Note `UpdateAttackEnemy` is `private` (`:270`), not `protected` — to change the attack loop itself
you re-implement `ManagedUpdate` rather than override that method.

This is the seam that buys phases, rotations, positioning, telegraph timing, threat, and everything
else. It is the recommended primary mechanism.

### Seam 5 — swap `BaseEntitySetting` (project-wide)

`GameInstance.EntitySetting` is a swappable `ScriptableObject` service
(`GameInstance/GameInstance.cs:375`, `:669`, defaulted at `:1573`), and
`BaseMonsterCharacterEntity.InitialRequiredComponents` calls
`EntitySetting.InitialMonsterCharacterEntityComponents(this)` on every monster
(`BaseMonsterCharacterEntity.cs:176`). The stock implementation adds a `DashAttackHandler`
(`Gameplay/EntitySettings/DefaultEntitySetting.cs:35-38`).

Use this to attach a component to *every* monster — a damage meter, an encounter registry hook —
not for per-boss behaviour. Note this is a ninth swappable service; `Documentation/EXTENDING.md`
lists eight.

## Recommended architecture

Three pieces, each at the cheapest seam that can hold it.

```
BossEncounterDefinition_G.asset          (subclass of MonsterCharacter — seam 3)
  ├─ phases[]                            ordered, each with:
  │    ├─ enterAtHpRate                  0.5, 0.25, ...
  │    ├─ enterBuff                      Buff applied on entry (stats, damage override, DoT)
  │    ├─ transition                     invuln seconds, animation, disallowMove
  │    ├─ rotation[]                     ordered skills + weights + per-phase cooldowns
  │    └─ addWaves[]                     summon skill + count
  └─ enrage                              seconds since pull → permanent buff

BossActivityComponent  (on the boss prefab, replaces MonsterActivityComponent — seam 4)
  ├─ owns _currentPhase (server-only, per-instance)
  ├─ drives phase entry from Entity.HpRate, never from the ScriptableObject
  ├─ picks the next ability from the current phase's rotation
  └─ delegates movement/positioning per phase

BossEncounterState     (side component on the boss prefab — seam 2)
  ├─ pull detection, fight clock, enrage
  ├─ participant tracking (for loot lock and wipe detection)
  └─ replicated phase index for UI
```

Why the split: **phase state must not live on the `ScriptableObject`.** The data asset is shared by
every instance (proved by `_tempRandomSkills` at `MonsterCharacter.cs:177`), so two copies of the
same boss would share a phase. State lives on the component; the asset holds only the definition.

Why a buff carries the phase change rather than direct field writes: buffs already replicate, already
stack, already show in UI, and `isOverrideDamageInfo` can swap the boss's autoattack wholesale
(`CharacterDataCache.cs:457`). Writing stats directly would need new network plumbing.

## Gotchas verified in the source

- **The AI stops entirely when nobody is subscribed.** `ManagedUpdate` returns early on
  `Entity.Identity.CountSubscribers() == 0` (`MonsterActivityComponent.cs:148`), and
  `BaseGameEntity.IsUpdateEntityComponents` gates components the same way (`BaseGameEntity.cs:176`).
  An enrage timer or a "boss resets after 30s out of combat" rule must not be driven from the
  activity component alone, or it freezes the moment the last player's interest area moves off.
- **`findEnemyDelayMax` is dead.** `_findEnemyCountDown = Random.Range(findEnemyDelayMin, findEnemyDelayMin)`
  (`MonsterActivityComponent.cs:235`) — `Min` is passed twice. The inspector field does nothing.
  This is a kit bug; do not "fix" it in place, override the method.
- **Target switching is a coin flip on every hit.** `Random.value > 0.5f` gated by
  `switchTargetDelay` (`:136`). Without a threat model, a boss will wander between players
  unpredictably. This is the single biggest obstacle to a tank-and-spank fight.
- **`moveSpeedRateWhileAttacking` defaults to 0** on `MonsterCharacter` (`:76`), the same trap
  `CLAUDE.md` records for weapons. A boss with the default is frozen for every swing.
- **`RandomSkill` is only called once per action cycle**, guarded by `_alreadySetActionState`
  (`MonsterActivityComponent.cs:289`), and the frame it fires returns early without moving (`:303`).
  A phase change does not take effect until the current cycle clears via `ClearActionState` (`:559`).
- **Leashing is not an encounter reset.** `maxDistanceFromSpawnPoint` and `followTargetDuration`
  (`:206`, `:212`) send the boss home but do **not** restore HP, clear buffs, or reset a phase.
  A boss walked out of its arena at 10% stays at 10%.
- **Renaming a boss data asset changes its `DataId`.** Standard project rule from `CLAUDE.md`; it
  bites harder here because a phase definition referenced by id would orphan too. Set `id`
  explicitly on encounter assets from day one.
- **Everything AI is server-side** (`:148`). Correct for MMO mode, and it means phase logic needs no
  client trust — but any client-visible phase cue must ride a replicated value (a buff, or a synced
  field), not a local computation.

## Realistic complexity ceiling

With seams 1–4 and no kit edits:

| Mechanic | Verdict |
|---|---|
| Abilities unlocked below an HP threshold | **Free.** Data only. |
| Distinct phase kits — abilities that also *stop* | Needs seam 3 or 4. Straightforward. |
| Stat / damage-type / appearance change at a threshold | Needs seam 2. One buff asset. |
| Scripted transition (invulnerable, immobile, animation) | Needs seam 2. `IsInvincible` + `disallowMove` buff. |
| Add waves at thresholds | Needs seam 2 or 4 to trigger; the summon itself is a stock skill. |
| Fixed rotations / openers / "cast A then B" | Needs seam 4. |
| Enrage timer | Needs seam 2, driven off the network manager, not the AI component. |
| Ground telegraphs, avoidable AoE | Stock `BaseAreaSkill` handles the mechanic; seam 4 for the timing. |
| Threat table, taunt, tanking | Build it, but not from zero — the per-attacker damage ledger already exists and is unused. Seam 4 plus a threat component. Still the largest single item. |
| Positional mechanics (behind-only, stack, spread) | Seam 4. Distance/angle checks against participants. |
| Multi-part bosses, linked health, adds that buff the boss | Seam 2 + a shared encounter object. No kit support. |
| Boss health frame, cast bar, phase banner | New UI under `UIDialogs_G.prefab`. No kit support. |
| Instanced raid lockouts | Depends on `01_BATTLEGROUND_QUEUE_DESIGN.md`. MMO mode only. |

Nothing in this table requires editing `Core/` or `MMO/`.

## Build order

Riskiest unknown first, so a dead end is found cheaply.

1. **Prove the prefab swap.** Fork a demo monster prefab into `Assets/1. Data/`, remove
   `MonsterActivityComponent`, add an empty `BossActivityComponent : MonsterActivityComponent`,
   confirm the monster still fights. If this fails, everything above it is wrong.
2. **One threshold, one buff.** `BossEncounterState` watching `onCurrentHpChange`, applying a
   permanent buff at 50%. Confirms the buff overrides land and replicate.
3. **Phase-gated selection.** Override `RandomSkill` (seam 3) or the selection call site (seam 4) so
   phase-one abilities actually stop. Confirms the "additional tactics" limit is beaten.
4. **Transition.** Invulnerable + immobile + animation, then phase two. Confirms `IsInvincible` and
   `disallowMove` behave on a server-owned entity.
5. **Rotation.** Ordered abilities with per-phase cooldowns, replacing random selection.
6. **Threat.** Only after the above works. Start by reading the existing ledger
   (`GetSortedReceivedDamageRecordsByDamage`) in a `FindOneEnemyFromList` override — that alone gives
   a boss that stays on its biggest damage dealer. Taunt and threat modifiers come after.
7. **Encounter lifecycle** — pull, reset, wipe, enrage — driven off a
   `BaseGameNetworkManagerComponent` (`Networking/BaseGameNetworkManagerComponent.cs:10`) so it
   survives the no-subscriber freeze.
8. **UI last.**

## Open decisions

- **Threat model, or design around its absence?** Cheaper than it first looks, because the damage
  ledger is already there and already credits summons. A dodge-and-position game does not need threat and
  saves the largest item on the list. A trinity game needs it. This choice determines whether step 6
  exists at all.
- **Where does the fight clock live?** A `BaseGameNetworkManagerComponent` is safe from the
  subscriber freeze but is per-map, not per-boss. An entity-owned clock is simpler but stops when the
  arena empties — which may in fact be the wanted behaviour.
- **Does a boss reset on leash?** The kit's leash does not reset HP. Deciding "walk it out and it
  heals to full" versus "it stays damaged" changes whether step 7 needs a full reset path.
- **LAN or MMO first?** Everything here works in both. Only lockouts and instanced raids need MMO.

## Related

- `Documentation/EXTENDING.md` — the mechanisms used above.
- `Documentation/Systems/01_BATTLEGROUND_QUEUE_DESIGN.md` — instanced maps, if bosses go in instances.
- `Documentation/Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md` — what is ours versus vendored.
- `CLAUDE.md` — where new work goes, and why `Core/` must not be edited.
