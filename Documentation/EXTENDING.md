# Extending the Kit

How to add functionality to this project without editing vendored kit code.

**Upstream reference:** the kit's own dev extension page, https://suriyun-production.github.io/mmorpg-kit-docs/#/pages/037-dev-extension. Read it for the author's intent. Everything below is derived from the source in this repository, which is newer than the published docs and is authoritative when the two disagree. Every hook name, delegate signature and path here was read from the code, and the last section says how to regenerate the tables.

**Why this matters here.** `Assets/UnityMultiplayerARPG/Core/` and `MMO/` are mirrored wholesale from GitHub when the kit is updated. Anything you write inside them is destroyed without warning. The mechanisms below exist so that you never have to. See `CLAUDE.md` for the rules and `Documentation/Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md` for the two places this project had to break that rule anyway.

**One structural fact makes all of this work:** kit gameplay code and our code compile into the same assembly, `Assembly-CSharp`, because neither has an assembly definition file. Kit classes are declared `partial`. So a file in `Assets/Scripts/` can add members to a kit class directly, with no wiring, no reflection on our side, and full compile-time checking.

## The decision procedure

Work down this table and stop at the first row that fits. Lower rows cost more and couple you more tightly to kit internals.

| What you want | Mechanism | Where your code goes |
|---|---|---|
| New item, skill, quest, NPC, map, monster, or any other content | Create a data asset, register it in `GameDatabase_G` | `Assets/1. Data/GameData/` |
| New kind of item, skill, quest task, NPC condition or NPC action | Subclass the kit's abstract data class, add `CreateAssetMenu` | `Assets/Scripts/` |
| Behaviour attached to your own prefab | Plain `MonoBehaviour` | `Assets/Scripts/Gameplay/` |
| React to a kit lifecycle moment (awake, destroy, server start, message registration) | `[DevExtMethods]` hook in a partial class | `Assets/Scripts/` |
| React to something happening to an entity (damage, death, level, buff, inventory change) | Subscribe to an entity event, from a `[DevExtMethods("Awake")]` hook | `Assets/Scripts/` |
| Change a formula or a global rule (damage, exp, gold, drops, level up) | Subclass the ScriptableObject service, assign it on `GameInstance` | `Assets/Scripts/` plus an asset |
| Change how a whole server feature behaves (inventory, party, guild, storage, chat) | Implement the handler interface, assign it | `Assets/Scripts/` |
| Change how the player controls the character or camera | Subclass the controller | `Assets/TopDownController/` or `Assets/Scripts/` |
| Change stat aggregation or item serialization globally | `GameExtensionInstance` static delegate | `Assets/Scripts/` |
| Store extra per-character values | Custom character data, see the worked example | `Assets/Scripts/` |
| None of the above works | Patch the kit, and log it. See the last section. | the kit file, plus `CHANGELOG.md` |

## Mechanism 1: DevExtMethods hooks

The kit calls out to you at fixed points. `Assets/UnityMultiplayerARPG/Core/DevExtension/Scripts/DevExtUtils.cs` provides an extension method that the kit invokes at each of those points:

```csharp
this.InvokeInstanceDevExtMethods("Awake");
```

It reflects over the runtime type of `this`, finds every method tagged `[DevExtMethods("Awake")]`, public or private, and calls them all. Results are cached per type and per hook name after the first call, so the reflection cost is paid once.

You participate by adding a partial class with a tagged method:

```csharp
// Assets/Scripts/Gameplay/PlayerCharacterEntity_DeathCount.cs
using Insthync.DevExtension;
using UnityEngine;

namespace MultiplayerARPG
{
    public partial class PlayerCharacterEntity
    {
        [DevExtMethods("Awake")]
        protected void DeathCount_Awake()
        {
            onKilled += DeathCount_OnKilled;
        }

        [DevExtMethods("OnDestroy")]
        protected void DeathCount_OnDestroy()
        {
            onKilled -= DeathCount_OnKilled;
        }

        private void DeathCount_OnKilled(BaseCharacterEntity target, EntityInfo instigator)
        {
            if (!IsServer)
                return;
            Debug.Log($"{target.CharacterName} was killed by {instigator.Id}");
        }
    }
}
```

Note the namespace and the class name must match the kit's exactly, and the file must be outside the kit tree.

### Every hook in this repository

Grouped by the class that invokes it. A hook fires only for the class that declares the call, so `[DevExtMethods("Awake")]` on `PlayerCharacterEntity` is unrelated to `[DevExtMethods("Awake")]` on `GameInstance`.

| Class | Hook names |
|---|---|
| `GameInstance` | `Awake`, `LoadedGameData`, `OnDestroy` |
| `BaseGameEntity` | `Awake`, `OnDestroy` |
| `BasePlayerCharacterController` | `Awake`, `OnDestroy` |
| `UISceneGameplay` | `Awake`, `OnDestroy` |
| `UIBase` | `Show`, `Hide` |
| `UISelectionEntry` | `UpdateData`, `UpdateUI` |
| `UIMinimapRenderer` | `Awake` |
| `UICharacterStats`, `UIBaseEquipmentBonus` | `SetStatsGenerateTextData`, `SetRateStatsGenerateTextData` |
| `CharacterStatsTextGenerateData` | `GetText` |
| `BaseGameNetworkManager` | `RegisterMessages`, `RegisterClientMessages`, `RegisterServerMessages`, `Clean`, `InitPrefabs`, `OnStartServer`, `OnStopServer`, `OnStartClient`, `OnStopClient`, `OnClientConnected`, `OnClientDisconnected`, `OnPeerConnected`, `OnPeerDisconnected`, `OnClientOnlineSceneLoaded`, `OnServerOnlineSceneLoaded`, `SendClientEnterGame`, `SendClientReady`, `SendClientNotReady`, `SendClientSafeDisconnect`, `HandleEnterGameResponse`, `HandleClientReadyResponse`, `HandleSafeDisconnectResponse`, `ReadMapInfoExtra`, `WriteMapInfoExtra`, `UpdateReadyToInstantiateObjectsStates`, `UpdateClientReadyToInstantiateObjectsStates`, `UpdateServerReadyToInstantiateObjectsStates` |
| `CentralNetworkManager` | `RegisterMessages`, `RegisterClientMessages`, `RegisterServerMessages`, `Clean`, `OnStartServer`, `OnStartClient` |
| `CentralNetworkManager_Character` | `SerializeCreateCharacterExtra`, `DeserializeCreateCharacterExtra` |
| `MapSpawnNetworkManager` | `Clean`, `OnStartServer`, `OnStartClient` |
| `DatabaseNetworkManager` | `RegisterMessages` |
| `MySQLDatabase`, `SQLiteDatabase`, `PostgreSQLDatabase` | `Init` |
| `BaseGameData` | `PrepareRelatesData` |
| `GameDatabase` | `LoadDataImplement`, `LoadReferredData` |
| `ResourcesFolderGameDatabase` | `LoadDataImplement` |
| `PlayerCharacterEntityMetaData` | `Setup` |
| `BaseCharacterModel` | `SetEquipmentContainersBySetters` |
| `GameEntityModel` | `SetEffectContainersBySetters` |
| `BaseEquipmentEntity` | `OnDrawGizmos` |
| `AnimatorCharacterModel2D` | `SetAnimatorClipsForTest` |
| `PlayerCharacterDataExtensions` (static) | `ValidateCharacterData`, `SetNewCharacterData`, `AddAllCharacterRelatesDataSurrogate`, `CloneTo`, `SerializeCharacterData`, `DeserializeCharacterData` |
| `BuildingSaveDataExtensions` (static) | `CloneTo`, `SerializeBuildingSaveData`, `DeserializeBuildingSaveData` |
| `PlayerCharacterSerializationSurrogate` | `GetObjectData`, `SetObjectData` |
| `GameExtensionInstance` (static) | `Init` |

Static hooks are invoked through `DevExtUtils.InvokeStaticDevExtMethods(type, name)` and your method must be `static` to match.

### Rules and failure modes

- **Hook names are strings, so a typo fails silently.** There is no compile error and no warning. Your method simply never runs. If a hook does not fire, check the spelling against the table above first.
- **Exceptions inside a hook are caught and logged, not propagated.** `DevExtUtils` wraps the invocation in a try/catch. A throwing hook will not crash the kit, which also means it will not announce itself loudly. Watch the console.
- **Always unsubscribe.** Subscribe in the `Awake` hook, unsubscribe in the `OnDestroy` hook. Entities are pooled and reused, so a leaked subscription fires against a recycled object.
- **Guard by authority.** Most gameplay reactions belong on the server only. Check `IsServer` before mutating state, or `IsOwnerClient` for local presentation.
- **Give your methods a distinctive prefix.** Several features may hook the same class. `DeathCount_Awake` will not collide, `Awake2` will.
- **The attribute is `Inherited = true`**, declared in `Core/DevExtension/Scripts/DevExtMethodsAttribute.cs`, and `AllowMultiple = false`. One hook name per method.

## Mechanism 2: entity events

For reacting to gameplay rather than lifecycle, entities expose C# events. These are strongly typed, so signatures are checked at compile time, which makes them safer than hook names. Subscribe from a `[DevExtMethods("Awake")]` hook as shown above.

Roughly 60 events exist across `BaseGameEntity_Events.cs`, `BaseCharacterEntity_Events.cs` and `BasePlayerCharacterEntity_Events.cs`. The ones you are most likely to want:

| Event | Fires when | Signature highlights |
|---|---|---|
| `onSetup` | the entity finished initialising | `(BaseGameEntity target)` |
| `onStart`, `onEnable`, `onDisable`, `onUpdate`, `onLateUpdate` | Unity lifecycle, per entity | `(BaseGameEntity target)` |
| `onNetworkDestroy` | the networked object goes away | `(BaseGameEntity target, byte reasons)` |
| `onReceiveDamage` | damage is about to be applied | includes `EntityInfo instigator`, the damage table, weapon and skill |
| `onReceivedDamage` | damage has been applied | `(DamageableEntity target, HitBoxPosition position, Vector3 fromPosition, EntityInfo instigator, CombatAmountType combatAmountType, int totalDamage, CharacterItem weapon, BaseSkill skill, int skillLevel, CharacterBuff buff, bool isDamageOverTime)` |
| `onKilled` | the character died | `(BaseCharacterEntity target, EntityInfo instigator)` |
| `onApplyBuff`, `onRemoveBuff` | buff added or removed | includes the `CharacterBuff` and, on removal, the reason |
| `onLevelChange`, `onExpChange`, `onCurrentHpChange`, `onGoldChange` | a synced value changed | `(BaseCharacterEntity target, <new value>)` |
| `onCanAttackValidated`, `onCanUseSkillValidated`, `onCanMoveValidated`, `onCanJumpValidated` | the kit is deciding whether an action is allowed | lets you veto an action without editing the kit |
| `onNonEquipItemsOperation`, `onEquipItemsOperation`, `onSkillsOperation`, `onQuestsOperation`, `onBuffsOperation` | a synced list changed | `LiteNetLibSyncList<T>.OnOperationDelegate` |

The `onCan...Validated` family is the clean way to add a restriction, for example forbidding attacks in a town, without touching combat code.

Working examples ship with the kit at `Assets/UnityMultiplayerARPG/Demo/Scripts/DevExt/`, notably `DevExtDemo_PlayerCharacterEntity.cs`, which subscribes to eleven events in exactly this pattern.

## Mechanism 3: GameExtensionInstance static delegates

`GameExtensionInstance` is a static partial class holding process-wide delegates for things that are not per entity. Assign them from a static `[DevExtMethods("Init")]` hook, which the class runs from its static constructor.

| Delegate | Purpose |
|---|---|
| `onIncreaseCharacterStats`, `onDecreaseCharacterStats` | adjust how two stat blocks combine |
| `onMultiplyCharacterStats`, `onMultiplyCharacterStatsWithNumber` | adjust stat scaling |
| `onRandomCharacterStats` | control randomised stat rolls |
| `onBuildCalculatedBuff`, `onBuildCalculatedItemBuff` | change how buffs are resolved into final numbers |
| `onCharacterItemClone`, `onCharacterItemSerialize`, `onCharacterItemDeserialize` | carry extra fields on `CharacterItem` through copying, saving and networking |

The three `CharacterItem` delegates are the supported way to add per-item data without changing the database schema.

## Mechanism 4: swapping a ScriptableObject service

`GameInstance` holds eight services as serialized fields. Each has a default created at `Awake` when the field is empty. To replace one, subclass it, create an asset, and drop the asset on `GameInstance` in the entry scene.

| Field | Base class | Controls |
|---|---|---|
| `gameplayRule` | `BaseGameplayRule` | damage, exp, gold, drop rates, level up, recovery. The highest-value swap. |
| `inventoryManager` | `BaseInventoryManager` | how items enter and leave inventories |
| `saveSystem` | `BaseGameSaveSystem` | LAN and offline persistence |
| `gmCommands` | `BaseGMCommands` | chat commands and their permissions |
| `messageManager` | `BaseMessageManager` | how game messages are formatted |
| `dayNightTimeUpdater` | `BaseDayNightTimeUpdater` | world time |
| `equipmentModelBonesSetupManager` | `BaseEquipmentModelBonesSetupManager` | how equipment binds to bones |
| `networkSetting` | `NetworkSetting` | transport tuning |

## Mechanism 5: swapping a handler interface

`GameInstance` exposes 24 static handler interfaces, twelve client-side and twelve server-side, covering inventory, character, party, guild, storage, mail, chat, cash shop, gacha, friends, bank and user content. The active network manager assigns them in its `_FeatureHandlers` partial. LAN and MMO already swap different implementations into the same slots, which is exactly the seam you would use to change a whole feature's server logic.

To override one, implement the interface and assign it after the network manager has set its defaults, from a `[DevExtMethods("OnStartServer")]` hook on the network manager. This is a large surface, so prefer mechanisms 1 to 4 unless you genuinely need to replace a feature wholesale.

## Mechanism 6: subclassing

For classes the kit instantiates from a prefab reference, subclassing is cleaner than hooks because you get normal `override`. This project does it once, in `Assets/TopDownController/Scripts/TopDownAimController.cs`, which extends `PlayerCharacterController` to add cursor aiming and is selected simply by pointing `GameInstance.defaultControllerPrefab` at a prefab carrying the subclass.

The same pattern applies to data classes. A new item type is a subclass of `BaseItem` with a `CreateAssetMenu` attribute; a new NPC dialog condition is a subclass of `BaseCustomNpcDialogCondition`; a new quest objective is a subclass of `BaseCustomQuestTask`.

## Mechanism 7: side components on your own prefabs

If you only need to observe or adjust something each frame, a plain `MonoBehaviour` on your prefab that reads public kit API is the lightest option. This project has two, both on `SyntyPlayerCharacter.prefab`: `LocomotionPhaseSync` and `ActionLayerMaskUpdater`, in `Assets/Scripts/Gameplay/`.

Both depend on kit members staying public. That is a deliberate trade: if a kit update hides them, compilation breaks loudly rather than the behaviour failing silently.

## Last resort: patching the kit

Sometimes there is no hook. This project has hit that twice, both in `Core/CameraAndInput/`, both documented in `CHANGELOG.md` and in the divergence index. If you must patch:

1. Confirm no mechanism above works. Check for a `virtual` method, a `protected` field, or an existing hook first.
2. Keep the change as small as possible.
3. Log it in `CHANGELOG.md` with the mechanism that forced it, and mark it in bold as an edit to a stock kit file.
4. Add it to the table in `Documentation/Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md`, which is the recovery checklist after a kit update.

## Worked example: a persistent per-character counter

Goal: count how many times a character has died, keep it across sessions, and show it on the client. This combines a hook, an event, custom character data and the existing persistence, without touching the kit or the database schema.

The kit stores arbitrary named values per character in three visibilities: **public** (synced to everyone, use for things other players see), **private** (synced to the owner only), and **server** (never leaves the server). Each comes in boolean, int and float variants, and all of them already persist to their own SQL tables.

```csharp
// Assets/Scripts/Gameplay/PlayerCharacterEntity_DeathCount.cs
using Insthync.DevExtension;

namespace MultiplayerARPG
{
    public partial class PlayerCharacterEntity
    {
        // Any stable int. Reuse the kit's hashing so it cannot collide by accident.
        private static readonly int DeathCountKey = "DEATH_COUNT".GenerateHashId();

        [DevExtMethods("Awake")]
        protected void DeathCount_Awake()
        {
            onKilled += DeathCount_OnKilled;
        }

        [DevExtMethods("OnDestroy")]
        protected void DeathCount_OnDestroy()
        {
            onKilled -= DeathCount_OnKilled;
        }

        private void DeathCount_OnKilled(BaseCharacterEntity target, EntityInfo instigator)
        {
            // Server owns the number. Clients receive it through the synced list.
            if (!IsServer)
                return;
            this.SetPrivateInt32(DeathCountKey, this.GetPrivateInt32(DeathCountKey) + 1);
        }

        public int DeathCount => this.GetPrivateInt32(DeathCountKey);
    }
}
```

What you did not have to do: add a database column, write a migration, register a network message, or edit a single kit file. Persistence and replication come from the custom data system, which already has its own tables and its own synced lists. Choose `Public` instead of `Private` if other players should see the value, or `Server` if it should never reach any client.

Two things to know before relying on this. `GetPrivateInt32` and its siblings are extension methods on `IPlayerCharacterData`, defined in `Core/SharedData/Scripts/CharacterData/PlayerCharacterDataExtensions.cs`, and their bodies are wrapped in `#if !DISABLE_CUSTOM_CHARACTER_DATA`. That symbol is **not** defined in this project, so they work, but if anyone ever defines it the calls compile to no-ops that silently return the default. And the key is an `int`, so hash a distinctive string once into a `static readonly` field rather than picking a number by hand.

For UI, read `DeathCount` from a component on your own canvas, and refresh it from the matching `onPrivateIntsOperation` event so it updates when the value changes.

## Regenerating the tables in this document

These tables are mechanical, which is why they are worth keeping. Regenerate rather than hand-edit after a kit update.

Every hook name and the class that invokes it:

```bash
grep -rn 'InvokeInstanceDevExtMethods\|InvokeStaticDevExtMethods' Assets --include=*.cs
```

Every entity event:

```bash
grep -rhn 'public event' Assets/UnityMultiplayerARPG/Core/Scripts/Gameplay --include=*.cs
```

The swappable services, in the `Gameplay Systems` block of `Core/Scripts/GameInstance/GameInstance.cs`, and the handler interfaces:

```bash
grep -E 'public static I(Client|Server)[A-Za-z]+Handlers' \
  Assets/UnityMultiplayerARPG/Core/Scripts/GameInstance/GameInstance.cs
```

A delegate signature, when you need the exact parameters:

```bash
grep -n -A12 'delegate void ReceivedDamageDelegate' \
  Assets/UnityMultiplayerARPG/Core/Scripts/Gameplay/GameplayDelegates.cs
```

## Related

- `CLAUDE.md`, section "Adding functionality", for the short version.
- [Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md](Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md) for what this project has already extended and patched.
- [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md) section 10 for where each system lives in the source.
- `CHANGELOG.md` for why past extensions were done the way they were.
