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

### 3.1 Players can permanently learn an equipment skill — the important one

Granted levels and learnt levels are **different, additive sources**. `AddSkill` writes to
`characterData.Skills`, the learnt list
(`Core/Scripts/CharacterData/PlayerCharacterDataExtensions.cs:409-449`), while equipment feeds
`buffSkills`. They meet only at the merge on `CharacterDataExtensions_Stats.cs:989`.

Meanwhile `UICharacterSkill` decides whether to offer a level-up using the **merged** level:

```csharp
bool ableToLevelUp = GameInstance.PlayingCharacter != null && Skill != null
    && Skill.CanLevelUp(GameInstance.PlayingCharacter, Level, out _);
```

`Core/Scripts/UI/Skill/UICharacterSkill.cs:273`

So a staff-granted Fireball shows up in the skill window with a "+" on it. Spend a skill point,
unequip the staff, and Fireball is now yours forever. The classless premise leaks in one click.

**Fix, data-only:** set `disallow = true` on every `requirementEachLevels` entry of every
equipment-granted skill. The flag is read by `GetRequireDisallow`
(`BaseSkill.cs:424-430`) and short-circuits `CanLevelUp` (`:913-935`), which both the UI and
`AddSkill` call. Model it as a rule: **a skill asset is either grantable or learnable, never both.**

**Rejected: filtering the skill window's list instead.** `UICharacterSkills.UpdateData` populates
from the cache (`:156`), so hiding gear skills there also hides them from the player, who then
cannot see what their gear does. And it would not stop the server, since `AddSkill` validates
through `CanLevelUp` and nothing else. `disallow` fixes the UI and the server with one field.

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

## Part 4 — Talent trees

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

## Part 5 — Build order

1. **Set `id` explicitly on every skill asset** that gear will ever reference (3.5). First, because
   it is destructive to retrofit.
2. **Split the skill catalogue in two** — grantable and learnable — and set
   `requirementEachLevels[].disallow = true` on every grantable one (3.1).
3. **One vertical slice:** one staff granting one spell, `availableWeapons = {Staff}` on the spell.
   Verify: appears on equip, hotbar-assignable, castable, gone on unequip, and **rejected by the
   server** when a client tries to cast it unequipped.
4. **Weapon-type identity pass.** Decide what each of the four existing weapon types
   (`Assets/1. Data/GameData/WeaponTypes/`) means, and author `increaseSkills` across the weapon
   catalogue with `IncrementalInt` curves so item level carries rank.
5. **Armor and accessory grants**, then `EquipmentSet.Effects[].Skills` for set-bonus spells.
6. **Talent architecture A** — passive nodes, tree edges via `skillLevels`, wired to the existing
   `UICharacterSkills` overload. Playable trees, no new C#.
7. **Talent architecture C** — the `BaseGameplayRule` subclass and the talent-mapping asset, once
   A has shown which nodes actually want per-skill scaling.
8. **`ItemRandomBonus.randomSkillLevels`** last. It is the most exciting feature here and the one
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

## Related

- `Documentation/EXTENDING.md` — the seven extension mechanisms, and which one each part above uses
- `Documentation/Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md` — nothing here patches the
  kit; this document adds no divergence
