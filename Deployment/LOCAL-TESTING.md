# Proving the online layer, before the content

The goal here is narrow and worth being blunt about: **get two clients seeing
each other in `Prototype_World_01`, this week, and never let that stop
working.** Everything else — the year of content — is safe to build once that
holds.

The good news is that this is a much smaller job than it looks, because the
networking is the kit's, not yours. See §5 for why that matters.

---

## 0. Where the project actually stands

I checked, rather than assumed:

| Thing | State |
|---|---|
| `Prototype_World_01` registered as a MapInfo in `GameDatabase_G` | ✅ already done |
| `Prototype_World_01` set as `startMap` on the player characters | ✅ already done |
| MMO init scene + your map in Build Settings | ✅ already there |
| Client `MmoNetworkSetting` (`Local.asset`) pointing at `127.0.0.1:7000` | ✅ already correct |
| MMO `GameInstance` prefab pointing at `GameDatabase_G` | ❌ **was** the kit's demo DB — fixed in this commit |
| Custom gameplay code that could break under networking | ✅ none (see §5) |

`GameDatabase_G` turned out to be the demo database plus exactly two swords
(`DarkFortressSword001_G`, `SyntySword001_G`), so pointing the MMO
`GameInstance` at it was a one-line change and nothing else had to move.

**You are much closer to a working online build than "start from scratch".**

---

## 1. The one editor gotcha that will waste your afternoon

`MMOServerInstance.Awake()` branches on `Application.isEditor`
(`MMOServerInstance.cs:170`):

- **In a build:** it reads `Config/serverConfig.json` and the `-startXServer`
  command-line flags. The inspector checkboxes are ignored.
- **In the editor:** it ignores the config file and the flags completely, and
  reads only the *"Running In Editor"* checkboxes on the `MMOServerInstance`
  prefab.

So `serverConfig.json` does nothing while you're pressing Play, and the
checkboxes do nothing in a build. Knowing which half is live saves a lot of
confused staring.

Second one: `databaseOptions` on `DatabaseNetworkManager` is
**`[0] = SQLite, [1] = MySQL`** in this project. (I had that backwards in
yesterday's `serverConfig.example.json`; it's corrected now — the VPS needs
`databaseOptionIndex: 1`.) SQLite creates its own tables on first run
(`SQLiteDatabase.cs:99`), which is why it's the right choice for the first
local test and the wrong one for Linux — only Windows `sqlite3.dll` ships with
the kit.

---

## 2. Milestone 0 — MMO stack inside the editor (30 minutes)

The fastest possible loop. No build at all.

1. Open `Assets/UnityMultiplayerARPG/MMO/Demo/Scenes/00Init_MMO.unity`.
2. Select the `MMOServerInstance` object. Under **Running In Editor**:
   - `startDatabaseOnAwake` ✔
   - `startCentralOnAwake` ✔
   - `startMapOnAwake` ✔
   - `startMapSpawnOnAwake` ✘ *(leave off — the spawner launches child
     processes from a built exe, which doesn't exist yet)*
   - `startingMap` → `Prototype_World_01`
   - `databaseOptionIndex` → `0` (SQLite)
3. Press Play.

**What this proves:** the database schema creates itself, an account registers,
a character is created and persisted, your map loads under the MMO code path,
and you can walk around in it. That is the entire persistence and login layer,
working, on day one.

**What it does not prove:** that two machines can see each other. That's next.

If it fails, the failure is almost certainly in the Console within the first
10 seconds, and it's almost certainly a missing game-data reference rather
than networking.

---

## 3. Milestone 1 — two clients, one PC (the real test)

Editor stays the server; two built clients connect to it.

1. Keep the editor running Milestone 0.
2. `File → Build Settings`, Windows x86_64, `00Init_MMO` as scene index 0.
   Build to e.g. `D:\1. Unity projekt\Builds\Windows`.
3. Run the exe **twice** (`local/launch-two-clients.bat` does this and keeps
   both windowed so they fit side by side).
4. Register two different accounts, create a character on each, enter the
   world with both.

**Watch for:** does character B move smoothly on character A's screen, or does
it teleport/rubber-band? Do equipment changes replicate? Does an attack
animation play on the other screen? Does chat arrive?

This is the moment you find out whether the online layer works. Everything
after this is scale and deployment, not "does it work at all".

---

## 4. Milestone 2 — the production shape, on loopback

Now stop using the editor as a server, and run the same four processes the VPS
will run — just all on `127.0.0.1`.

```
copy Deployment\local\serverConfig.local.json  <build>\Config\serverConfig.json
Deployment\local\start-local-stack.bat
Deployment\local\launch-two-clients.bat
```

This is the first time these get exercised: `serverConfig.json` parsing, the
`-startXServer` flags, the cluster bus, and **the map spawner launching a map
server as a child process** — which is the part with the most ways to go wrong
and the part Milestone 0 skips entirely.

Once it works on loopback, switch `databaseOptionIndex` to `1` and point the
MySQL settings at a local MySQL. Now your local stack is identical to the VPS
in every respect except the machine it runs on, and a deploy is just "same
thing, different IP".

**Do this milestone before you buy anything.** If the map spawner doesn't work
locally it won't work on a VPS either, and debugging it over SSH is
considerably less pleasant.

---

## 5. Why this is less risky than it feels

The fear — "a year of work and then I can't make it networked" — is the right
fear in general. It's mostly not applicable here, for one specific reason:

**You are not writing the networking.** MMORPG KIT is server-authoritative by
construction. Movement, combat, inventory, and stats are already replicated by
the kit's own components. Your custom code is six files, of which three are
editor tools, one is UI (`UIEscapeWindowsHandler`), and two
(`LocomotionPhaseSync`, `ActionLayerMaskUpdater`) are pure animation
presentation running off the model's playable graph. None of it touches
authority, state or replication.

So the classic disaster — building a year of single-player systems on
client-side state and then discovering none of it replicates — is not the
shape of your risk. Your risk is much narrower:

1. **Content authored against a database identity that later changes.** This
   is the real one. See §6.
2. **New gameplay code that reads or writes state on the client.** Avoidable
   by habit, see §6.
3. **Deployment mechanics.** Which is what Milestones 2–3 are for, and they're
   a weekend, not a year.

---

## 6. The three habits that make "push live later" safe

Adopt these now and the year of content is genuinely low-risk.

### 6.1 Fill in the `id` field on every game data asset

`BaseGameData.Id` falls back to the **asset file name** when `id` is blank
(`BaseGameData.cs:30-33`), and `DataId` is a hash of it. `Prototype_World_01`
currently has an empty `id`, as do your items. That's harmless today and
destructive later: rename a sword after players own one and its DataId
changes, leaving unresolvable rows in `characteritem`.

Set `id` explicitly — `SWORD_DARK_FORTRESS_001`, `MAP_PROTOTYPE_WORLD_01` —
and file names become free to change forever. This costs minutes now and is
close to unfixable once you have live players.

### 6.2 Run Milestone 2 before every deploy, and after any big system

Not as ceremony — as the thing that catches "I broke networking three weeks
ago and didn't notice". Two clients, walk at each other, done in five minutes.

### 6.3 Never add gameplay state on the client

When you write new systems, the question is always "who decides this?" and the
answer is always "the server". If a new feature needs to persist, it goes
through the database layer; if it affects other players, it's server-side and
replicated. The kit's existing components are the model to copy — when in
doubt, find the closest thing the kit already does and follow its structure.

---

## 7. Then, and only then, the VPS

`README.md` in this folder covers it. By the time you get there it's the same
four processes you've already been running on loopback, with MySQL instead of
SQLite and a real IP in `publicAddress` instead of `127.0.0.1`.

Suggested order: Milestones 0 → 1 → 2 this week; VPS whenever you feel like
it. There is no reason to pay for a VPS until Milestone 2 passes, and no
reason to rush Milestone 3 — a year of content development happens entirely on
your own machine either way.
