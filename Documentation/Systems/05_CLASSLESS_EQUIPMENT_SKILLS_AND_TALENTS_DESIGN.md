# Classless Characters — Equipment Grants Spells, Talents Shape Them

**Status:** design only, nothing implemented. Written 2026-09-04.

## Purpose

The question this document answers: if the game has no classes, and **equipment decides which
spells a character can cast**, does the MMORPG KIT support that — and where do talent trees fit
once it does?

The short answer is that **the kit already implements the classless half.** Not "can be made to" —
the field exists on every equipment item, the aggregation path exists, the server-side validation
already reads the aggregated result, and the hotkey UI carries a kit-authored comment saying
`// Get all skills included equipment skills`. This is a supported feature that nothing in the demo
content exercises. Building it is data authoring, not programming.

The talent half is not free, and most of this document is about that. The important finding is
negative and would cost a day to rediscover: **a passive skill's `increaseSkills` is silently
dropped** by the stat aggregator, so the obvious design — a talent node that adds +2 ranks to the
Fireball your staff gave you — does not work, and fails quietly rather than loudly.

Every claim carries a `file:line` citation against the source in this repo, per the rule in
`CLAUDE.md` that the source wins over the kit's online docs.

## Scope

In scope: how a skill reaches a character, which sources of skills work, what the two independent
gates on casting are, what breaks when learnt and granted skills coexist, and three viable talent
architectures with their costs.

Out of scope: the tree layout UI's visual design, balance numbers, and the question of whether to
keep character levels at all.

---

## Part 1 — The classless mechanism already exists

### The field

`BaseEquipmentItem` — the base of every weapon, armor, shield and accessory — carries:

```csharp
[SerializeField]
private SkillIncremental[] increaseSkills = new SkillIncremental[0];
```

`Assets/UnityMultiplayerARPG/Core/Scripts/GameData/Item/Implements/BaseEquipmentItem.cs:138-145`

`SkillIncremental` is `{ BaseSkill skill; IncrementalInt level; }`
(`Core/Scripts/GameData/Skill/SkillLevel.cs:37-41`). The `IncrementalInt` means the granted rank
**scales with the item's level**, so a level 1 staff grants Fireball rank 1 and a level 40 staff
grants Fireball rank 5, from one asset and one curve.

There is a second, more interesting source on the same class: `ItemRandomBonus.randomSkillLevels`
is a `SkillRandomLevel[]` with `minLevel`, `maxLevel` and `applyRate`
(`Core/Scripts/GameData/Item/ItemRandomBonus.cs:19`,
`Core/Scripts/GameData/Skill/SkillLevel.cs:12-33`). That is a Diablo-style rolled affix — a dropped
staff can roll a spell. It is seeded and deterministic (`CalculatedItemBuff.Build` takes
`randomSeed`, `Core/Scripts/MemoryManagement/Caching/CalculatedItemBuff.cs:73`), so the client and
server agree without replicating the roll.

### The aggregation path, end to end

1. `CalculatedItemBuff.Build` resolves the item's granted skills at the item's own level and merges
   the rolled bonus into the same dictionary
   (`CalculatedItemBuff.cs:107-108`).
2. `CharacterDataExtensions.GetBuffs(this CharacterItem ...)` pushes that dictionary into the
   `onIncreasingSkills` callback (`Core/Scripts/CharacterData/CharacterDataExtensions_Stats.cs:161`),
   and does the same for every socket enhancer slotted into the item (`:166-181`).
3. `GetAllStats` seeds `resultSkills` from the character class and the learnt list
   (`CharacterDataExtensions_Stats.cs:699-700`, via `GetCharacterSkills` at `:68-87`), accumulates
   every other source into `buffSkills`, and merges the two at
   `CharacterDataExtensions_Stats.cs:989`.
4. `CharacterDataCache.Skills` holds the merged dictionary
   (`Core/Scripts/MemoryManagement/Caching/CharacterDataCache.cs:29`, set at `:237-240`).

### Every consumer already reads the merged cache, not the learnt list

This is the part that makes the design free rather than merely possible. Nothing needs patching
because nothing anywhere asks "did you learn this?":

| Consumer | Reads | Citation |
|---|---|---|
| **Server-side cast validation** | `character.GetCaches().Skills` | `CharacterDataExtensions.cs:1324` |
| `BaseSkill.IsAvailable` | `character.GetCaches().Skills` | `BaseSkill.cs:499-502` |
| Hotkey resolution | `GetCaches().Skills` | `UI/Hotkey/UICharacterHotkey.cs:157` |
| Hotkey assign window | `GetCaches().Skills` | `UI/Hotkey/UICharacterHotkeyAssigner.cs:160` |
| Skill window | `GetCaches().Skills` | `UI/Skill/UICharacterSkills.cs:156` |
| Client-side input | `CachedData.Skills` | `PlayerCharacterController_Inputs.cs:1066` |
| Passive skill combat effects | `GetCaches().Skills` | `DefaultGameplayRule.cs:749`, `:810` |
| Battle point score | `GetCaches().Skills` | `CharacterDataCache.cs:391-398` |

`UICharacterHotkey.cs:157` carries the kit author's own comment on the line above the lookup:

```csharp
// Get all skills included equipment skills
Dictionary<BaseSkill, int> skills = GameInstance.PlayingCharacter.GetCaches().Skills;
```

The feature is intended. It is simply unused by the demo content, which is why it looks like it
isn't there.

### What this buys, concretely

Equip a staff whose `increaseSkills` lists Fireball → Fireball appears in the skill window, can be
dragged to a hotkey, and the **server** accepts the cast. Unequip → it leaves the cache, the
hotkey's `GetAssignedSkill` returns false and the slot renders empty
(`UICharacterHotkey.cs:150-162`), and the server rejects the cast at
`CharacterDataExtensions.cs:1324`. No code, no new network message, no schema change.

Cache invalidation is handled: any character data mutation calls `MarkToMakeCaches`
(`Core/Scripts/MemoryManagement/Caching/CharacterDataCacheManager.cs:25-37`), so an equip change
rebuilds the skill set on the next read.

---

## Part 2 — The second gate, and why it matters more than it looks

Granting a skill and permitting its use are **separate axes**, and the kit implements both.
`BaseSkill.CanUse` checks, in order (`BaseSkill.cs:1001-1090`):

- `IsAvailable` — is it in the merged cache at level > 0 (`:1017-1022`)
- `requireShield` — a shield must be in the left hand (`:84`, checked `:1030-1038`)
- `availableWeapons` — a `WeaponType[]`; if non-empty, one hand must hold a matching type, or, bare-handed, `DefaultWeaponItem` must match (`:87`, checked `:1040-1063`)
- `availableArmors` — an `ArmorType[]`; if non-empty, a matching armor must be worn (`:90`, checked `:1065-1080`)

**All of these are player-only.** `CanUse` enters the block only for `BasePlayerCharacterEntity`
(`:1015`), so monsters bypass both the learnt check and the gear checks and can be authored freely.

Two axes is the difference between a system that works and one that plays well:

- **Grant** (`item.increaseSkills`) answers *what is in your book*.
- **Gate** (`skill.availableWeapons`) answers *what you can cast right now*.

A ring that grants Fireball plus a skill whose `availableWeapons` is `{Staff, Wand}` gives you a
character who owns Fireball permanently but must actually hold a caster weapon to use it. That is
the shape most classless games want, and it is two data fields.

It also solves the weapon-swap problem for free. A hotkey bar full of sword skills does not need
clearing when you swap to a bow — the swords' skills leave the cache, the bow's arrive, and the
same bar re-resolves.

---

## Part 3 — What breaks, and the fixes

Five real problems. The first is the only one that is a genuine trap.

### 3.1 Learnability is whitelisted by the class asset — corrected 2026-09-04

**An earlier draft of this document claimed `requirementEachLevels[].disallow` was the primary
defence against a player permanently learning their staff's spell. That was wrong, or at least
second-order.** The real gate is a whitelist, and it is stricter.

`PlayerCharacter.skills` does double duty (`GameData/Character/PlayerCharacter.cs:126-146`):

- `GetSkillLevels(level, result)` **grants** each listed skill, scaled by character level
- `GetLearnableSkillDataIds()` returns every listed skill's `DataId` — **the complete set of skills
  this character may ever learn**

and `CanLevelUp` refuses anything outside it, before any other check:

```csharp
BaseCharacter data = character.GetDatabase();
if (data == null || !data.GetLearnableSkillDataIds().Contains(DataId))
{
    gameMessage = UITextKeys.UI_ERROR_INVALID_CHARACTER_DATA;
    return false;
}
```

`Core/Scripts/GameData/Skill/BaseSkill.cs:921-925`

Both `AddSkill` on the server (`PlayerCharacterDataExtensions.cs:421`, `:436`) and the level-up
button in the UI (`UICharacterSkill.cs:273`) route through `CanLevelUp`, so **a spell that is not
listed on the class asset simply cannot be learnt.** For a classless game with a single "class"
asset, that array is the master list of everything purchasable with skill points, and grantable
spells are kept off it.

`disallow` remains useful, but for a different job: locking an individual *rank* of a skill that is
otherwise learnable (`BaseSkill.cs:424-430`, checked at `:928-932`) — a capstone that needs a quest,
say. It is not the grant/learn boundary.

Two consequences of the whitelist worth knowing before authoring:

- **New characters are seeded with a rank-0 row for every learnable skill.** Character creation
  loops the whitelist and adds `CharacterSkill.Create(skillDataId, 0)`
  (`PlayerCharacterDataExtensions.cs:143-147`). Talent nodes therefore already exist on the
  character at rank 0 — nothing needs seeding, and unlearnt nodes are visible to a tree UI.
- **Removing a skill from the class asset refunds it.** `ValidateCharacterData` strips any character
  skill outside the whitelist and returns its levels as skill points
  (`PlayerCharacterDataExtensions.cs:50-70`). That is a usable migration path for retiring a talent,
  and an accidental one if the array is edited carelessly.

### 3.2 A passive talent's `increaseSkills` is silently discarded

**This is the finding that shapes Part 4, and it is worth reading twice.**

In `GetAllStats`, the merge is one-way and happens once:

```csharp
// Sum skills from base and buffs
GameDataHelpers.CombineSkills(resultSkills, buffSkills);      // :989

if (sumWithSkills)
{
    foreach (var skillEntry in resultSkills)                   // :993
    {
        GetBuffs(skillEntry.Key, skillEntry.Value,
            ...
            skills => GameDataHelpers.CombineSkills(buffSkills, skills),   // :1006
            ...);
    }
}
...
if (onGetSkills != null)
    onGetSkills.Invoke(resultSkills);                          // :1042-1043
```

`Core/Scripts/CharacterData/CharacterDataExtensions_Stats.cs:988-1043`

Passive skills are walked **after** the merge, and their `increaseSkills` is written into
`buffSkills`, which is never combined into `resultSkills` again. `resultSkills` is what gets
emitted. **Line 1006 is dead code for the skill dictionary** — a passive skill whose buff carries
`increaseSkills` contributes its stats, attributes, damages and resistances correctly, and
contributes its skills to nothing at all.

There is no error, no warning, and the field is fully visible and editable in the inspector. This
is the single most expensive thing to discover by experiment in this design.

**What still works,** because everything below is aggregated *before* line 989:

- Character class skills (`PlayerCharacter.skills`, `GameData/Character/PlayerCharacter.cs:13-16`)
- Learnt skills (`characterData.Skills`)
- **Equipment items** (`increaseSkills` and rolled bonuses)
- **Equipment sets** (`EquipmentBonus.Skills`, `GameData/Item/Equipments/EquipmentSet.cs:28`)
- **Active buffs** on `characterData.Buffs` (`CharacterDataCache.cs:275`) — including a buff applied
  by casting a skill
- Summons, vehicles, titles and factions, each via their buff
  (`CharacterDataExtensions_Stats.cs:268`, `:307`, `:346`, `:385`, `:424`)

So "an active buff grants skills" works; "a passive grants skills" does not. Two adjacent fields
with the same name behave differently, which is exactly the kind of thing this repo's changelog
exists to record.

### 3.3 Battle point is inflated by gear skills

`CharacterDataCache` sums `skill.battlePointScore * level` over the merged dictionary
(`:391-396`), so gear-granted skills count toward battle point. That is arguably correct — the gear
genuinely made you stronger — but if battle point drives matchmaking or a power display, be aware
it now double-counts gear, which also contributes through stats. Set `battlePointScore = 0` on
grantable skills if that matters.

### 3.4 Passive skills from gear auto-apply their ailments

`CharacterDataCache:398` walks the merged skills, and for each passive one with a buff calls
`UpdateAppliedAilments`. `DefaultGameplayRule:749` and `:810` do the same for
`selfStatusEffectsWhenAttacking` / `WhenAttacked`. This is a feature, not a bug: **a passive skill
granted by an item is a working way to attach combat behaviour to gear.** Recorded so it isn't
mistaken for a leak.

### 3.5 Renaming a grantable skill asset orphans every item that grants it

Already covered in `CLAUDE.md`, restated because this design multiplies the blast radius:
`DataId` hashes the asset name when the `id` field is empty (`BaseGameData.cs:30`, `:180`), and
every project asset currently leaves `id` empty. With spells referenced from dozens of item assets,
**set `id` explicitly on every skill before authoring the item side.** Do this first; it is
five minutes now and a data migration later.

---

## Part 4 — Rolled ranks on gear, and a baseline every character owns

Two questions that come up immediately once Part 1 is understood: can a helm roll a defensive skill
at a random rank, and can a character start with passives that levelling then deepens? Both yes, and
both have sharp edges.

### 4.1 A rolled rank is a real affix system

`ItemRandomBonus.randomSkillLevels` is `SkillRandomLevel[]`, and each entry is
`{ BaseSkill skill; int minLevel; int maxLevel; float applyRate; }`
(`GameData/Item/ItemRandomBonus.cs:19`, `GameData/Skill/SkillLevel.cs:12-33`). So a helm listing
`{ Guard, 1, 5, 0.25 }` has a 25% chance to carry Guard at a rank rolled 1-5. It works identically
for active and passive skills — nothing in the roll path inspects `SkillType`.

**The roll is per item instance and stable.** `CharacterItem.randomSeed` is assigned once, at
creation (`CharacterItem.cs:296`), persisted on the instance
(`SharedData/.../CharacterItem.cs:37`), and `CalculatedItemRandomBonus.Build` derives everything
from `new System.Random(_randomSeed)` (`CalculatedItemRandomBonus.cs:91`). Two helms of the same
type roll differently; one helm rolls the same forever; and because both sides compute from the
persisted seed, **the roll never has to be replicated**.

Four things to get right:

- **`maxRandomStatsAmount` is a budget shared across every affix category**, not per category
  (`CalculatedItemRandomBonus.cs:113-116`). If the helm also rolls random armor and attributes, the
  skill competes with them for slots. `0` means unlimited.
- **Category order is shuffled** (`s_randomActions.Shuffle(random)`, `:107`), and within a category
  the entry order is shuffled too, but only when the item's `version > 1`
  (`PrepareRandomingIndexes`, `:120-128`, gated at `:282-288`). `CURRENT_VERSION` is `2`
  (`CharacterItem.cs:9`), so newly created items are fine; items created before a version bump keep
  their stored version and roll in declaration order. Do not author affix lists that rely on order.
- **A rolled rank is additive with the item's flat `increaseSkills`**, since both land in the same
  dictionary (`CalculatedItemBuff.cs:107-108`). A helm granting Guard 1 flat *and* rolling Guard 1-5
  gives 2-6, not 1-5.
- **`applyRate` is rolled per entry**, so listing ten possible skills on one helm gives a binomial
  spread, not "pick one". Use `maxRandomStatsAmount = 1` if exactly one affix should land.

### 4.2 A baseline every character owns, deepened by levelling

The mechanism is the same whitelist from 3.1, used in both of its roles at once: list the baseline
passive on the class asset with a flat `skillLevel` of 1. `GetSkillLevels` grants rank 1 to every
character, `GetLearnableSkillDataIds` makes it purchasable, and the two stack additively — class
grants and learnt levels are summed by `GetCharacterSkills`
(`CharacterDataExtensions_Stats.cs:76-86`). One skill point buys rank 2, and so on.

This is the right shape for a classless game. **Gear decides breadth — which spells. The baseline
decides depth — how strong the character is underneath whatever they are wearing.** It is the only
progression a player cannot lose by changing kit, so it is where character identity lives, which
argues for broad passives (vitality, mana regeneration, crit) over element- or role-specific ones.
Element-specific baselines re-create classes at character creation, which is the thing being
avoided.

Three gotchas, all verified, all in exactly this configuration:

- **`maxLevel` defaults to `1`, and the class grant counts against it.** `BaseSkill.maxLevel = 1`
  (`:18`), and the cap test is `level + tempData[this] >= maxLevel` where `level` is the *learnt*
  level and `tempData[this]` is the class grant sampled at character level 1 (`:941-951`). A
  baseline of 1 with the default `maxLevel` of 1 is immediately capped — the player can never buy a
  rank, with `UI_ERROR_SKILL_REACHED_MAX_LEVEL` as the only clue. Set `maxLevel = 5` for one free
  rank plus four purchasable.
- **The cap always samples the class grant at character level 1**, hardcoded — `GetSkillLevels(1,
  tempData)` (`:944`), with the kit's own comment saying as much. So if the baseline itself scales
  with character level, the cap ignores the scaling: a passive growing 1→5 across levels with
  `maxLevel = 5` still permits four purchased ranks on top, reaching 9. Either keep baselines flat
  and buy the growth, or keep them scaling and set `maxLevel` knowing what it actually measures.
  Flat plus purchased is the one that matches "levelling is a choice".
- **The UI and the server disagree about which requirement rank is being bought.** This is the one
  that will waste an afternoon. `UICharacterSkills.GenerateList` builds each entry with the *learnt*
  level in the `CharacterSkill` but the *merged* level as `targetLevel`
  (`UICharacterSkills.cs:224-226`), `UICharacterSkill.Level` returns `targetLevel` (`:13`), and that
  is what it passes to `CanLevelUp` (`:273`). The server passes the learnt level instead
  (`PlayerCharacterDataExtensions.cs:421`, `:436`). With a baseline grant of 1 and nothing learnt,
  the UI reads `requirementEachLevels[1]` while the server charges `[0]` — the displayed cost and
  the charged cost are different rows. It never surfaces in stock content, where learnt and merged
  levels are equal because nothing grants skills.

  **Workaround, in order of preference.** Make every `requirementEachLevels` entry identical, so a
  flat per-rank cost makes the index mismatch unobservable — this is free and is what a baseline
  passive wants anyway. If costs must escalate, fork `UICharacterSkill` into `Assets/1. Data/` and
  pass the learnt level to `CanLevelUp`. **Rejected: patching `UICharacterSkill.cs` in place** — it
  is under `Core/` and a kit update would revert it, and the merged `targetLevel` is deliberate for
  *display* ("your effective rank"), so the fix is to stop reusing it for a requirement index, not
  to change what it means.

### 4.3 Talent nodes sit at rank 0 in the ordinary skill window

Because character creation seeds a rank-0 row for every learnable skill (3.1) and
`CombineSkills` only skips null keys, never zero values
(`GameDataHelpers_CombineKeyValuePair.cs:203-217`), an unlearnt talent is present in the merged
cache at 0. `IsAvailable` requires `> 0` (`BaseSkill.cs:501`) so it is not castable, but
`UICharacterSkills` will list it. That is exactly right for a tree UI and clutter in the normal
skill window; `UICharacterSkillsUtils.GetFilteredList` with `filterCategories` / `filterSkillTypes`
(`UICharacterSkills.cs:200`) is the seam for separating the two.

## Part 5 — Talent trees

### The design tension

If talents *grant* spells, talents are classes with extra steps — the player picks the fire tree,
gets fire spells, and the gear stops mattering. **Talents must shape what the gear grants, not
compete with it.** Everything below follows from that.

The good news is that `SkillRequirement` is already a talent-tree node definition
(`Core/Scripts/GameData/Skill/SkillRequirement.cs`):

```csharp
public bool disallow;                       // hard-lock this rank
public IncrementalInt characterLevel;       // level gate
public IncrementalFloat skillPoint;         // cost, per rank
public IncrementalInt gold;
public AttributeAmount[] attributeAmounts;  // "requires 30 Int"
public SkillLevel[] skillLevels;            // <-- the tree's edges
public CurrencyAmount[] currencyAmounts;
public ItemAmount[] itemAmounts;
```

It is `requirementEachLevels`, a **list — one entry per rank** (`BaseSkill.cs:424-497`). A five-rank
talent with escalating cost and a prerequisite that tightens at rank 3 is pure data. `skillLevels`
is the parent-edge list, so the DAG is expressed on the child node, and `UISkillRequirement` already
renders it (`Core/Scripts/UI/Skill/UISkillRequirement.cs:170-185`).

Respec exists too: `ResetSkills` and `ResetAttributes` refund the points
(`PlayerCharacterDataExtensions.cs:382-407`, `:453-480`), reachable from an item
(`GameData/Item/Item.cs:1234`, `:1257`) or a quest (`GameData/Quest/Quest.cs:24-25`).

What the kit does **not** have is any tree *layout*. There is no node graph for skills — xNode is
vendored but wired only to `NpcDialogGraph` (`Core/Scripts/GameData/Npc/NpcDialogGraph.cs`). The
prerequisite data exists; the picture of it does not. That is the one thing to build.

### Three architectures

**A — Passive talent nodes (zero code, ships first).**

A talent is a passive `Skill` asset with a `Buff`. Learn it with skill points; it lands in the
merged cache; the cache applies its ailments and `GetAllStats` folds its buff into stats,
attributes, damages and resistances. Gate the tree with `requirementEachLevels[].skillLevels`.

Reaches: `+X% crit`, `+N max mana`, `attacks apply Bleed`, `+15% fire damage` (via
`increaseDamages` on the buff, or via `Attribute.IncreaseDamages` if the talent raises an
attribute).

Cannot reach: anything targeting **one specific skill**. And it cannot grant or rank up a skill —
see 3.2.

**B — Tiered skill assets (zero code, coarse).**

"Fireball" and "Greater Fireball" are separate assets; the greater one's
`requirementEachLevels[].skillLevels` requires the lesser. The staff grants the lesser; the talent
tree unlocks the greater. Works, but each tier is a full asset with its own effects and animation
wiring, and the player ends with two hotbar entries for one concept. Use sparingly, for genuine
capstones.

**C — A `BaseGameplayRule` subclass (the real answer for skill-specific scaling).**

`CLAUDE.md` extension route 6. The seam is:

```csharp
public abstract int GetTotalDamage(Vector3 fromPosition, EntityInfo instigator,
    DamageableEntity damageReceiver, float totalDamage,
    CharacterItem weapon, BaseSkill skill, int skillLevel);
```

`Core/Scripts/Gameplay/Rule/BaseGameplayRule.cs:83`

It receives **the skill being cast** and the instigator. A subclass can read
`attacker.GetCaches().Skills`, find the talent nodes, and multiply damage for the matching skill —
exactly the pattern `DefaultGameplayRule` already uses when it walks that dictionary for passive
combat effects (`:749`, `:810`). Subclass `BaseGameplayRule`, make an asset, assign it on
`GameInstance`; do not edit `DefaultGameplayRule`.

The talent→skill mapping wants somewhere to live. A small `ScriptableObject` under
`Assets/1. Data/GameData/Talents/` holding `{ BaseSkill talentNode, BaseSkill[] affectedSkills,
float perRankMultiplier }` keeps it authorable and out of code. **Do not register it in
`GameDatabase_G`** — that asset holds a fixed set of typed arrays (`GameDatabase.cs:25-63`) with no
generic slot, and adding one needs a `partial` field plus a `LoadDataImplement` hook. Reference the
list directly from the gameplay-rule asset, which is the same conclusion doc 04 reached for camera
shake profiles.

**Recommended: A for breadth, C for depth, B only for capstones.** A is data and ships immediately;
C is roughly one script and one asset type and is what makes a tree feel like it is about *your*
spells.

### The tree UI

`UICharacterSkills` has an overload that takes a curated dictionary rather than reading the cache:

```csharp
public void UpdateData(ICharacterData character, IDictionary<BaseSkill, int> skills)
```

`Core/Scripts/UI/Skill/UICharacterSkills.cs:161-171`

That is the hook — feed it the talent subset and reuse the existing entry prefab, requirement
rendering and level-up plumbing. Fork the dialog into
`Assets/1. Data/Prefabs/UI Prefabs/UIDialogs_G.prefab` per `CLAUDE.md`; `UIEscapeWindowsHandler`
collects it at `Awake` so Escape handling needs no wiring.

Node positions and edges are presentation. Author them on the UI prefab, or on the talent
`ScriptableObject` from architecture C, rather than on the `Skill` assets — a skill should not carry
a screen coordinate.

---

## Part 6 — Default keybindings that follow your gear

The want: a key that means "my legs skill". Swap legs, and the new legs' skill is on that key with
no dragging. This is buildable, the kit ships a working precedent for it, and there is one
architectural fact that decides the whole shape.

### 6.1 The kit already does this, for items

`UICharacterHotkey` has an `autoAssignItem` flag (`UI/Hotkey/UICharacterHotkey.cs:27`) and this
handler (`:72-86`):

```csharp
private void OnNonEquipItemsOperation(LiteNetLibSyncListOp operation, int index, ...)
{
    if (!autoAssignItem)
        return;
    if (!GetAssignedSkill(out _, out _) && !GetAssignedItem(out _, out _, out _))
    {
        foreach (CharacterItem nonEquipItem in GameInstance.PlayingCharacter.NonEquipItems)
        {
            if (!CanAssignCharacterItem(nonEquipItem))
                continue;
            GameInstance.PlayingCharacterEntity.AssignItemHotkey(HotkeyId, nonEquipItem);
            break;
        }
    }
}
```

Inventory changes, the slot is empty, so it fills itself. Two things to copy from it: it reacts to a
**sync-list operation**, and it **only fills an empty slot** — it never overwrites what the player
put there.

### 6.2 Hotkeys cannot be made virtual — write the assignment, don't intercept it

The tempting design is a "virtual slot" that resolves live: *slot 5 means whatever my legs currently
grant*, with nothing stored. **That route is closed.** `GetAssignedSkill` reads the stored
`Data.relateId` (`UICharacterHotkey.cs:150-162`), it is **not `virtual`**, and it is called from
three non-virtual sites inside the same class (`:76`, `:103`, `:245`). A subclass cannot intercept
resolution, and `UICharacterHotkey` is under `Core/`, so overriding it means patching the kit.

So the supported design is the one `autoAssignItem` uses: **write a real `CharacterHotkey` into the
character's persisted `hotkeys` list when equipment changes.** Everything downstream — rendering,
input, drag-and-drop, the cast itself — then works unchanged, because nothing knows the assignment
was automatic.

### 6.3 Run it on the server

`autoAssignItem` runs client-side and sends `CallCmdAssignHotkey`
(`BasePlayerCharacterEntity_NetworkRequest.cs:56-60`). That is safe — `CmdAssignHotkey` performs
**no validation at all**, it writes whatever it is handed
(`BasePlayerCharacterEntity_NetworkResponse.cs:57-71`) — because hotkeys are cosmetic and the cast
is validated separately at `CharacterDataExtensions.cs:1324`.

Prefer the server side anyway:

- `Hotkeys` is directly writable there — the property returns the live sync list and has a setter
  (`BasePlayerCharacterEntity_NetworkData.cs:290-298`).
- Equipment can change through paths that are not the equip UI (quest rewards, admin commands, an
  item expiring). A client-side hook misses those.
- No RPC round trip, and the write reaches the client for free: the sync-list op fires
  `onHotkeysOperation`, which `UICharacterHotkeys` already listens to (`UICharacterHotkeys.cs:178`).

Shape: a `partial class PlayerCharacterEntity` in `Assets/Scripts/Gameplay/` with a
`[DevExtMethods("Awake")]` hook subscribing to `onEquipItemsOperation`
(`BaseCharacterEntity_Events.cs:45`), guarded by `if (!IsServer) return;`, unsubscribing from the
`OnDestroy` hook. This is extension mechanisms 4 and 2 from `EXTENDING.md`, and touches no kit file.

### 6.4 The mapping asset

Two keys are needed: which equip slot, and which hotkey.

**Equip slot** is `ArmorType.EquipPosition` (`GameData/Item/Equipments/ArmorType.cs:18-21`) — a
string that falls back to the `ArmorType`'s own `Id`, uppercased. `CharacterItem.equipSlotIndex`
(`SharedData/.../CharacterItem.cs:32`) disambiguates multi-slot types, so two ring slots can carry
different defaults.

**Hotkey id** is the `hotkeyId` string that `UICharacterHotkeyPair` binds to a UI element
(`UI/Hotkey/UICharacterHotkeyPair.cs`).

So a small `ScriptableObject` under `Assets/1. Data/GameData/` holding
`{ ArmorType armorType, byte equipSlotIndex, string hotkeyId }` rows is the whole configuration.
**Do not register it in `GameDatabase_G`** — same reason as elsewhere in this document and doc 04:
`GameDatabase` has a fixed set of typed arrays (`GameDatabase.cs:25-63`) and no generic slot.
Reference it from the component that reads it.

If a single item ever needs to override the slot default, `BaseItem` and its subclasses are
`partial` and compile into `Assembly-CSharp`, so a serialized `defaultHotkeyId` can be added from
`Assets/Scripts/` without touching kit source. Treat that as the escape hatch, not the default —
one mapping asset is easier to reason about than a field on two hundred items.

### 6.5 The overwrite policy is the actual design decision

This is where it goes wrong if it is not decided deliberately.

- **Always overwrite.** Swap legs, slot 5 becomes the new legs skill, unconditionally. Simple, and
  it silently destroys any deliberate arrangement the player made.
- **Fill only when empty**, as `autoAssignItem` does. Never destroys intent, but the slot stops
  updating after the first assignment — see the trap in 6.6.
- **Replace only what this slot put there** — recommended. `onEquipItemsOperation` hands you
  `oldItem` and `newItem`. If the hotkey currently holds a skill that `oldItem` granted, replace it
  with the corresponding skill from `newItem`; if `newItem` is empty, clear it; otherwise leave it
  alone. That preserves every manual choice exactly, keeps gear slots live, and handles first equip
  and unequip as the same rule with no special cases.

Where an item grants several skills, the mapping row needs to say which one, or list several hotkey
ids in order. Do not silently take `increaseSkills[0]` — the order of that array is authoring
accident, and 4.1 shows affix order is explicitly shuffled for rolled bonuses.

### 6.6 Gotchas

- **Judge "is this slot empty" by resolution, not by record.** `IndexOfHotkey`
  (`SharedData/.../PlayerCharacterDataExtensions.cs:663`) finds a stored row; `GetAssignedSkill`
  returns false when the stored `relateId` no longer resolves against the merged cache
  (`UICharacterHotkey.cs:157-161`). A slot holding a skill you no longer have **looks empty on
  screen but is not empty in data**. Fill-only logic keyed on `IndexOfHotkey` fires exactly once and
  then never again. This is the bug this feature will produce if it produces one.
- **`relateId` is the string `Id`, not the integer `DataId`.** `AssignSkillHotkey` stores
  `characterSkill.GetSkill().Id` (`BasePlayerCharacterEntity_NetworkRequest.cs:62-66`) and
  `GetAssignedSkill` re-hashes it with `BaseGameData.MakeDataId` (`:158`). Combined with 3.5 — `Id`
  falls back to the asset name while the `id` field is empty — **renaming a skill asset silently
  breaks every persisted hotkey pointing at it**, on every character. Another reason to set `id`
  explicitly before any of this ships.
- **`hotkeys` is synced and persisted, so every write costs a DB round trip in the MMO flavour.**
  Compare before writing and skip no-op assignments. Policy 3 does this naturally; "always
  overwrite" writes on every swap, including swaps that change nothing.
- **Do not leave `autoAssignItem` enabled on a slot this system manages.** Both would assign, and
  the client-side one runs on a different event. Pick one owner per slot.
- **The keybind itself is prefab data, not game data.** `UICharacterHotkey.key` is a raw `KeyCode`
  and `buttonName` is an InputManager button, read as
  `InputManager.GetKeyDown(key) || InputManager.GetButtonDown(buttonName)` (`:129`). **Use
  `buttonName`** — it routes through `InputManager` and therefore through the Input System, so the
  key stays rebindable; a `KeyCode` does not. These live on our forked `CanvasGameplay_G.prefab`,
  and `InputManager` is one of the two kit files this project has already patched in place
  (`CLAUDE.md`), so changes near it need care.

## Part 7 — A fixed skill budget across weapon loadouts

The want: weapon slots always yield **exactly two** skills. One-hander + shield gives 1 + 1. One-hander
+ off-hand weapon gives 1 + 1. A two-handed sword or staff gives 2 on its own. The player always has
the same number of buttons whatever they are holding.

This works, and it is almost entirely data. There is one mechanical obstacle and one live kit bug.

### 7.1 What is free

`WeaponType.EquipType` is a `WeaponItemEquipType` — `MainHandOnly`, `DualWieldable`, `TwoHand`,
`OffHandOnly` (`GameData/Item/Equipments/WeaponType.cs:8-14`, field at `:28-29`) — so the loadout
shapes are already modelled, and the kit enforces them on equip
(`CharacterInventoryExtensions.cs:614-625`, `:665`).

`increaseSkills` is an array, so **a two-hander listing two skills needs no code at all.** A
one-hander lists one, a shield lists one, an off-hand weapon lists one. Different item classes
cannot collide, so 1H + shield is safe by construction.

### 7.2 The obstacle: identical dual-wielded items collapse into one skill

Both hands are aggregated through the same callback and the same additive merge:

```csharp
// Right hand equipment                                          // :776
GetBuffs(data.EquipWeapons.rightHand, ...,
    skills => GameDataHelpers.CombineSkills(buffSkills, skills), ...);
// Left hand equipment                                           // :810
GetBuffs(data.EquipWeapons.leftHand, ...,
    skills => GameDataHelpers.CombineSkills(buffSkills, skills), ...);
```

`CharacterDataExtensions_Stats.cs:764-825`

**The hand is not carried into the skill dictionary**, and `CombineSkills` sums duplicate keys
(`GameDataHelpers_CombineKeyValuePair.cs:203-217`). So two identical daggers, each granting
`Slash 1`, produce a single entry `Slash 2` — **one** hotkey at double rank, not two hotkeys. The
budget silently becomes 1 instead of 2, and the skill is silently stronger than intended.

There is no data-only way to make one item grant a different skill depending on which hand holds it:
`increaseSkills` is a property of the item, resolved by `CalculatedItemBuff` with no hand context.

**Recommended fix: make the off-hand a distinct class of weapon, using `OffHandOnly`.** Parrying
dagger, focus, tome, buckler — items that only ever go in the left hand and therefore carry their
own skill. The invariant becomes structural instead of a convention someone has to remember, and it
gives the off-hand its own identity, which is better design anyway.

`DualWieldRestriction` (`WeaponType.cs:16-21`) is the softer version — `MainHandRestricted` and
`OffHandRestricted` constrain which hand a dual-wieldable type may occupy without making it
off-hand-only.

**Rejected: allowing true mirror dual-wield and relying on players to mix weapon types.** One
duplicate pair breaks the invariant, the failure is silent, and it presents as "why do I only have
one button" — which points at the hotkey system rather than at the merge that actually caused it.

### 7.3 The live bug: a broken main hand suppresses the off-hand's skills

Inside the **left hand** block, the durability guard reads the **right** hand:

```csharp
// Left hand equipment
tempEquipmentItem = data.EquipWeapons.GetLeftHandEquipmentItem();
if (tempEquipmentItem != null)
{
    ...
    if (!data.EquipWeapons.rightHand.IsBroken())        // <-- should be leftHand
    {
        GameDataHelpers.CombineArmors(resultArmors, data.EquipWeapons.leftHand.GetArmorAmount());
        GetBuffs(data.EquipWeapons.leftHand, ...);
    }
```

`CharacterDataExtensions_Stats.cs:798-823`, against the correct `:773` in the right-hand block
above it. It is a copy-paste slip, and it inverts durability for the off-hand:

- **Main hand breaks → the off-hand's skills and buffs vanish**, though the off-hand is undamaged.
- **Off-hand breaks → its skills and buffs keep working.**

**This project already has durability in use** — `Assets/1. Data/GameData/Items/Shields/Shield001_G.asset:69`
sets `maxDurability: 100` — so this is live, not hypothetical. Under the design in this document it
presents as *"my shield skill disappeared when my sword broke"*, which points at the wrong system
entirely and is very hard to diagnose.

Three options, in order of preference:

1. **Do not put durability on weapons or shields.** Free, and sidesteps it entirely. Reasonable if
   durability is not a mechanic this game wants.
2. **Patch the line.** It is a one-word fix in `Core/`, so it is extension route 10: keep it
   minimal, record it in bold in `CHANGELOG.md` as a stock-kit edit, and add it to the divergence
   index, because a kit mirror silently reverts it.
3. **Live with it** and document the behaviour. Only defensible if items never break in practice.

### 7.4 Mapping the budget onto keys

Part 6's mapping asset keys on `ArmorType.EquipPosition`, which does not describe hands. For weapons
the rows want hand plus index instead:

- `WeaponSkill1` ← right hand, granted skill index 0
- `WeaponSkill2` ← left hand, granted skill index 0 — **or** right hand, granted skill index 1 when
  the right hand's `EquipType` is `TwoHand`

That conditional is the whole "always two buttons" rule, and it is why the mapping belongs in one
asset the auto-assign component reads rather than as a `defaultHotkeyId` field on each item: no item
can know whether it is currently supplying slot 2.

Where an item grants several skills, do not take `increaseSkills[0]` and `[1]` by position without
saying so in the data. Array order is authoring accident, and 4.1 shows the kit deliberately
shuffles affix order for rolled bonuses — a rolled skill and an authored one are indistinguishable
once merged into the cache.

**Also subscribe to `onEquipWeaponSetChange`** (`BaseCharacterEntity_Events.cs:31`), not only
`onEquipItemsOperation`. `WeaponType.EquippableSetIndexes` (`WeaponType.cs:36-40`) supports multiple
weapon sets, and swapping sets changes the whole loadout without firing an equip-items operation.

### 7.5 The invariant is a convention — enforce it in the editor

Nothing in the kit validates that a `TwoHand` weapon lists exactly two skills and a `MainHandOnly`
one lists exactly one. The failure mode is a weapon that quietly gives the player one button too
few, discovered by a player.

An editor validation pass is the cheap answer, and this repo already has the pattern:
`Assets/Scripts/Editor/`, namespace `MMORPGGranny.EditorTools`, menu under `Tools/`
(`CLAUDE.md`). Walk every weapon and shield asset and assert the expected count against
`WeaponType.EquipType`. Extend it with the checks the rest of this document earns:

- every grantable skill has a non-empty `id` (3.5, 6.6)
- no grantable skill appears in the class asset's `skills` array (3.1)
- every learnable skill has `maxLevel` set deliberately, not left at the default `1` (4.2)
- `moveSpeedRateWhileAttacking` is not `0` on any new weapon (`CLAUDE.md`)

That is one script covering every silent-failure mode identified here.

## Part 8 — Build order

1. **Set `id` explicitly on every skill asset** that gear will ever reference (3.5). First, because
   it is destructive to retrofit.
2. **Split the skill catalogue in two.** Learnable skills — baseline passives and talent nodes —
   go on the class asset's `skills` array, which is the whitelist. Grantable spells stay off it and
   are referenced only from items. Set `maxLevel` deliberately on everything learnable (4.2).
3. **One vertical slice:** one staff granting one spell, `availableWeapons = {Staff}` on the spell.
   Verify: appears on equip, hotbar-assignable, castable, gone on unequip, and **rejected by the
   server** when a client tries to cast it unequipped.
4. **Weapon-type identity pass.** Decide what each of the four existing weapon types
   (`Assets/1. Data/GameData/WeaponTypes/`) means, set `EquipType` on each, and author
   `increaseSkills` across the weapon catalogue with `IncrementalInt` curves so item level carries
   rank. Settle the skill budget here (Part 7): one skill for main-hand and off-hand types, two for
   `TwoHand`, and off-hand types marked `OffHandOnly` so a mirror dual-wield cannot collapse them.
   Decide the durability question in 7.3 at the same time.
5. **Armor and accessory grants**, then `EquipmentSet.Effects[].Skills` for set-bonus spells.
6. **Talent architecture A** — passive nodes, tree edges via `skillLevels`, wired to the existing
   `UICharacterSkills` overload. Playable trees, no new C#.
7. **Talent architecture C** — the `BaseGameplayRule` subclass and the talent-mapping asset, once
   A has shown which nodes actually want per-skill scaling.
8. **Gear-following default keybindings** (Part 6), once more than one slot grants a skill. It is
   pointless with one staff and becomes obviously necessary at four grant slots.
9. **The editor validation pass** (7.5), once the conventions above are settled — one script for
   every silent-failure mode this document identifies.
10. **`ItemRandomBonus.randomSkillLevels`** last. It is the most exciting feature here and the one
   most likely to wreck balance before the baseline exists.

`moveSpeedRateWhileAttacking` defaults to 0 and freezes movement for the length of every cast —
set it to 1 on every new skill and weapon (`CLAUDE.md`, and `SyntySword001_G` still has the
default).

## Open decisions

- **Do character levels still gate anything?** If gear grants spells and talents shape them,
  `characterLevel` on `SkillRequirement` may be the wrong currency. Skill points from levels are
  still the talent currency, so levels do not disappear — but their role narrows.
- **One shared talent pool, or one per weapon identity?** A single pool respecced at will is the
  most classless; per-weapon pools re-introduce classes through the back door but read more clearly
  to players.
- **Does unequipping a weapon clear its hotbar slots, or leave them greyed?** The kit leaves them
  greyed and re-resolves on re-equip (`UICharacterHotkey.cs:150-162`), which is free and probably
  right, but it means a bar can look half-dead after a swap.
- **Battle point handling for gear skills** (3.3) — only matters once matchmaking exists.
- **Does this game want weapon durability at all?** 7.3 makes the answer load-bearing: while
  durability is on, a stock-kit bug lets a broken main hand suppress the off-hand's granted skills.
  Dropping durability from weapons and shields avoids a kit patch entirely.

## Related

- `Documentation/EXTENDING.md` — the seven extension mechanisms, and which one each part above uses
- `Documentation/Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md` — nothing here patches the
  kit; this document adds no divergence
