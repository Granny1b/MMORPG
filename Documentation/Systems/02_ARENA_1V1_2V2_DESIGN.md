# Arena (1v1 and 2v2) — Design

**Status:** design only, nothing implemented. Written 2026-09-03.

## Purpose

This document plans ranked arena: small-team PvP (1v1 and 2v2) with rating, ladder placement and
fast queue pops, on top of the queue and instance transport already designed in
[01_BATTLEGROUND_QUEUE_DESIGN.md](01_BATTLEGROUND_QUEUE_DESIGN.md).

It is a separate document because although arena reuses the battleground's plumbing wholesale, four
problems are genuinely new and none of them are solved by that design: **friend/foe inside a team
match**, **persistent rating and leaderboards**, **queue-pop latency** (a 1v1 that lasts three
minutes cannot wait for a Unity process to boot), and **premade versus solo queueing**.

As with document 01, every claim below carries a `file:line` citation into the kit source so it can
be re-checked after a kit update.

## Scope

Inside this document:

- What arena reuses from the battleground design unchanged, and what it must add.
- How friend/foe is decided in this kit, and the extension point that makes 2v2 possible.
- Where rating lives, and what the schema does and does not support for leaderboards.
- The warm-server pool that makes fast queue pops a configuration change rather than code.
- A phased build order that assumes the battleground work landed first.

Outside this document:

- The queue, matchmaking transport and instance warp mechanics: document 01. Not repeated here.
- Rating formula constants, season length, reward tables. Content decisions, not architecture.
- The extension mechanisms themselves: `Documentation/EXTENDING.md`.

## What arena reuses unchanged

Everything in document 01's transport layer applies verbatim and should not be reimplemented:

- The cluster-side queue and its disconnect handling.
- Spawning one instance and reusing its `peerInfo` for every participant, via the partial-class
  bridge to the private `SaveAndWarpCharacterByPeerInfo`.
- Carrying per-match state across the map transfer through custom character data.
- The `BaseGameNetworkManagerComponent` pattern for map-server and match-runner logic.
- The 30-second empty-instance termination window and its consequences.

Arena is a second **bracket** on that queue, not a second queue. Build it as configuration and
subclasses over the battleground code, not as a parallel stack.

## Correction to document 01

Document 01 did not mention this, and it will stop a `BattlegroundMapInfo` from compiling.

`BaseMapInfo` declares **four `protected abstract` methods** that every subclass must implement
(`Core/Scripts/GameData/MapInfo/BaseMapInfo.cs:330-333`):

```csharp
protected abstract bool IsPlayerAlly(BasePlayerCharacterEntity playerCharacter, EntityInfo targetEntityInfo);
protected abstract bool IsMonsterAlly(BaseMonsterCharacterEntity monsterCharacter, EntityInfo targetEntityInfo);
protected abstract bool IsPlayerEnemy(BasePlayerCharacterEntity playerCharacter, EntityInfo targetEntityInfo);
protected abstract bool IsMonsterEnemy(BaseMonsterCharacterEntity monsterCharacter, EntityInfo targetEntityInfo);
```

They are not optional and they are not a detail — they are where all PvP targeting is decided. Both
`BattlegroundMapInfo` and `ArenaMapInfo` must implement all four.

## The core problem: friend and foe

In 2v2, teammates must not damage each other and opponents must. The kit resolves this **per map**,
which turns out to be exactly the seam needed.

`DamageableEntity` does not decide anything itself — it delegates to the current map
(`Core/Scripts/Gameplay/DamageableEntity.cs:443` and `:450`):

```csharp
public bool IsAlly(EntityInfo entityInfo)
{
    if (CurrentMapInfo == null)
        return false;
    return CurrentMapInfo.IsAlly(this, entityInfo);
}
```

`BaseMapInfo.IsAlly` / `IsEnemy` (`:312`, `:321`) then handle the self-check and dispatch to the four
abstracts above. So **a map type defines its own factions**, and no combat code needs touching.

The GuildWar add-on is the working precedent, keying off guild membership
(`GuildWar/Scripts/GameData/GuildWarMapInfo.cs:102`):

```csharp
protected override bool IsPlayerAlly(BasePlayerCharacterEntity playerCharacter, EntityInfo targetEntity)
{
    if (targetEntity.Type == EntityTypes.Player)
        return targetEntity.GuildId != 0 && targetEntity.GuildId == playerCharacter.GuildId;
    ...
}
```

Arena keys off the team index instead. The lookup is available because `EntityInfo` carries a live
entity reference alongside its scalar fields (`Core/.../EntityInfo.cs`), exposed through
`TryGetEntity<T>`:

```csharp
// Assets/Scripts/Arena/ArenaMapInfo.cs
private static readonly int TeamKey = "ARENA_TEAM".GenerateHashId();

protected override bool IsPlayerAlly(BasePlayerCharacterEntity playerCharacter, EntityInfo target)
{
    if (target.Type != EntityTypes.Player) return false;
    if (!target.TryGetEntity(out BasePlayerCharacterEntity other)) return false;
    int a = playerCharacter.GetPublicInt32(TeamKey);
    int b = other.GetPublicInt32(TeamKey);
    return a != 0 && a == b;                       // 0 means unassigned: ally to nobody
}

protected override bool IsPlayerEnemy(BasePlayerCharacterEntity playerCharacter, EntityInfo target)
{
    if (target.Type != EntityTypes.Player) return false;
    if (!target.TryGetEntity(out BasePlayerCharacterEntity other)) return false;
    int a = playerCharacter.GetPublicInt32(TeamKey);
    int b = other.GetPublicInt32(TeamKey);
    return a != 0 && b != 0 && a != b;
}
```

**The team value must use `Public` visibility, not `Server` or `Private`.** Ally checks run on
clients too — for nameplate colour, targeting and reticle state — and only public custom data
replicates to everyone. This is a deliberate difference from document 01, which suggested `Server`
visibility for the battleground team index; for anything the client must render differently per
team, use `Public`.

Treat `0` as "no team". A player who somehow arrives unassigned is then ally to nobody and enemy to
nobody, which fails safe rather than letting them hit their own side.

## Rating and leaderboards

### Where rating lives

Use custom character data, one key per bracket. It persists and replicates with no schema change,
exactly as team assignment does:

```csharp
private static readonly int Rating1v1Key = "ARENA_RATING_1V1".GenerateHashId();
private static readonly int Rating2v2Key = "ARENA_RATING_2V2".GenerateHashId();
```

These are backed by real tables — the MMO schema has nine of them, one per visibility and type
(`MMO/SQLs/mysql_main.sql`): `character_public_int32`, `character_private_int32`,
`character_server_int32` and the boolean/float variants. The int32 tables are shaped
`(id, characterId, hashedKey, value)`, which is precisely a key-value store per character.

Rating should be **public** (opponents and armory views can read it); match history counters that
should not be visible can be **private**.

### The leaderboard has a performance trap

A ladder query is the natural one:

```sql
SELECT characterId, value FROM character_public_int32
WHERE hashedKey = ? ORDER BY value DESC LIMIT 100;
```

That shape is supported, but **the table is not indexed for it.** The only keys declared are
(`mysql_main.sql:790-794`):

```sql
ALTER TABLE `character_public_int32`
  ADD PRIMARY KEY (`id`),
  ADD KEY `characterId` (`characterId`);
```

There is no index on `hashedKey` or `value`, so the query is a full table scan over a table holding
*every* public custom value for *every* character — not just ratings. It will be fine with a hundred
characters and will not be fine later.

Two mitigations, in order of preference:

1. **Cache the ladder on the cluster.** Compute the top N periodically (every few minutes) and serve
   reads from memory. Ladders do not need to be real-time, and this needs no schema change at all.
2. **Add a composite index** on `(hashedKey, value)` as a documented operations step. Do **not** add
   it by editing `mysql_main.sql` or `MySQLDatabase_Migrate.cs` — both are vendored and a kit update
   reverts them. The kit does have a migration mechanism (`DoMigration(id, action)` against a
   `__migrations` table, `MMO/.../MySQLDatabase_Migrate.cs`), but `DoMigration()` is a single
   `public override` that we cannot extend without editing that file, so this belongs in the server
   deployment runbook rather than in game code.

Prefer 1. Reach for 2 only when measurement says the cache refresh itself has become slow.

## Queue-pop latency, and the warm pool

Battlegrounds run long and pop rarely, so booting a map-server process per match is acceptable.
Arena is the opposite: matches are short, pops are frequent, and a player who waits 20 seconds
watching a loading screen after "match found" will feel it every single game.

**The kit already solves this, and it is configuration rather than code.** A map server can be
started in *allocate* mode: it boots, loads the scene, registers itself as available and then sits
idle, refusing to accept players or save data (`MapNetworkManager.IsAllocate`, `MapNetworkManager.cs:65`,
with early-outs at `:158` and elsewhere).

`ClusterServer.HandleRequestSpawnMap` checks that warm pool **before** asking a map-spawn server to
boot anything (`ClusterServer.cs:493-503`): if a pre-allocated peer exists for the requested map, it
is handed a `MMORequestTypes.RunMap` request, removed from the pool, and returned immediately.

Configuring the pool is a field on `MapSpawnNetworkManager` (`:56`):

```csharp
public List<SpawnAllocateMapData> spawningAllocateMaps = new List<SpawnAllocateMapData>();
// SpawnAllocateMapData { BaseMapInfo mapInfo; int allocateAmount; }
```

So "keep four warm 2v2 arenas ready at all times" is an inspector entry on the map-spawn server, not
a line of code. Size the pool to peak concurrent matches per bracket; each warm server is an idle
Unity process, so it costs memory but almost no CPU.

This also dissolves the 30-second termination race from document 01: a warm server is already
running when the match is made, so the save-warp-reconnect window starts from a much better place.

## Match flow

Arena is round-based rather than objective-based. The kit's dueling system is a useful reference
implementation for this shape, though **it should not be reused directly** — it is built around a
consensual request/accept handshake between two entities in the open world
(`Core/.../PlayerCharacterDuelingComponent.cs`), which is the wrong entry point for a match the
server starts on its own.

Worth borrowing from it:

- **Countdown then timed round.** It separates `_countDownDuration` from `_duelDuration` (`:336`)
  and treats running out the clock as an outcome, not a bug.
- **Leaver handling for free.** `OnDestroy` ends the duel against the leaver if the server is
  running and the duel had started (`:369`). Arena wants exactly this: disconnecting is a loss.
- **`EndDueling(loser)`** notifies both sides then tears down (`:360`). Mirror the shape.

Worth setting rather than borrowing:

- **`DisableDueling` must be true on arena maps.** It is virtual on `BaseMapInfo` and already
  consulted by the dueling component via `BaseGameNetworkManager.CurrentMapInfo.DisableDueling`
  (`PlayerCharacterDuelingComponent.cs:101`). Without it, players can start side-duels inside a
  ranked match.
- `AutoRespawnWhenDead` should be **false** for arena (death ends your round; you do not pop back
  up), which is the `BaseMapInfo` default (`:288`) and the opposite of the battleground setting.
- `SaveCurrentMapPosition` must be **false**, same reasoning as document 01 (`:289`).

## Components to build

Assuming the battleground work from document 01 exists, arena adds:

| # | File (under `Assets/Scripts/Arena/`) | Mechanism | Job |
|---|---|---|---|
| 1 | `ArenaMapInfo.cs` | Subclass `BaseMapInfo` | Team-based ally/enemy, round settings, team spawn points |
| 2 | `ArenaBracket.cs` | Plain data / enum | 1v1 and 2v2 bracket definitions, team size, rating key |
| 3 | `ArenaRating.cs` | Static helper | Elo-style calculation, floors, placement handling |
| 4 | `ArenaQueueExtension.cs` | Extends the cluster queue from doc 01 | Rating-banded matching, band widening over time |
| 5 | `ArenaMatchComponent.cs` | `BaseGameNetworkManagerComponent` | Rounds, countdown, win detection, rating write-back |
| 6 | `ArenaLadderService.cs` | `[DevExtMethods]` on `CentralNetworkManager` | Cached top-N ladder, periodic refresh |
| 7 | `UI/UIArenaQueue.cs`, `UI/UIArenaLadder.cs` | Prefabs under `Assets/1. Data/` | Bracket picker, rating display, ladder window |

## Matchmaking specifics

Small brackets make matchmaking harder, not easier — there are fewer candidates to pair.

- **Rating bands that widen with wait time.** Start at roughly ±100 rating and widen every few
  seconds up to a cap. Without widening, high- and low-rated players never get a match; without a
  cap, the ladder becomes meaningless.
- **Placement matches.** New characters have no rating. Seed at a fixed value and either widen their
  band aggressively or mark the first N matches as placements with larger rating swings.
- **2v2 premades versus solo queue.** A premade duo coordinating on voice beats two strangers of the
  same rating. Options: separate queues (cleanest, needs population), premade-only (simplest), or
  mixed with a rating adjustment (worst of both). **Recommendation: start premade-only for 2v2** —
  a party of exactly two queues together, which reuses the existing party system with no new
  grouping concept. Add solo queue later if population supports it.
- **Rematch avoidance.** Track the last opponent per character in memory on the cluster and prefer
  a different pairing when one exists. Purely a queue-side concern.

## Gotchas

Additional to everything in document 01, all of which still applies.

1. **Team must be `Public` custom data.** Ally checks run client-side; server or private visibility
   makes friendly fire look correct on the server and wrong on every client.
2. **All four ally/enemy abstracts must be implemented**, including the monster ones, even on a map
   with no monsters. Return `false` for both if there are none, rather than throwing.
3. **Clear team and match state when the match ends.** A stale non-zero team index follows the
   character out into the world and will make them ally or enemy to strangers on any other map that
   reads the same key.
4. **Warm-pool servers still count as processes.** `allocateAmount` of 10 across three brackets is
   30 idle Unity processes. Size against real concurrency, and remember an allocated server is
   consumed when used and must be replenished.
5. **Rating write-back is the server's job and must happen before the warp out.** Same reasoning as
   team assignment in document 01: the character is saved on the way out of the instance, so a
   rating written after the warp starts is lost.
6. **Do not index the ladder by editing kit SQL.** See the leaderboard section; it is an operations
   step or a cache, not a code change.

## Build order

Assumes document 01's Phases 0-3 landed, so a queue can already spawn an instance and pull players
into it.

**Phase A — `ArenaMapInfo` and friend/foe.** The data class with all four abstracts implemented
against a public team key, plus a 2v2 arena scene with two team spawn points. Test by warping four
players in manually and confirming teammates cannot damage each other while opponents can. **This is
the phase that proves the design** and it needs no queue, no rating and no UI.

**Phase B — Warm pool.** Add the arena maps to `spawningAllocateMaps` and confirm the cluster
consumes a pre-allocated server rather than booting one. Pure configuration; measure the pop latency
difference before and after so the value is known rather than assumed.

**Phase C — 1v1 end to end.** The simpler bracket first: queue two players, run rounds, decide a
winner, write rating back, warp out. 1v1 has no friendly fire to get wrong, so it isolates the match
lifecycle from the team logic proven in Phase A.

**Phase D — 2v2.** Premade-only to start. Team assignment from party membership, which removes the
team-balancing question entirely for the first iteration.

**Phase E — Ladder and UI.** Cached top-N service, ladder window, rating display, bracket picker,
placement handling.

## Open decisions

- **Rating formula.** Elo is the obvious default; K-factor and starting rating are tuning.
- **Season length and reset policy.** Full reset, soft squash toward the mean, or none.
- **Rewards.** Per-win currency, end-of-season rewards by bracket, or cosmetic titles.
- **Solo queue for 2v2.** Deferred by the recommendation above; revisit when population is known.
- **Spectating.** Not designed here. It would need a non-participant connection path into the
  instance and is a much larger piece of work.
- **Whether battlegrounds and arena share one rating.** They should not, but the queue code is
  shared, so keep the rating keys per bracket from the start rather than retrofitting.

## Related

- [01_BATTLEGROUND_QUEUE_DESIGN.md](01_BATTLEGROUND_QUEUE_DESIGN.md) — the queue and instance
  transport this design builds on. Read first.
- [00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md](00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md)
  — ownership rules and the post-kit-update checklist.
- `Documentation/EXTENDING.md` — the mechanisms used here.
- `GuildWar/Scripts/GameData/GuildWarMapInfo.cs` — the in-repo precedent for a map type that defines
  its own factions.
