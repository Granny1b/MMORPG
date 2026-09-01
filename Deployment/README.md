# Deploying to a VPS

Everything here targets the MMO mode of MMORPG KIT (`Assets/UnityMultiplayerARPG/MMO/`).
Paths in the scripts assume a Debian/Ubuntu VPS with root access and a real
public IPv4 address.

---

## 1. What actually has to run

MMO mode is not one server. It is four cooperating processes, all of them the
*same* Linux binary started with different flags:

| Process | Flag | Default port | Exposed? | Job |
|---|---|---|---|---|
| Database manager | `-startDatabaseServer` | 6100 | **no** | The only process that talks to MySQL. |
| Central | `-startCentralServer` | 7000 (+ cluster 6010) | **yes**, 7000 only | Login, character list, party/guild/chat, the cluster bus. |
| Map spawn | `-startMapSpawnServer` | 6001 | **no** | Launches a map-server child per map. |
| Map server | `-startMapServer` | 8000, 8001, … | **yes** | One process per map, holds the live world. |

You never start map servers yourself — the spawner launches them, re-running
its own binary with `-channelId`, `-mapName`, `-mapPort` and the cluster
address appended (`MapSpawnNetworkManager.cs:395`).

Startup order matters: **database → central → map spawn**. Central opens a
database client on start; if the DB manager isn't listening it logs an error
and logins fail. The systemd units in `systemd/` encode that with
`Requires=`/`After=`.

Config comes from `./Config/serverConfig.json`, resolved **relative to the
process working directory** (`ConfigManager.cs:61`), so the systemd
`WorkingDirectory=` must be the server root or the file is silently ignored
and every default applies. Any key can also be passed as a CLI flag, and the
flag wins.

---

## 2. Before you touch the VPS: three things that will block you

### 2.1 The MMO scene is not wired to your game

`GameDatabase_G` is referenced by exactly one scene — `00Init.unity`, the
single-player demo init. The MMO path uses
`MMO/Demo/Prefabs/GameInstance.prefab`, whose `gameDatabase` field still
points at the kit's stock demo database (guid `78362f3a…`, not
`038c79f8…`). Boot the MMO stack as-is and you get the kit's demo content,
not yours.

So before deploying:

1. Duplicate `00Init_MMO.unity` and the MMO `GameInstance` prefab into
   `Assets/1. Data/` (don't edit the kit copies you may want to update later).
2. Point the copied prefab's `gameDatabase` at `GameDatabase_G`.
3. Make sure `Prototype_World_01` is registered as a MapInfo in
   `GameDatabase_G` and its scene is in Build Settings.
4. Set the MMO init scene as scene index 0 for the **server** build.

### 2.2 Ignored asset packs

`.gitignore` excludes ~1.9 GB of purchased Asset Store content (Synty, Hovl,
Kevin Iglesias, Melee Weapons Pack, Action RPG SFX, BLINK). A clean clone
cannot build. Build from your working machine, which has them — do **not**
try to build on the VPS.

### 2.3 The build target

Install the **Dedicated Server / Linux** module in Unity Hub for
`6000.3.13f1`. Build `Linux x86-64`, Dedicated Server subtarget. That build
strips rendering, audio and input, which is the difference between a map
server costing ~250 MB and ~700 MB. Name the output `MMORPGServer` to match
the systemd units, or edit `ExecStart=` to match your name.

You will produce **two builds from the same commit**: the Linux server build
that goes to the VPS, and a Windows client build for players. They must come
from the same commit — see §6.

---

## 3. Is a basic one.com VPS enough?

Check the plan's specs and compare against this, measured per process:

- Database manager, central, map spawn: roughly 100–200 MB RSS each, low CPU.
- **Each map server: expect 250–600 MB and a meaningful slice of one core.**
  Unity ticks a fixed physics/network step per map regardless of how many
  players are on it — an empty map still burns CPU.

So the floor for one map is around **2 GB RAM and 2 vCPU**, and that leaves
nothing for MySQL under load. 4 GB / 2 vCPU is a comfortable "small live
world". A 1 vCPU / 1–2 GB plan will boot and let you and a friend walk
around, and will fall over as soon as you add a second map or a dozen
players.

Two things to confirm with one.com **before** you buy or commit, because
either one is fatal:

1. **Do you get a dedicated public IPv4, and can you open arbitrary UDP
   ports?** The kit's default transport (LiteNetLib) is UDP. Hosts that only
   proxy HTTP/HTTPS, or that hand you a shared IP behind a web proxy, cannot
   run this at all. one.com's core business is web hosting, so this is worth
   verifying explicitly rather than assuming.
2. **Is there a bandwidth cap?** A map server broadcasts entity state to every
   connected client several times a second. Budget roughly 5–20 KB/s per
   player each way as a starting estimate and measure your own build.

If UDP is blocked but TCP is open, your fallback is `useWebSocket: true`,
which moves the transport to TCP/WebSocket. It works, but it is a worse fit
for a real-time action game and you should treat it as a workaround.

---

## 4. Deploy, step by step

### 4.1 Prepare the box

```bash
scp Deployment/scripts/setup-vps.sh root@YOUR_VPS:/root/
ssh root@YOUR_VPS 'bash /root/setup-vps.sh'
```

This installs MariaDB, creates the `mmo` service user and `/srv/mmo`, creates
the database with a generated password (**write it down — it is printed once**),
binds MySQL to loopback, and sets a UFW policy that opens only SSH, 7000 and
8000–8010.

### 4.2 Import the schema

```bash
scp Assets/UnityMultiplayerARPG/MMO/SQLs/mysql_main.sql root@YOUR_VPS:/tmp/
ssh root@YOUR_VPS 'mysql -u mmo -p mmorpg_kit < /tmp/mysql_main.sql'
```

### 4.3 Put the MySQL credentials in the build

The `MySQLDatabase` component on the `MMOServerInstance` prefab currently
holds `127.0.0.1 / root / password / mmorpg_kit`. Change `username` and
`password` to what `setup-vps.sh` generated **before** building, or supply a
`connectionString` at runtime. These are serialized into the prefab, so they
end up in the build and in git — if you'd rather not commit credentials, use
the `connectionString` field fed from an environment-specific config instead.

### 4.4 Upload config and build

```bash
# once
scp Deployment/serverConfig.example.json root@YOUR_VPS:/srv/mmo/server/Config/serverConfig.json
ssh root@YOUR_VPS 'nano /srv/mmo/server/Config/serverConfig.json'   # set publicAddress
```

`publicAddress` **must** be the VPS's public IP or a hostname resolving to it.
It is what central hands to clients so they know where to reach the map
server; leave it at `127.0.0.1` and clients will connect to themselves and
time out. This is the single most common first-deploy failure.

Then, from your dev machine:

```bash
VPS_HOST=root@YOUR_VPS BUILD_DIR=../Builds/LinuxServer ./Deployment/scripts/deploy.sh
```

`deploy.sh` stops the services, rsyncs the build (preserving `Config/`),
fixes ownership and the exec bit, and restarts in dependency order.

### 4.5 Install the services

```bash
scp Deployment/systemd/*.service root@YOUR_VPS:/etc/systemd/system/
ssh root@YOUR_VPS 'systemctl daemon-reload && systemctl enable --now mmo-database mmo-central mmo-mapspawn'
```

### 4.6 Backups

```bash
scp Deployment/scripts/backup-db.sh root@YOUR_VPS:/srv/mmo/
ssh root@YOUR_VPS 'chmod +x /srv/mmo/backup-db.sh && (crontab -l 2>/dev/null; echo "0 4 * * * /srv/mmo/backup-db.sh >> /srv/mmo/logs/backup.log 2>&1") | crontab -'
```

Do this on day one, not after the first wipe. Characters, inventories, guilds
and storage are all in that one database and none of it is in git.

### 4.7 Point the client at the server

In the client build, set the `MmoNetworkSetting` (see
`MMO/Demo/GameData/MmoNetworkSettings/`) to your VPS address and port 7000.
For a server list you can edit without rebuilding the client, put a
`serverList.txt` in `StreamingAssets` — `ConfigManager.ReadServerList()`
parses `Title,host:port,webSocketSecure` per line.

---

## 5. Security notes worth taking seriously

- **The database manager port has no authentication.** `DatabaseNetworkManager`
  is a plain LiteNetLib server; the only `secretKey` in the codebase belongs to
  the optional REST database client (`RestDatabaseClient.cs:26`). Anyone who
  reaches port 6100 owns every character in your game. Keep it on loopback,
  keep it out of the firewall, and if you ever split the servers across
  machines, tunnel it over WireGuard rather than exposing it.
- Same for the cluster port (6010) and map spawn (6001).
- Run the servers as the unprivileged `mmo` user, never root. The units do.
- Keep MySQL bound to `127.0.0.1`.

---

## 6. How much harder is development once it's live?

Honestly: the *coding* is barely harder. The **process** around it is
substantially harder, and that's where the time goes.

### What stays the same

Keep developing exactly as you do now. You can run the whole MMO stack on
your own PC — the same four flags against `127.0.0.1` — and the kit's demo
even has UI to start them. Nothing about having a VPS forces you to develop
against it. **Do not develop against production.** Treat the VPS as a place
you *ship to*, and it changes your day very little.

### What gets genuinely harder

**The iteration loop stretches from seconds to minutes.** Press Play is ~10
seconds. Build Linux server → rsync → restart → reconnect a client is 5–15
minutes. This is the single biggest tax, and it's why you keep a local stack:
you should only be pushing to the VPS for changes you've already proven
locally.

**Client and server are version-locked.** Both builds must come from the same
commit. A serialization change on either side and clients silently
desync or drop. Tag every deploy (`git tag deploy-2026-09-01`) so you can
tell which client build matches which live server.

**Data migrations become a real concern — and this kit has a specific trap.**
`BaseGameData.DataId` is a hash of the asset's `Id`, which falls back to the
**asset file name** when `id` is left blank (`BaseGameData.cs:30-33, 180-188`).
Rename `SM_Wep_Sword_01_G` after players own one, and its DataId changes; the
rows in `characteritem` still hold the old hash and the item becomes an
unresolvable blank. In single-player this costs you nothing because you wipe
your save. Live, it destroys player inventories.

The fix is cheap and only works if you do it *before* launch: **explicitly set
the `id` field on every game data asset** so it is decoupled from the file
name. Then renaming assets is free forever.

**Schema changes need migrations.** `mysql_main.sql` is the initial schema;
the kit ships `SQLiteDatabase_Migrate.cs`-style migration hooks, but any
column you add yourself is your job to apply to a live database with data in
it. Test the migration against a restored backup before running it on prod.

**Debugging moves from the debugger to logs.** No breakpoints on the VPS. You
get `/srv/mmo/logs/*.log` and `journalctl -u mmo-central -f`. Bugs that only
appear at 80 ms latency, or with two players interacting, or after 6 hours of
uptime, are the ones you'll actually be chasing — and they're the ones you
cannot reproduce in the editor. Budget for adding more logging than feels
necessary.

**Server-authoritative bugs are a new category.** Anything you can do on a
client that the server doesn't re-validate is now an exploit rather than a
curiosity. The kit is server-authoritative by default; the risk is in the
custom gameplay code under `Assets/Scripts/Gameplay/`, where it's easy to
write a client-trusted shortcut that works fine in single-player testing.

**You now have uptime and state to care about.** Restarts disconnect players.
Backups matter. A crash loop at 2am matters. `Restart=always` in the units
covers the boring cases.

### A realistic setup

1. Local dev in the editor, single-player init scene — 95% of your time.
2. Local full MMO stack on your PC — for anything touching networking or
   persistence.
3. VPS as staging while you're the only player: deploy freely, wipe the
   database whenever you like.
4. VPS as production once anyone else has a character: tagged deploys,
   backup-before-deploy, and `id` fields locked down on all game data.

Stage 4 is where the cost appears. Until you have real players, running on a
VPS is only marginally more work than running locally — mostly the build-and-
upload wait.
