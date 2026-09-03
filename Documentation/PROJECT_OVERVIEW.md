# MMORPG Granny: Project Architecture Overview

This is the entry point to the architecture documentation of the `MMORPG Granny` Unity project. It explains what the project is built from, how the pieces fit together, what happens at runtime, and where to go for details. Every subsystem has its own document under [Systems/](Systems/).

The documentation was produced by reading the source code in this repository. Where the official MMORPG KIT documentation or the Asset Store version differ from what is in this repository, the repository wins and the difference is called out.

## 1. What this repository is

| Fact | Value |
|---|---|
| Product | `MMORPG Granny` (`ProjectSettings/ProjectSettings.asset`, bundle version 1.97) |
| Engine | Unity 6000.3.13f1, Universal Render Pipeline 17.3.0, Input System 1.19.0 in "Both" mode |
| Framework | Suriyun **MMORPG KIT** ("UnityMultiplayerARPG"), namespaces `MultiplayerARPG` and `MultiplayerARPG.MMO` |
| Kit provenance | `Core/` mirrors GitHub `suriyun-mmorpg/UnityMultiplayerARPG_Core` at commit `2830829`; `MMO/` mirrors `UnityMultiplayerARPG_MMO` at `cbccdcf` (both applied 2026-08-29). `Demo*/` folders are older Asset Store content (February 2026) with no public repository. See [Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md](Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md). |
| Networking | LiteNetLib 1.0.1-1 through the kit's `LiteNetLibManager` layer (UDP, WebSocket, offline transports) |
| Persistence | MySQL or SQLite inside Unity server builds; PostgreSQL and Redis exist in source for the .NET server build only |
| Official kit docs | https://suriyun-production.github.io/mmorpg-kit-docs/ |
| Project change log | [CHANGELOG.md](../CHANGELOG.md) at the repository root. It is the authoritative record of every project customization and the reasoning behind it. |

The project is at an early stage: it is the kit plus a first slice of project content (a Synty modular player character, a top-down cursor-aim controller, two swords, two armor slots, a prototype world scene, and a handful of editor tools). Most gameplay data still comes from the kit's demo folder.

## 2. How to use this documentation

Read in this order if you are new:

1. This overview (sections 3 to 8).
2. [Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md](Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md): what is project-owned versus kit-owned, which kit files were edited in place, and the checklist to re-apply after a kit update.
3. The source itself, using the system map in section 10 to find the right directory.

**There are deliberately no per-subsystem documents for the kit itself.** Of the 2,514 C# files here, 2,507 are vendored kit code, and `Core/` and `MMO/` are replaced wholesale when the kit is mirrored from GitHub. Prose describing them goes stale in one discontinuous step, silently, while the source stays readable. This documentation therefore holds only what cannot be recomputed from the code: the project's own decisions, the vendor boundary, and a map into the source. For detail on a kit subsystem, read the source, or ask an agent to generate a map of it from the current code.

Paths are repository-relative and start with `Assets/`.

Conventions used throughout:

- **Kit Core** means `Assets/UnityMultiplayerARPG/Core/`. **Kit MMO** means `Assets/UnityMultiplayerARPG/MMO/`. **Kit Demo content** means `Assets/UnityMultiplayerARPG/Demo*/`. **Project custom** means `Assets/Scripts/`, `Assets/1. Data/`, `Assets/TopDownController/`, project-authored prefabs and any kit file this project edited in place.
- Demo2D, DemoShooter, DemoSurvival and the Shooter controller family exist in the repository but are out of scope for this documentation pass. They are mentioned only where a system branches on them.

## 3. Repository layout

| Path | Content | Origin |
|---|---|---|
| `Assets/UnityMultiplayerARPG/Core/Scripts/` | The gameplay framework: GameInstance, game data ScriptableObjects, entities, character systems, networking handlers, UI, LAN game (1480 C# files) | Kit Core |
| `Assets/UnityMultiplayerARPG/Core/LiteNetLibManager/` | Networking library (transport, identity, sync fields, RPC, request/response) plus bundled LiteNetLib, UniTask, ZString, Fleck (374 C# files) | Kit Core sub-library |
| `Assets/UnityMultiplayerARPG/Core/SharedData/` | Plain C# data types shared with the .NET server projects: `CharacterData`, `PlayerCharacterData`, `CharacterItem`, `GuildData`, `PartyData`, `Mail`, UI key enums | Kit Core sub-library |
| `Assets/UnityMultiplayerARPG/Core/{CameraAndInput,AudioManager,GraphicSettings,UpdateManager,DevExtension,AddressableAssetTools,UnityEditorUtils,UnityRestClient,SpatialPartitioningSystems,SerializableCallback,SerializationSurrogates,xNode}/` | Independent sub-libraries, each with its own assembly definition | Kit Core sub-libraries |
| `Assets/UnityMultiplayerARPG/Core/Editor/` | Kit editor tooling: game database editor, entity creators, NPC dialog graph editor, build helpers (38 files) | Kit Core |
| `Assets/UnityMultiplayerARPG/MMO/Scripts/MMOGame/` | MMO layer: `MMOServerInstance`, `MMOClientInstance`, `MapNetworkManager`, server-side handlers; `Src/` holds the Central, Cluster, MapSpawn and Database sources shared with the .NET server projects (329 C# files) | Kit MMO |
| `Assets/UnityMultiplayerARPG/MMO/{Plugins,SQLs}/` | MySqlConnector, Mono.Data.Sqlite, sqlite3 natives, I18N; `mysql_main.sql` schema | Kit MMO |
| `Assets/UnityMultiplayerARPG/GuildWar/` | Guild war add-on (12 files) talking to an external Node.js service | Kit add-on |
| `Assets/UnityMultiplayerARPG/Demo/` | 3D demo content: game data under `GameData/Resources/`, entity and UI prefabs, scenes `00Init`, `01Home`, `Map001`, `Map002`, input actions asset, nine demo scripts | Kit Demo content (Asset Store era) |
| `Assets/UnityMultiplayerARPG/{Demo2D,DemoShooter,DemoSurvival,DemoGuildWar,DemoAddressable}/` | Other template flavours | Kit Demo content (out of scope this pass, except DemoGuildWar assets registered in the project database) |
| `Assets/UnityMultiplayerARPG/MMO/Demo/` | MMO entry scenes and prefabs (`00Init_MMO`, `01Home_MMO`, `GameInstance`, `MMOServerInstance`, `MMOClientInstance`, `MapNetworkManager`) | Kit MMO demo |
| `Assets/1. Data/` | Project game data and prefabs: `GameDatabase_G.asset`, `GameData/` category folders, `Prefabs/SyntyPlayerCharacter.prefab`, forked UI prefabs, weapon prefab, `Scenes/Prototype_World_01.unity` | Project custom |
| `Assets/Scripts/` | Project runtime and editor code (6 files) | Project custom |
| `Assets/TopDownController/` | Cursor-aim controller and top-down camera prefabs (1 script, 3 prefabs) | Project custom, derived from the creator's TopDownController add-on |
| `Assets/Settings/` | URP pipeline and renderer assets, quality levels | Project custom (Unity URP template) |
| `Assets/Resources/BillingMode.json` | Unity IAP store selection (Google Play) | Unity Purchasing |
| `Packages/manifest.json` | Unity package set (Addressables, AI Navigation, Purchasing, Vivox, Cinemachine, unity-mcp, ...) | Project configuration |
| `ProjectSettings/` | Player settings, build scene list, define symbols, input axes, tags and layers | Project configuration |
| `CHANGELOG.md` | Project change record with reasoning | Project custom |

Purchased art and audio packs (Synty Polygon Fantasy Hero Characters, Synty Animation Base Locomotion, Synty interface packs, Action RPG SFX V2, Hovl Studio VFX, Kevin Iglesias Human Animations, Melee Weapons Pack 1, BLINK icons) are excluded by `.gitignore` and must be re-imported after cloning. Prefabs under `Assets/1. Data/` reference them.

## 4. Architecture at a glance

```mermaid
flowchart TB
    subgraph Unity["Unity 6 runtime"]
        URP["URP 17.3"]
        InputSys["Input System 1.19"]
        Addr["Addressables 2.9 (compiled out on Standalone)"]
    end
    subgraph Net["Networking library (Kit Core sub-library)"]
        LNL["LiteNetLib transport"]
        LNLM["LiteNetLibManager: identity, sync fields, RPC, request/response, AOI"]
        LNL --> LNLM
    end
    subgraph Core["Kit Core framework (Assembly-CSharp)"]
        GI["GameInstance singleton + static registries"]
        GD["Game data ScriptableObjects (items, skills, characters, maps, quests, NPC dialogs)"]
        ENT["Entities: BaseGameEntity, BaseCharacterEntity, Player/Monster, Npc, Vehicle, Building, Harvestable, drops"]
        NM["BaseGameNetworkManager + handler interfaces"]
        UI["UI framework (UIBase, UISceneGameplay)"]
        CTRL["Player controllers, camera, input"]
        GD --> GI
        GI --> ENT
        NM --> ENT
        ENT --> UI
        CTRL --> ENT
    end
    subgraph MMO["Kit MMO layer"]
        CEN["CentralNetworkManager + ClusterServer"]
        SPAWN["MapSpawnNetworkManager"]
        MAP["MapNetworkManager (map server)"]
        DBM["DatabaseNetworkManager / IDatabase (MySQL, SQLite)"]
        MAP --> DBM
        MAP --> CEN
        SPAWN --> CEN
    end
    subgraph Proj["Project layer"]
        PD["Assets/1. Data: GameDatabase_G, items, armor types, MapInfo, Synty character, UI forks"]
        PC["TopDownAimController"]
        PA["LocomotionPhaseSync, ActionLayerMaskUpdater, UIEscapeWindowsHandler"]
        PE["Editor tools: Synty builders, Scene Shortcuts"]
    end
    Unity --> Core
    LNLM --> NM
    NM --> MAP
    PD --> GD
    PC --> CTRL
    PA --> ENT
    PA --> UI
```

Layering rules that hold in the code:

- Everything in `Core/Scripts`, `MMO/Scripts`, `GuildWar`, `Demo*/Scripts`, `Assets/Scripts` and `Assets/TopDownController` compiles into the single default `Assembly-CSharp`. Only the sub-libraries (LiteNetLibManager, UniTask, xNode, CameraAndInput, and so on) have assembly definitions. This means project code can use `partial class` and `[DevExtMethods]` hooks on kit classes directly.
- The MMO layer inherits from the Core layer: `MapNetworkManager : BaseGameNetworkManager : LiteNetLibGameManager : LiteNetLibManager`. LAN/offline play uses `LanRpgNetworkManager : BaseGameNetworkManager` instead.
- Game data is loaded once into static dictionaries on `GameInstance` keyed by an integer `DataId` derived from the asset name. Entities reference data by id, never by asset reference, so the same data works on client, server and in the database.
- Server authority is the rule: clients send requests or RPCs, the server validates and mutates synchronized state, and the state replicates back. Movement is the main client-driven exception, in `Assets/UnityMultiplayerARPG/Core/Scripts/Gameplay/EntityMovementSystems/`.

## 5. What happens when the game starts

Build scene 0 is `Assets/UnityMultiplayerARPG/Demo/Scenes/00Init.unity` (the LAN/offline flavour). Its `GameInstance` object references the project's `Assets/1. Data/GameDatabase_G.asset`, the project's `Assets/1. Data/Prefabs/UI Prefabs/CanvasGameplay_G.prefab` and the project's `Assets/TopDownController/Demo/Prefabs/TopDownAimController.prefab`. The MMO flavour starts from `Assets/UnityMultiplayerARPG/MMO/Demo/Scenes/00Init_MMO.unity`, whose `GameInstance` prefab still points at the kit's demo database, stock controller and stock canvas (see section 9).

```mermaid
sequenceDiagram
    participant Scene as 00Init scene
    participant GI as GameInstance (execution order int.MinValue)
    participant DB as GameDatabase_G (BaseGameDatabase)
    participant Reg as GameInstance static registries
    participant Home as Home scene (01Home)
    participant NM as Network manager
    Scene->>GI: Awake(): singleton, DontDestroyOnLoad, create default services when fields are null
    GI->>GI: InvokeInstanceDevExtMethods("Awake")
    GI->>DB: Start(): GameDatabase.LoadData(this)
    DB->>DB: LoadDataImplement(): PrepareRelatesData on every asset
    DB->>Reg: AddItems / AddSkills / AddCharacters / AddMapInfos / ... (DataId dictionaries)
    DB->>GI: LoadedGameData()
    GI->>GI: OnGameDataLoadedEvent (listeners: MMOServerInstance, data exporters, UI)
    GI->>Home: LoadHomeSceneTask() (platform-specific home scene, Addressables when enabled)
    Home->>NM: user starts host / connects (LAN) or logs in through MMOClientInstance (MMO)
    NM->>NM: EnterGame request, map scene load, ClientReady, spawn player entity
```

The default services created for null fields are all in `GameInstance.Awake()`, and the execution-order constants in `Core/Scripts/Consts/DefaultExecutionOrders.cs`. The MMO server start sequence (command-line arguments, `serverConfig.json`, and which of the central, map spawn, database and map servers a given process starts) is in `MMO/Scripts/MMOGame/MMOServerInstance.cs` alongside `Src/Consts/ProcessArguments.cs` and `Config/ServerConfig.cs`.

## 6. Who owns global state

| State | Owner | Notes |
|---|---|---|
| Loaded game data (items, skills, attributes, characters, map infos, quests, NPC dialogs, factions, ...) | Static dictionaries on `GameInstance` (`Assets/UnityMultiplayerARPG/Core/Scripts/GameInstance/GameInstance_Data.cs`) | Filled once by the game database at start; keyed by `DataId` |
| Gameplay services (gameplay rule, inventory manager, save system, GM commands, day/night, bones setup, network setting, message manager) | Serialized ScriptableObject fields on `GameInstance`, with defaults created in `Awake()` | Swap by assigning a subclass asset |
| Client/server feature handlers (inventory, character, party, guild, chat, storage, mail, cash shop, ...) | Static interface properties on `GameInstance`, assigned by the active network manager's `_FeatureHandlers` partial | Different implementations for LAN (`LanRpg*`) and MMO (`MMOServer*`) |
| The local player | `GameInstance.PlayingCharacter` / `PlayingCharacterEntity`, `JoinedParty`, `JoinedGuild`, `OpenedStorages`, `UserId`, `AccessToken` | Static; set by the network manager when the player entity spawns |
| Networked objects and players | `LiteNetLibGameManager.Assets` (spawned objects), `Players` | Server keeps the authoritative set |
| Per-map server state (online players, warp requests, instance ids, pending saves) | `MapNetworkManager` (MMO) or `LanRpgNetworkManager` (LAN) | One per process |
| Cluster-wide state (registered app servers, online users, channels) | `CentralNetworkManager` + `ClusterServer` | One central per cluster |
| Persistent state | SQL through `IDatabase` (MMO) or `DefaultGameSaveSystem` files (LAN) | `MMO/Scripts/MMOGame/Src/Database/`, `Core/Scripts/LanGame/SaveSystem/` |

## 7. Runtime topologies

```mermaid
flowchart LR
    subgraph LAN["LAN / offline (00Init scene)"]
        H["Host: LanRpgNetworkManager (server + client in one process)"]
        C1["Client"] --> H
        H --> SAVE["DefaultGameSaveSystem (local files)"]
    end
    subgraph MMOT["MMO (00Init_MMO scene, MMOServerInstance per process)"]
        CL["Client: MMOClientInstance"] -->|login, character list, select| CEN["Central server (CentralNetworkManager + ClusterServer)"]
        CL -->|EnterGame with access token| MAPS["Map server(s): MapNetworkManager"]
        MAPS -->|ClusterClient| CEN
        MSP["Map spawn server: MapSpawnNetworkManager"] -->|ClusterClient| CEN
        MSP -->|launches processes| MAPS
        MAPS -->|IDatabaseClient| DBS["Database server: DatabaseNetworkManager or REST"]
        DBS --> SQL["MySQL / SQLite"]
    end
```

Both topologies run the same gameplay code; the difference is which `BaseGameNetworkManager` subclass is active and which handler implementations it installs. The MMO topology lives in `MMO/Scripts/MMOGame/`, and the network foundation in `Core/LiteNetLibManager/` plus `Core/Scripts/Networking/`.

## 8. How data flows

```mermaid
flowchart LR
    A["Game data assets (.asset ScriptableObjects)"] --> B["GameInstance registries (DataId)"]
    B --> C["Entity prefabs + character data structs (CharacterData, CharacterItem, CharacterSkill, ...)"]
    C --> D["Server-side entity state: LiteNetLibSyncField / SyncList"]
    D --> E["Clients (replicated state, UI reads it)"]
    E -->|requests / RPCs| D
    D --> F["Data updaters mark dirty"]
    F --> G["IDatabase (SQL) or DefaultGameSaveSystem (LAN)"]
    G --> C
```

- Definitions are ScriptableObjects; instances are plain structs and classes (`CharacterItem`, `CharacterSkill`, `CharacterBuff`, `CharacterQuest`) carrying a `dataId` and per-instance values. See `Core/Scripts/GameData/` and `Core/SharedData/`.
- Derived values (final stats, resistances, calculated buffs) are computed and cached per entity, never persisted. See `Core/Scripts/MemoryManagement/Caching/`.
- Custom per-character values use the built-in public, private and server boolean, int and float collections instead of schema changes. See `CharacterDataBoolean`, `CharacterDataInt32` and `CharacterDataFloat32` in `Core/SharedData/`.

## 9. Core framework versus project customization

The kit is treated as a vendored framework. Project work is meant to live in `Assets/1. Data`, `Assets/Scripts`, `Assets/TopDownController` and in forked prefabs, using the kit's extension mechanisms (subclassing, partial classes, `[DevExtMethods]` hooks, entity events, handler interface swaps, ScriptableObject service swaps). The full inventory, including the kit files that were edited in place and the checklist to re-apply after a kit update, is in [Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md](Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md).

Summary of what the project currently owns:

| Area | Project element | Kit element it builds on |
|---|---|---|
| Game database | `Assets/1. Data/GameDatabase_G.asset` (explicit `GameDatabase`), `Assets/1. Data/GameData/**` | `GameDatabase`, `BaseGameData` |
| Player character | `Assets/1. Data/Prefabs/SyntyPlayerCharacter.prefab` (Synty FixedScale rig, `PlayableCharacterModel`) | `PlayerCharacterEntity`, `PlayableCharacterModel`, `CharacterControllerEntityMovement` |
| Animation | `LocomotionPhaseSync`, `ActionLayerMaskUpdater`, `SyntyUpperBody.mask` | `AnimationPlayableBehaviour` layer mixers |
| Controller and camera | `TopDownAimController`, `TopDownGameplayCamera.prefab` | `PlayerCharacterController`, `FollowCameraControls` |
| UI | `CanvasGameplay_G`, `UIDialogs_G`, `UIItemsDialog` forks, `UIEscapeWindowsHandler` | `UISceneGameplay`, `UIBase`, `UIEquipItems` |
| Items and equipment | `Legs`/`Cloak` armor types, `Legs001_G`, `DarkFortressSword001_G`, `SyntySword001_G`, `SM_Wep_Sword_01_G.prefab` | `ArmorType`, `ArmorItem`, `WeaponItem`, `EquipmentModel` |
| World | `Prototype_World_01.unity` + `MapInfo` (terrain, NavMesh, one spawn point, no spawn areas yet) | `MapInfo`, `BaseGameNetworkManager` scene flow |
| Editor tools | Scene Shortcuts, Synty Equipment Container Builder, Synty Locomotion Animation Builder | `BaseCharacterModel.EquipmentContainers`, `PlayableCharacterModel.defaultAnimations` |

Both entry scenes matter: `00Init.unity` (LAN) is wired to the project assets above, while `00Init_MMO.unity` still instantiates the kit's `GameInstance.prefab`, which references `Assets/UnityMultiplayerARPG/Demo/GameData/GameDatabase.asset`, the stock `PlayerCharacterController.prefab` and the stock `CanvasGameplay.prefab`. The kit's demo `GameDatabase.asset` has itself been edited to include the project content, so the two databases differ only by the two project swords.

## 10. System map

Where each system lives. Kit paths below are relative to `Assets/UnityMultiplayerARPG/`; project paths are given in full. Use this to find the source, then read it. Nothing here restates what the code says.

| System | Key types | Source |
|---|---|---|
| Bootstrap and global state | `GameInstance` | `Core/Scripts/GameInstance/` |
| Game data definitions | `BaseGameData`, `GameDatabase`, `GameDataHelpers` | `Core/Scripts/GameData/`, `Core/Scripts/GameData/Database/` |
| Shared data types | `CharacterData`, `PlayerCharacterData`, `CharacterItem`, `GuildData`, `PartyData`, `Mail` | `Core/SharedData/` |
| Networking library | `LiteNetLibManager`, `LiteNetLibIdentity`, sync fields and lists, RPC, transports | `Core/LiteNetLibManager/Scripts/` |
| Game networking | `BaseGameNetworkManager`, handler interfaces, message structs | `Core/Scripts/Networking/` |
| LAN and offline play | `LanRpgNetworkManager`, `DefaultGameSaveSystem` | `Core/Scripts/LanGame/` |
| MMO servers | `MMOServerInstance`, `CentralNetworkManager`, `MapNetworkManager`, `MapSpawnNetworkManager` | `MMO/Scripts/MMOGame/` |
| Database and persistence | `IDatabase`, `MySQLDatabase`, `SQLiteDatabase`, `DatabaseNetworkManager` | `MMO/Scripts/MMOGame/Src/Database/` |
| SQL schema | 34 tables | `MMO/SQLs/mysql_main.sql` |
| Entity framework | `BaseGameEntity`, `DamageableEntity`, `EntityInfo` | `Core/Scripts/Gameplay/` |
| Entity movement | `CharacterControllerEntityMovement`, `NavMeshEntityMovement` | `Core/Scripts/Gameplay/EntityMovementSystems/` |
| Characters | `BaseCharacterEntity`, `PlayerCharacterEntity`, `BaseMonsterCharacterEntity` | `Core/Scripts/Gameplay/CharacterEntity/` |
| Character components | recovery, skill and buff, crafting, dealing, vending, dueling, PK, building | `Core/Scripts/Gameplay/CharacterSystems/` |
| Character data instances | `CharacterAttribute`, `CharacterSkill`, `CharacterQuest`, `CharacterBuff` | `Core/Scripts/CharacterData/` |
| Stat caching | `CharacterDataCacheManager`, `CalculatedBuff` | `Core/Scripts/MemoryManagement/Caching/` |
| Combat and damage | `IDamageInfo`, `MeleeDamageInfo`, `MissileDamageInfo`, `DamageElement` | `Core/Scripts/GameData/Damage/`, `Core/Scripts/Gameplay/DamageEntities/` |
| Attack and skill execution | `DefaultCharacterAttackComponent`, `DefaultCharacterUseSkillComponent` | `Core/Scripts/Gameplay/CharacterSystems/CharacterActionsSystem/` |
| Skills | `BaseSkill`, `Skill`, `SkillSummon`, `SkillMount` | `Core/Scripts/GameData/Skill/` |
| Buffs and status effects | `Buff`, `StatusEffect`, `AilmentPresets` | `Core/Scripts/GameData/Buff/` |
| Items and inventory | `BaseItem`, `Item`, the item implementations and interfaces | `Core/Scripts/GameData/Item/` |
| Equipment | `ArmorType`, `WeaponType`, `EquipmentModel`, `EquipmentSet` | `Core/Scripts/GameData/Item/Equipments/` |
| Character models and animation | `BaseCharacterModel`, `PlayableCharacterModel`, `AnimationPlayableBehaviour` | `Core/Scripts/GameData/Model/` |
| Quests | `Quest`, `QuestTask`, `BaseCustomQuestTask` | `Core/Scripts/GameData/Quest/` |
| NPCs and dialogue | `Npc`, `NpcDialog`, `NpcDialogGraph`, conditions and actions | `Core/Scripts/GameData/Npc/`, `Core/Scripts/Gameplay/Npcs/` |
| Monster AI | `MonsterActivityComponent` | `Core/Scripts/Gameplay/CharacterSystems/MonsterCharacterSystems/` |
| Spawn areas | `MonsterSpawnArea`, `HarvestableSpawnArea`, `GameSpawnArea` | `Core/Scripts/Gameplay/Area/` |
| Maps, portals and scenes | `MapInfo`, `WarpPortal`, `WarpPortalEntity` | `Core/Scripts/GameData/MapInfo/`, `Core/Scripts/GameData/WarpPortal/` |
| Building | `BuildingEntity`, `BuildingArea`, `WorkbenchEntity` | `Core/Scripts/Gameplay/BuildingSystems/` |
| Harvesting | `Harvestable`, `HarvestableEntity` | `Core/Scripts/GameData/Harvestable/`, `Core/Scripts/Gameplay/HarvestSystems/` |
| Vehicles and mounts | `VehicleEntity`, `MountEntity`, `VehicleSeat` | `Core/Scripts/Gameplay/VehicleSystems/` |
| Rewards and drops | `Reward`, `ItemDropEntity`, `ItemsContainerEntity` | `Core/Scripts/Gameplay/Rewarding/` |
| Social | `SocialCharacterData`, `SocialSystemSetting`, guild and party data | `Core/Scripts/Gameplay/Social/`, `Core/SharedData/` |
| Gameplay rules | `BaseGameplayRule`, `DefaultGameplayRule` | `Core/Scripts/Gameplay/Rule/` |
| Effects | `GameEffect`, `PoolingGameEffectsPlayer` | `Core/Scripts/GameEffect/` |
| UI framework | `UIBase`, `UISceneGameplay`, `UIList` | `Core/Scripts/UI/` |
| Input, camera, controllers | `InputManager`, `FollowCameraControls`, `PlayerCharacterController` | `Core/CameraAndInput/`, `Core/Scripts/Gameplay/CharacterControllerSystems/`, `Assets/TopDownController/` |
| Localization | `LanguageManager`, `UITextKeys` | `Core/Scripts/Language/`, `Core/SharedData/` |
| Audio and graphics settings | `AudioManager`, `GraphicSettingManager` | `Core/AudioManager/`, `Core/GraphicSettings/` |
| Addressables | `AssetReference` wrappers, download manager | `Core/AddressableAssetTools/`, `Core/Scripts/AddressableAssets/` |
| Cash shop and IAP | `CashShopItem`, `CashPackage`, purchasing | `Core/Scripts/GameData/CashShop/`, `Core/Scripts/GameInstance/GameInstance_Purchasing.cs` |
| GM commands | `BaseGMCommands`, `DefaultGMCommands` | `Core/Scripts/Gameplay/GMCommands/` |
| Extension mechanisms | `DevExtUtils`, `[DevExtMethods]`, `GameExtensionInstance` | `Core/DevExtension/`, `Core/Scripts/Modding/`, `Demo/Scripts/DevExt/` |
| Editor tooling | database editor, entity creators, dialog graph editor | `Core/Editor/`, `Assets/Scripts/Editor/` |
| Guild war add-on | `GuildWarMapInfo`, `GuildWarCastleHeart` | `GuildWar/` |
| Project game data and prefabs | `GameDatabase_G`, Synty character, UI forks, prototype map | `Assets/1. Data/` |
| Project runtime code | `LocomotionPhaseSync`, `ActionLayerMaskUpdater`, `UIEscapeWindowsHandler` | `Assets/Scripts/` |

## 11. Conditional compilation and build flavours

`ProjectSettings/ProjectSettings.asset` defines `STEAMWORKS_NET` for Standalone, Android, iOS and WebGL (no Steamworks code exists; the define is inert) and `DISABLE_ADDRESSABLES` for Standalone. The second one matters: every `#if !DISABLE_ADDRESSABLES` path in the kit (addressable entity, UI and scene loading) is compiled out of PC builds, and the direct prefab references on `GameInstance` and `GameDatabase` are used instead. The editor compiles both paths. Legacy numeric entries carry `UNITY_SERVER`, `NO_GPGS` and `ENABLE_PURCHASING;UNITY_PURCHASING`.

Kit-recognised symbols with the largest footprint: `UNITY_SERVER` and `EXCLUDE_SERVER_CODES` (strip server code from clients), `EXCLUDE_PREFAB_REFS` (addressables-only builds), `DISABLE_CUSTOM_CHARACTER_DATA`, `DISABLE_CLASSIC_PK`, `DISABLE_DIFFER_MAP_RESPAWNING`, `ENABLE_INPUT_SYSTEM`, `ENABLE_PURCHASING`. The build scene list is in `ProjectSettings/EditorBuildSettings.asset` and the server launch arguments in `MMO/Scripts/MMOGame/Src/Consts/ProcessArguments.cs`.

## 12. Where to change what

| I want to... | Go to |
|---|---|
| Add an item, skill, quest, NPC, map or monster definition | Create the asset under `Assets/1. Data/GameData/<Category>/`, register it in `Assets/1. Data/GameDatabase_G.asset`. `Core/Scripts/GameData/Database/` |
| Add a new item or skill type | Subclass `BaseItem`/`Item` or `BaseSkill`/`Skill` with `CreateAssetMenu`. `Core/Scripts/GameData/Item/`, `Core/Scripts/GameData/Skill/` |
| Change damage, exp, gold or drop formulas | Subclass `BaseGameplayRule` and assign it on `GameInstance`. `Core/Scripts/Gameplay/Rule/` |
| React to entity lifecycle or damage without touching kit code | Partial class with `[DevExtMethods("Awake")]` subscribing to `onReceivedDamage`, `onApplyBuff`, and so on. `Core/DevExtension/`, `Demo/Scripts/DevExt/` |
| Store a custom per-character value | Use the public/private/server custom data collections. `Core/SharedData/` |
| Add a new network request | Register in a `BaseGameNetworkManager` partial under `[DevExtMethods("RegisterMessages")]`, add a message struct and handler. `Core/Scripts/Networking/` |
| Change how the player controls the character or camera | Extend `TopDownAimController` or swap `GameInstance.defaultControllerPrefab`. `Assets/TopDownController/`, `Core/Scripts/Gameplay/CharacterControllerSystems/` |
| Add equipment visuals to the Synty character | Run the Synty Equipment Container Builder, set `EquipmentModel.equipSocket` and `instantiatedObjectIndex` on the item. `Assets/Scripts/Editor/SyntyEquipmentContainerBuilder.cs` |
| Add or change animations | Fill `PlayableCharacterModel.defaultAnimations` / `weaponAnimations` or the `WeaponType` playable settings. `Core/Scripts/GameData/Model/` |
| Add a dialog window | Add it under the forked `UIDialogs_G.prefab`; it is auto-collected by `UIEscapeWindowsHandler`. `Assets/1. Data/Prefabs/UI Prefabs/` |
| Run a dedicated server or an MMO cluster | `MMO/Scripts/MMOGame/`, `MMO/SQLs/mysql_main.sql` |
| Update the kit from GitHub | Follow the re-apply checklist in [Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md](Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md) |

## 13. Known gaps and risks (project level)

- The `Demo*/` content is February-2026 Asset Store data running against August-2026 Core and MMO code. Prefab and scene deserialization against changed code is the first suspect when a demo scene misbehaves (recorded in `CHANGELOG.md`, "Known follow-ups").
- Nine kit files or assets have been edited in place (listed in the `00` document). A kit re-import or GitHub mirror overwrites them.
- The project scene `Prototype_World_01.unity` contains terrain, lights, a NavMesh surface and one spawn point, but no monster spawn areas, NPCs, portals or harvestables yet.
- `Assets/1. Data/GameData/` is mostly empty placeholder folders; the game still runs on kit demo data (attributes, currencies, skills, quests, NPCs, monsters).
- The MMO entry scene is not wired to the project's database, controller or canvas.
- Purchased art is not in the repository; a fresh clone shows missing meshes and clips on the Synty character until the packs are re-imported.
- Installed but unused packages (Vivox, Cinemachine, Splines, Timeline, Visual Scripting, Memory Profiler, Multiplayer Center) add editor and build weight without kit integration. The set is in `Packages/manifest.json`.

## 14. Out of scope for this pass

Demo2D, DemoShooter, DemoSurvival, `MMO/Demo2D`, the 2D entity and movement classes, the Shooter controller family, and survival-specific mechanics are not documented in depth. They remain in the repository and compile.
