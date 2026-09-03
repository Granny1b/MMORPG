# Camera Shake — Design

**Status:** design only, nothing implemented. Written 2026-09-03.

## Purpose

Two things are wanted from a shake system, and they have almost nothing in common:

1. **A local shake anyone can call.** `CameraShake.Play(profile)` from a hit reaction, a landing, a
   UI beat, a debug key. No networking, no authority, no ceremony.
2. **A server-decided shake.** A boss stomps, a barrel explodes, a gate slams — the server owns the
   decision and every player who can see it should feel it, with the players who can't see it left
   alone.

The finding that shapes this whole document is that **(2) mostly does not need new networking.**
The kit already replicates the boss's skill to every observing client and every observing client
already spawns the skill's effect prefabs locally. Hanging the shake on the effect prefab makes it
server-decided for free. An explicit RPC is a second tier, needed only for shakes that are not
attached to an effect.

The other finding is project-specific and unpleasant: **this game aims with the cursor, and the
cursor aim is a ray through the gameplay camera.** Shaking that camera shakes the aim. That is
solvable, but it is the thing to prove before building anything else.

Every claim below carries a `file:line` citation against the source in this repository so it can be
re-checked after a kit update. Paths are relative to `Assets/UnityMultiplayerARPG/Core/` unless
stated otherwise.

## Scope

Inside this document:

- How the gameplay camera pose is produced, and the one place a shake can be applied without losing
  the fight against it.
- Why cursor aiming makes camera shake a gameplay problem here and not just a visual one.
- The local API, and the single guard that makes the same call site safe on a headless server.
- Three tiers of server-decided shake, ranked by cost, with the wire-compatibility trap in the
  cheap-looking one.
- The profile asset, and why it should not be registered in `GameDatabase_G`.
- Gotchas found in the source, and a build order that proves the risky assumptions first.

Outside this document:

- The extension mechanisms in general: `Documentation/EXTENDING.md`.
- Encounter content — which boss, which ability, how hard it should hit. Content decisions.
- Boss phase scripting, which is where a scripted (non-skill) shake would be triggered from:
  `Documentation/Systems/03_BOSS_ENCOUNTER_DESIGN.md`.

## Part 1 — Where a shake can be applied

### The camera pose is rewritten from scratch every frame

`FollowCamera.UpdateCamera` ends by assigning the pose absolutely:

```csharp
CacheCameraTransform.position = wantedPosition;   // CameraAndInput/Scripts/Camera/FollowCamera.cs:207
CacheCameraTransform.rotation = wantedRotation;   // :208
```

It runs from `LateUpdate` (`FollowCamera.cs:100`), and both `FollowCamera` (`:7`) and
`FollowCameraControls` (`FollowCameraControls.cs:8`) are marked
`[DefaultExecutionOrder(int.MinValue)]`.

Two consequences, and they point the same way:

- **You cannot shake the camera from anything that runs earlier.** `Update`, `FixedUpdate`, any
  `LateUpdate` on a component with a lower execution order — all of it is overwritten in the same
  frame, silently.
- **You do not have to save and restore an original pose.** A component at the default execution
  order (0, which is greater than `int.MinValue`) applying an additive offset in its own
  `LateUpdate` receives a clean, freshly computed base pose every frame. The offset cannot
  accumulate, and there is nothing to reset when the shake ends.

So the shape of the answer is fixed: **a component on the camera prefab root, default execution
order, adding an offset in `LateUpdate`.**

### The kit already does this once, for recoil

`FollowCameraControls.LateUpdate` calls `base.LateUpdate()` and then adds a euler offset on top
(`:212-221`), fed by `public void Recoil(float x, float y, float z)` (`:207`).

That is the proof the approach works. It is also not the thing to reuse:

- It is rotation-only (`:221`), so no positional kick.
- It is a spring toward zero (`:219-220`), not a windowed envelope. A shake wants "0.4 s, this
  curve, then exactly nothing"; recoil wants "push, drift back". Forcing one into the other loses
  the curve.
- There is one accumulator and one amplitude, with no notion of separate sources or priorities.
- `ShooterRecoilUpdater.cs:271` already calls it for weapon recoil. Sharing it means gun recoil and
  a boss stomp fight over the same variable.

Add a separate component. Recoil keeps its accumulator, shake keeps its own, and they sum naturally
because both are additive offsets on a pose that is rebuilt each frame.

### Rejected: a shake rig

The obvious alternative — parent the camera under a "shake pivot" and move the pivot, so the
camera's own transform stays clean — **does not work here**, and the reason is worth recording
because it looks like it should.

`FollowCamera.CacheCameraTransform` *is* the camera's own transform (`FollowCamera.cs:45`, assigned
from `CacheCamera.transform` at `:79`), and the pose is written in **world** space (`:207-208`). A
parent offset is therefore ignored: whatever the pivot does, the camera lands on `wantedPosition`
anyway. Moving the `Camera` component down onto a child and pointing `FollowCamera.targetCamera` at
it just relocates the problem — `CacheCameraTransform` becomes the child, and the child gets written
in world space instead.

There is no clean transform to hide behind. Post-processing the pose after `FollowCamera` has
written it is the mechanism, not a workaround for one.

### Our camera prefab, and why the UI camera is already handled

`DefaultGameplayCameraController.Init` instantiates `gameplayCameraPrefab` (`Scripts/Gameplay/
CharacterControllerSystems/Default/DefaultGameplayCameraController.cs:35`) and destroys the instance
with the controller (`:62`). For us that prefab is
`Assets/TopDownController/Demo/Prefabs/TopDownGameplayCamera.prefab` — **ours**, so the shaker
component goes on it with no kit prefab fork.

Its root carries `Camera`, `FollowCameraControls`, `SimpleFade` and URP's additional camera data. A
child `CharacterUICamera` sits at local position zero with its own `Camera` and a `CopyCamera`
(`Scripts/Utils/CopyCamera.cs`), which copies **lens properties only** — orthographic size, clip
planes, FOV, rect, physical camera settings — and never the transform.

That matters: because the UI camera is a child at local zero and inherits the transform rather than
copying it, shaking the root moves both cameras in lockstep. Nameplates and world-space UI stay
welded to the world instead of sliding across it. Nothing extra to build.

**Do not put the shaker on the minimap camera.** `PlayerCharacterController.minimapCameraPrefab`
(`:33`, used at `:174`) is a second `FollowCameraControls` instance. A shaking minimap is nauseating
and reads as a bug, and its render texture is one of the demo assets we have already patched.

## Part 2 — The cursor-aim wobble (accepted, 2026-09-03)

**Decision: the wobble is accepted and will not be corrected.** This section stays because the
mechanism is worth knowing — a character that twitches during a stomp is a plausible bug report, and
the next person to see it should find it documented as intentional rather than go hunting.

`TopDownAimController.TryGetCursorWorldPosition` builds the aim ray straight through the gameplay
camera:

```csharp
Ray ray = camera.ScreenPointToRay(InputManager.MousePosition());
// Assets/TopDownController/Scripts/TopDownAimController.cs:216
```

and the point it returns drives `PlayingCharacterEntity.SetLookRotation(...)` (`:200`), feeds
`CurrentAimPosition`, and is what `RedirectPendingActionToCursor` points attacks and skills at.

So shaking the camera transform shakes the aim. With the mouse perfectly still, the character's
facing wobbles — and facing is replicated, so other players see it too. The kit's own cursor pick
has the same exposure (`Scripts/Gameplay/CharacterControllerSystems/Default/
PlayerCharacterController_Inputs.cs:32`).

It is not a theoretical frame-ordering nicety either. The shake lands in `LateUpdate`; the aim is
computed in `Update`. The aim therefore reads **the previous frame's shaken pose** — a real,
one-frame-delayed wobble on every frame of every shake.

**Why it is being lived with rather than fixed.** The available fix — have the shaker cache the
pre-shake pose and have `TopDownAimController.TryGetCursorWorldPosition` build its ray from that —
works and costs little, but it buys precision nobody asked for: the wobble is bounded by the shake
amplitude, which is degrees and centimetres, and it only exists while a shake is running. Keeping
the aim honest during a shake is arguably worse anyway, since the whole point of the effect is that
the world lurches.

Two consequences to keep in mind rather than fix:

- **Keep amplitudes modest.** The wobble scales with the shake, so this decision is only cheap while
  the numbers stay in the ranges in Part 5. A 5° shake would make the character visibly spin.
- **Facing is replicated,** so other players see the twitch too. It is not a local-only artifact.

If it ever does become a problem, the unshaken-pose fix above is the answer and needs no kit edit.
The kit's own click-to-pick raycast (`PlayerCharacterController_Inputs.cs:32`) could not be fixed
that way regardless, since it lives in `Core/`.

## Part 3 — The local API

Two entry points and one component, in `Assets/Scripts/Gameplay/CameraShake/`:

```csharp
CameraShake.Play(profile, scale = 1f);                       // screen-space, no falloff
CameraShake.PlayAt(profile, worldPosition, scale = 1f);      // attenuated by distance to the camera target
```

Both resolve `CameraShaker.Current` — a static set in the shaker's `OnEnable` and cleared in
`OnDisable`, which is what makes it survive `DefaultGameplayCameraController`'s destroy-and-recreate
cycle (`:35`, `:62`) — and **both no-op when it is null.**

That single null check is the load-bearing part of the design. Null is exactly the state on a
headless server, during scene load, and before a character is spawned. It means **no call site
anywhere ever needs an `IsClient` guard**, which in turn is what lets the same method be called from
code that runs on both sides. Every alternative (guarding at each call site, `#if !UNITY_SERVER`
around callers, an interface with a null implementation) costs more and fails open.

A `CameraShakeEmitter : MonoBehaviour` wraps the same calls as inspector-callable methods
(`PlayAtSelf()`, `PlayScreenSpace()`). It exists so tier 1 below needs no code at all.

## Part 4 — Server-decided shake, in three tiers

### Tier 1 — no new networking (the recommended default)

**Put a `CameraShakeEmitter` on the skill's effect prefab and wire the prefab's `onGetInstance`
event to it.** That is the entire implementation.

The chain that makes it work is already built:

1. The server decides the boss uses a skill.
2. `DefaultCharacterUseSkillComponent` replicates it: `[AllRpc] RpcUseSkill(...)`
   (`Scripts/Gameplay/CharacterSystems/CharacterActionsSystem/DefaultCharacterUseSkillComponent.cs:518-519`),
   sent on `BaseGameEntity.ACTION_DATA_CHANNEL` (`Scripts/Gameplay/BaseGameEntity.cs:16`).
3. Every receiving client runs `ProceedUseSkill`, which instantiates `skill.SkillActivateEffects`
   through `GameEntityModel.InstantiateEffect` (`Scripts/GameData/Model/GameEntityModel.cs:263`,
   spawning at `:279`; call sites at `DefaultCharacterUseSkillComponent.cs:276`, `:285`, `:292`).
4. `PoolSystem.GetInstance` fires the pooled object's `OnGetInstance` (`Scripts/Gameplay/PoolSystem/
   PoolDescriptor.cs:24`), which invokes the serialized `onGetInstance` UnityEvent (`:16`).

Why this is the right default and not a shortcut:

- **The interest filter is already correct.** `RPCReceivers.All` sends only to connections where
  `Identity.HasSubscriberOrIsOwning(connectionId)` holds (`LiteNetLibManager/Scripts/GameApi/
  LiteNetLibRPC.cs:86`), and the default visible range is 80 m (`LiteNetLibManager/Scripts/GameApi/
  BaseInterestManager.cs:10`, resolved at `:58`). Players who cannot see the boss are already
  excluded, without writing a distance check.
- **Zero wire cost and zero wire-compatibility risk**, because no new message exists.
- **It behaves identically in LAN host, LAN dedicated and MMO**, because it is not a new code path
  in any of them.
- **The amplitude lives where it is tuned.** Shake strength belongs next to the dust plume and the
  impact sound, on the same prefab, adjustable by whoever is tuning the effect.

- **It costs nothing on a dedicated server.** Skill effect instantiation sits inside `if (IsClient)`
  (`DefaultCharacterUseSkillComponent.cs:271`), and hit effects behind `if (!IsClient) return;`
  (`Scripts/Gameplay/DamageableEntity.cs:255`). The server never builds the effect, so it never
  reaches the shake.

**The "area" is client-side, and that is the point.** `PlayAt` is one distance test per client
against its own player; there is no server-side area query and no per-player message. Measure from
the camera's **follow target**, not the camera position — the camera sits back from the target by
`zoomDistance` (`FollowCamera.cs:183`), so measuring from it biases the falloff by how far the
player happens to be zoomed out.

#### Which anchor, and therefore where the shake originates

This is the part that decides whether "within an area" means what you want, because the three effect
paths anchor in three different places:

| Anchor | Path | Origin of the shake | Right for |
|---|---|---|---|
| **The caster** | `SkillCastEffects` / `SkillActivateEffects` | the boss's model socket | a stomp, a roar, a slam — anything originating at the caster |
| **The impact point** | the skill's `AreaDamageEntity` prefab, network-spawned at `aimPosition.position` (`GameData/Skill/SimpleAreaAttackSkill.cs:148-153`) | where the ability lands | a meteor, a ground AoE, anything aimed away from the caster |
| **The victim** | `DamageableEntity.PlayHitEffects`, driven by `[AllRpc] RpcAppendCombatText` (`:236`, spawning at `:303`) | whoever got hit | "you personally took a big hit" |

Skill effects are **caster-anchored, not world-anchored**: `InstantiateEffect` requires a non-empty
`effectSocket`, looks it up in the model's `CacheEffectContainers`, spawns at that container's
transform and sets `FollowingTarget` to it (`GameEntityModel.cs:269-280`). So a stomp effect on the
boss shakes outward from the boss — correct — but a meteor's activate effect would shake outward
from the *caster*, which is wrong. Use the area entity for those.

**Timing:** `SkillActivateEffects` spawn after the cast delay but **before** the action animation
plays (`DefaultCharacterUseSkillComponent.cs:270-297`), so they lead the impact by the length of the
swing. For a stomp that reads as shaking before the foot lands. Either give the profile a start
delay, or anchor to the area entity, which spawns at the damage trigger instead.

#### The spawn hook is reliable for `GameEffect` and *not* for networked prefabs

This is the trap in tier 1, and it is invisible until a pool runs dry:

- **`GameEffect` → reliable.** `PoolSystem.GetInstance` calls `OnGetInstance()` after the if/else, so
  it fires on both the dequeue and the fresh-instantiate branch (`PoolSystem.cs:108`), and an
  uninitialised pool is routed through `InitPool` and a recursive call (`:113-114`). Every spawn,
  every time. Wire the prefab's `onGetInstance` UnityEvent and it just works.
- **A networked prefab → not reliable.** `LiteNetLibAssets.GetObjectInstance` calls `OnGetInstance()`
  **only on the pooled-dequeue branch** (`:324`); the fresh-`Instantiate` branch returns without it
  (`:331-334`), and `disablePooling` skips pooling altogether (`:274`, `:286`). So
  `Identity.onGetInstance` silently misses any spawn beyond `PoolingSize` (`:302`). That is fine for
  what the kit uses it for — resetting pooled state, which a fresh instance does not need
  (`AreaDamageEntity.cs:33`) — and wrong for firing an effect.
- **`OnEnable` is not the fix.** Pool pre-warm instantiates each instance *active* and then
  deactivates it (`:304-306`), so every pooled instance fires one spurious `OnEnable` at startup —
  `PoolingSize` phantom shakes on load. And there is nothing to guard on, because `NetworkSpawn`
  calls `SetActive(true)` (`:465`) *before* `Initial(...)` assigns the object id (`:466`).
- **Use `OnStartClient`.** `LiteNetLibBehaviour.OnStartClient()` is invoked once per network spawn and
  only when `Manager.IsClient` (`LiteNetLibAssets.cs:472-473`, dispatched at
  `LiteNetLibIdentity.cs:642-644`). The behaviour-index warning in tier 2 applies, but it is about
  *existing* prefabs: on a new `AreaDamageEntity` variant authored in `Assets/1. Data/` there is no
  deployed build to stay compatible with — only a standing rule never to reorder its components
  afterwards.

Where it runs out:

- Shakes for things that are not skills or effects — a phase transition, a scripted gate, an enrage.
- A shake whose parameters the server computes at runtime (scaled by remaining HP, by raid size).
- A shake that must outlive or precede the effect it would be attached to.

Those are tier 2.

### Tier 2 — an explicit `[AllRpc]`, added to an existing behaviour

```csharp
// Assets/Scripts/Gameplay/CameraShake/BaseGameEntity_CameraShake.cs
namespace MultiplayerARPG
{
    public partial class BaseGameEntity
    {
        public void CallRpcCameraShake(int profileId, float scale)
        {
            RPC(RpcCameraShake, Identity.DefaultRpcChannelId,
                DeliveryMethod.ReliableUnordered, profileId, scale);
        }

        [AllRpc]
        protected void RpcCameraShake(int profileId, float scale)
        {
            // Also invoked on the server - see the note below. CameraShake.PlayAt no-ops there.
            if (CameraShakeProfileSet.TryGet(profileId, out CameraShakeProfile profile))
                CameraShake.PlayAt(profile, EntityTransform.position, scale);
        }
    }
}
```

This follows the kit's own `CallRpcX` / `[AllRpc] RpcX` pairing exactly — compare
`Scripts/Gameplay/BaseGameEntity_AnimationFunctions.cs:9-17` — and it needs no kit edit, because
`BaseGameEntity` is `partial` and everything compiles into `Assembly-CSharp`.

**Add it to an existing behaviour, never as a new `LiteNetLibBehaviour` on the boss prefab.** An RPC
is addressed by `LiteNetLibIdentity.GetHashedId(TypeFullName + "_" + behaviourIndex + "_" +
methodName)` (`LiteNetLibManager/Scripts/GameApi/LiteNetLibBehaviour.cs:1193-1207`), and
`behaviourIndex` is a position in `GetComponentsInChildren<LiteNetLibBehaviour>()`
(`LiteNetLibManager/Scripts/GameApi/LiteNetLibIdentity.cs:584`). Adding a networked component to a
prefab root therefore shifts the index of every behaviour that sorts after it — including all
behaviours on child objects — which silently re-hashes their RPC **and sync-element** ids. A client
and server built from the same prefab still agree, so it appears to work; the moment builds differ,
RPCs on the shifted behaviours dispatch to nothing. Extending an existing class costs one file and
moves no index.

**Delivery method:** `ReliableUnordered`. A shake that is simply dropped for one player during a
boss cast reads as a bug, so not `Unreliable`; ordering against other shakes is meaningless, so not
`ReliableOrdered`; and it should not queue behind the ordered action channel, so not
`ACTION_DATA_CHANNEL`.

**The handler runs on the server as well as on clients.** In the `RPCReceivers.All` branch, the
host's own connection is dispatched by direct `HookCallback()` rather than a packet
(`LiteNetLibRPC.cs:78-83`), and a dedicated server invokes the callback too because of
`if (!manager.IsClientConnected) HookCallback();` (`:95-97`). The `CameraShaker.Current == null`
no-op covers it, but the comment above is worth keeping — without it the guard looks like dead code
and someone will "clean it up".

**`[AllRpc]` reaches current subscribers only.** A player whose interest area arrives 50 ms into the
stomp gets nothing. That is the correct behaviour for a shake, and there is no "shake with nobody
watching" case to handle either: an entity with zero subscribers does not run its AI at all
(`Scripts/Gameplay/CharacterSystems/MonsterCharacterSystems/MonsterActivityComponent.cs:148`,
`Scripts/Gameplay/BaseGameEntity.cs:176`).

### Tier 3 — a map-wide message, for events with no entity

For a scripted world event that no entity owns — an earthquake, a siege impact, a zone-wide cue.
An RPC needs an identity to hang off; a manager message does not.

- `public partial class GameNetworkingConsts { public const ushort CameraShake = 200; }` — the class
  is `partial` (`Scripts/Networking/GameNetworkingConsts.cs:3`). Kit message ids currently stop at
  122 and request ids at 194; the two id spaces are separate, but picking a number above both is the
  cheapest thing to reason about later.
- Register the client handler from `[DevExtMethods("RegisterClientMessages")]` on
  `BaseGameNetworkManager`, invoked at `Scripts/Networking/BaseGameNetworkManager.cs:180`.
- Send with `ServerSendPacketToAllConnections` (`LiteNetLibManager/Scripts/LiteNetLibManager.cs:444`)
  for a whole-map shake, or iterate `GameInstance.ServerUserHandlers.GetPlayerCharacters()`
  (`Scripts/Networking/Interfaces/IServerUserHandlers.cs:20`, used at `BaseGameNetworkManager.cs:437`)
  and send per-connection with `ServerSendPacket` (`LiteNetLibManager.cs:367`) to filter by distance
  server-side.

Build this only when a world event actually needs it. Tiers 1 and 2 cover bosses.

## Part 5 — The profile asset

`CameraShakeProfile : ScriptableObject`, `[CreateAssetMenu]`, stored under
`Assets/1. Data/GameData/CameraShakeProfiles/`:

| Field | Why |
|---|---|
| `duration` | Total length. |
| `envelope` (`AnimationCurve`, 1 → 0) | One curve replaces attack/sustain/decay parameters, and is what a designer actually reaches for. |
| `positionAmplitude`, `rotationAmplitude` | Metres and degrees. Rotation sells a stomp; position sells an explosion. |
| `frequency` | Rumble (low) versus snap (high). |
| `noiseMode` | Perlin for a sustained rumble, decaying impulse for a single kick. |
| `innerRadius`, `outerRadius`, `falloff` | Distance attenuation for `PlayAt`. Full strength inside, zero outside, curve between. |
| `ignoreDistance` | For shakes that are about the viewer, not the world (taking a hit, a UI beat). |

Sample Perlin noise with a **different seed per axis** (`Mathf.PerlinNoise(seed_axis + t * frequency,
k)`), or the three axes correlate and the shake becomes a diagonal slide rather than a shake.

### Deciding the radius

Two radii, not one. A single "30 m" gives a shake that is already fading at the epicentre, because
the falloff starts the moment you step off the exact spawn point.

- **`innerRadius`** — full strength inside it. Set it to roughly the ability's visual footprint, so
  everyone standing *in* the stomp feels the same stomp. For a boss stomp, 5–8 m.
- **`outerRadius`** — zero beyond it. **This is the "30 m".**
- **`falloff`** (`AnimationCurve`, 1 at t=0 → 0 at t=1, evaluated on the normalised distance between
  the two radii) — a curve rather than a formula, because both obvious formulas feel wrong. Linear
  makes the boundary noticeable as it sweeps past you; true inverse-square puts almost everything
  into the first few metres and wastes the other 25. Start slightly convex — around 0.5 at the
  midpoint — and tune by walking away from a test emitter.

**Hard ceiling: `outerRadius` cannot usefully exceed the source entity's network visible range.**
The effect only exists on clients subscribed to the boss — 80 m by default
(`BaseInterestManager.cs:10`, resolved at `:58`), overridable per prefab
(`LiteNetLibIdentity.cs:55-56`, `:172`). Set `outerRadius` to 100 m and every player between 80 and
100 m silently feels nothing, because they never received the effect at all: the curve claims a
falloff that the network already truncated. Keep `outerRadius` comfortably under the boss's visible
range, or raise that entity's `visibleRange` deliberately and knowingly. At 30 m this is not a
concern; it becomes one the moment someone wants a zone-wide rumble, which is what tier 3 is for.

### Deciding the magnitude

Two amplitudes, because they read as different things:

- **`rotationAmplitude`** (degrees) — the one that sells weight. Rotation swings the whole frame, so
  small numbers read big. Around **0.5–1.5°** is already a lot.
- **`positionAmplitude`** (metres) — reads as a physical jolt. At this game's top-down zoom, about
  **0.1–0.25 m** is clearly visible and 0.5 m is cartoonish.
- **`frequency`** (Hz) — **15–25** for a sharp impact, **4–8** for a rumble.
- **`duration` + `envelope`** — a stomp is roughly **0.3–0.5 s**, instant attack, exponential decay.
  The envelope is what stops it feeling like a vibrating phone.

These are **starting points to tune from, not measurements** — nothing here has been sampled against
the real camera yet. Re-derive them the first time a profile is authored, and record what you land
on in `CHANGELOG.md`.

Rotation amplitude is also the number the Part 2 decision constrains: the character's aim wobbles
with it, and that is only tolerable while it stays in the range above.

### How the numbers combine

The answer to "how strong is this shake, for this player, right now" is one multiplication chain:

```
amplitude = profile.positionAmplitude / rotationAmplitude   // the profile's absolute size
          × profile.envelope.Evaluate(elapsed / duration)   // shape over the shake's lifetime
          × falloff(distanceAtSpawn)                        // how far away THIS client was
          × emitter.scale                                   // per-prefab trim, default 1
          × playerScreenShakeScale                          // accessibility setting, 0 disables
```

Every term after the first is 0–1. That is the useful property: **tune the profile once for the
"standing at the epicentre with the setting at 100%" case**, and every other case falls out of it —
no per-caller magic numbers, and the accessibility slider cannot be bypassed by any call path.

`emitter.scale` exists so one `BossStomp_Heavy` profile can serve a small add and a raid boss without
forking the asset. Reach for a second profile only when the *shape* differs, not the size.

### Distance is sampled once, at spawn

`PlayAt` computes the falloff weight when the effect spawns and stores the result; it does not track
the player afterwards. Running out of the radius mid-shake does not cut it short, and running in does
not start one. That is both cheaper (one distance test per client per effect, no per-frame work) and
better behaved — a shake whose strength changed while you moved would read as a bug.

It also means the caster-following behaviour of skill effects (Part 4) is irrelevant to the shake:
the effect may chase the boss's socket, but the weight was fixed at spawn.

### The two methods that implement all of the above

```csharp
// CameraShake.cs
public static void PlayAt(CameraShakeProfile profile, Vector3 worldPosition, float scale = 1f)
{
    CameraShaker shaker = CameraShaker.Current;
    if (shaker == null || profile == null)
        return;                       // headless server, scene load, no character yet

    float weight = profile.EvaluateFalloff(
        Vector3.Distance(shaker.FalloffOrigin, worldPosition));
    if (weight <= 0f)
        return;                       // outside outerRadius - nothing is queued at all

    shaker.Add(profile, scale * weight);
}

// CameraShakeProfile.cs
public float EvaluateFalloff(float distance)
{
    if (ignoreDistance)          return 1f;
    if (distance <= innerRadius) return 1f;
    if (distance >= outerRadius) return 0f;
    return Mathf.Clamp01(falloff.Evaluate(
        Mathf.InverseLerp(innerRadius, outerRadius, distance)));
}
```

`shaker.FalloffOrigin` is the camera's **follow target** — the player character — not the camera
itself, for the `zoomDistance` reason in Part 8.

### Authoring a 30 m boss stomp

1. Create `CameraShakeProfile` → `BossStomp_Heavy_G` in
   `Assets/1. Data/GameData/CameraShakeProfiles/`. **Set its `id` field explicitly**, do not rely on
   the file name (Part 5, "Do not register profiles").
2. `duration` 0.4, `rotationAmplitude` 1.0, `positionAmplitude` 0.15, `frequency` 18, envelope
   instant-attack/exponential-decay.
3. `innerRadius` 6, **`outerRadius` 30**, falloff curve ~0.5 at the midpoint.
4. On the stomp's `GameEffect` prefab: add `CameraShakeEmitter`, assign the profile, leave `scale`
   at 1.
5. On the same prefab, wire the `onGetInstance` UnityEvent → `CameraShakeEmitter.PlayAtSelf`.

No code, no ids, no networking. Every player within 30 m of the boss who can see the effect shakes,
scaled by their distance and their own accessibility setting; nobody else does.

### Do not register profiles in `GameDatabase_G`

`GameDatabase` holds a fixed set of typed arrays (`Scripts/GameData/Database/GameDatabase.cs:25-63`)
with no generic slot for a new data type. It *is* `partial` (`:18`) and it *does* invoke
`[DevExtMethods("LoadDataImplement")]` (`:136`) and `"LoadReferredData"` (`:279`), so a
`CameraShakeProfile[]` field plus a hook is genuinely available with no kit edit.

It is still the wrong choice. Only tiers 2 and 3 ever name a profile over the wire, and they can do
it more cheaply:

**Recommended:** one `CameraShakeProfileSet` ScriptableObject holding the array, loaded from
`GameInstance`'s `LoadedGameData` hook into a static dictionary keyed by `id.GenerateHashId()` — the
same hash `BaseGameData.MakeDataId` uses (`Scripts/GameData/BaseGameData.cs:180`, `:184`). Send the
hashed int. Nothing is added to the game database, and profiles stay out of the asset that governs
items, skills and quests.

**Rejected — sending raw parameters** (duration, amplitude, frequency) instead of an id. It removes
the registry, but it freezes tuning into whatever the server binary shipped with, puts
designer-tunable numbers on the wire, and grows the packet every time a profile gains a field.

**Rejected — sending an array index.** Stable only until somebody reorders the array. Hashed string
ids survive reordering; this is the same trap `CLAUDE.md` records for `DataId`, and it carries the
same remedy: **set `id` explicitly on every profile asset from day one**, so renaming the file does
not orphan anything.

## Part 6 — Blending, clamping and the player setting

**Sum, then clamp.** Concurrent shakes add naturally, which is correct — two stomps should feel like
more than one — but unbounded addition puts the camera through the floor. Clamp the summed
translation and rotation on the shaker, and cap the number of live instances (dropping the weakest)
so pulling twenty mobs does not turn into a per-frame list walk.

**A `screenShakeScale` 0–1 setting is a requirement, not a nicety.** Camera shake is a known
vestibular trigger; 0 must fully disable it. Apply the multiplier inside the shaker so every
path — tier 1, 2, 3 and local — respects it without each call site remembering to. Cache the value
rather than reading `PlayerPrefs` per frame; the same per-frame `PlayerPrefs` cost is exactly why
`FollowCameraControls.SaveCameraPrefs` was moved to `OnDisable`/`OnApplicationQuit` in this repo (see
`CLAUDE.md`). Key it distinctly, e.g. `GRANNY_ScreenShakeScale`, so it cannot collide with the kit's
`savePrefsPrefix + "_..."` camera keys.

The settings control belongs in our forked `Assets/1. Data/Prefabs/UI Prefabs/UIDialogs_G.prefab`.

## Part 7 — Recommended architecture

```
CameraShakeProfile              (ScriptableObject, Assets/1. Data/GameData/CameraShakeProfiles/)
  └─ duration, envelope, amplitudes, frequency, noise mode, radius falloff

CameraShakeProfileSet           (ScriptableObject, one asset)
  └─ loaded on GameInstance "LoadedGameData"; static id -> profile map, keyed by GenerateHashId

CameraShaker                    (MonoBehaviour on TopDownGameplayCamera.prefab root — ours)
  ├─ static Current, set OnEnable / cleared OnDisable
  ├─ LateUpdate at default order, i.e. after FollowCameraControls (int.MinValue)
  ├─ caches the pre-shake pose, then adds the summed offset
  └─ clamps the sum; applies the player's screenShakeScale

CameraShake                     (static facade, Assets/Scripts/Gameplay/CameraShake/)
  ├─ Play(profile, scale)                  screen-space
  ├─ PlayAt(profile, worldPos, scale)      distance attenuated
  └─ no-ops when CameraShaker.Current is null  → safe on a headless server, no IsClient guards

CameraShakeEmitter              (MonoBehaviour, inspector-callable)
  └─ goes on GameEffect prefabs; driven by the prefab's onGetInstance UnityEvent   ← tier 1

BaseGameEntity_CameraShake.cs   (partial class, [AllRpc])                          ← tier 2
GameNetworkingConsts.CameraShake + RegisterClientMessages hook                     ← tier 3

```

Why the split: the **shaker** owns per-frame state and must live on the camera, because that is the
only object whose lifetime matches the camera's. The **profile** owns tuning and must be an asset,
because tuning is not a code change. The **facade** exists so the null-camera guard is written once.
The **emitter** exists so tier 1 needs no code. Everything server-side names a profile by id and
sends nothing else.

## Part 8 — Gotchas verified in the source

- **Anything writing the camera pose before `FollowCamera` is discarded.** `FollowCamera` is
  `[DefaultExecutionOrder(int.MinValue)]` (`FollowCamera.cs:7`) and assigns position and rotation
  absolutely (`:207-208`). The corollary is the useful half: an offset applied after it cannot drift,
  and needs no reset.
- **A parent "shake pivot" does nothing.** The pose is written in world space onto the camera's own
  transform (`FollowCamera.cs:45`, `:79`, `:207-208`), so a parent transform is ignored entirely.
- **Camera shake shakes the aim.** `TopDownAimController.cs:216` builds the aim ray with
  `ScreenPointToRay`; the result sets the character's replicated facing (`:200`). Aim reads the
  previous frame's shaken pose, because the shake lands in `LateUpdate` and the aim is computed in
  `Update`.
- **`[AllRpc]` handlers run on the server too.** Host connections are dispatched by direct
  `HookCallback()` (`LiteNetLibRPC.cs:78-83`) and a dedicated server calls the handler as well
  (`:95-97`). Client-only presentation code must be a no-op there, not merely unlikely to run.
- **Adding a `LiteNetLibBehaviour` to a prefab renumbers the ones after it.** `behaviourIndex` is the
  index into `GetComponentsInChildren<LiteNetLibBehaviour>()` (`LiteNetLibIdentity.cs:584`) and is
  hashed into every RPC and sync-element id (`LiteNetLibBehaviour.cs:1193-1207`). Same-build
  client/server agree, so this fails only across mismatched builds — the worst way to find out.
- **`[AllRpc]` is subscribers-only, and interest is 80 m by default.**
  `Identity.HasSubscriberOrIsOwning` (`LiteNetLibRPC.cs:86`), `defaultVisibleRange = 80f`
  (`BaseInterestManager.cs:10`, `:58`). Correct filtering for free, and also a hard ceiling: nothing
  beyond that range can be shaken by an entity RPC. A zone-wide rumble needs tier 3.
- **`onGetInstance` fires on every spawn for `GameEffect` and not for networked prefabs.**
  `PoolSystem.GetInstance` calls it on both the dequeue and the fresh-instantiate branch
  (`PoolSystem.cs:108`); `LiteNetLibAssets.GetObjectInstance` calls it only when dequeuing (`:324`)
  and not when instantiating (`:331-334`). A shake wired to a networked prefab's event therefore
  works until the pool runs dry, which is exactly when the fight is busiest.
- **Skill effects anchor to the caster's socket, not to the world.** `InstantiateEffect` needs a
  non-empty `effectSocket`, resolves it against the model's containers, and sets `FollowingTarget`
  to it (`GameEntityModel.cs:269-280`). An ability that lands away from its caster needs the
  `AreaDamageEntity` as the shake origin instead (`SimpleAreaAttackSkill.cs:148-153`).
- **`SkillActivateEffects` lead the impact.** They spawn after the cast delay but before the action
  animation (`DefaultCharacterUseSkillComponent.cs:270-297`), so an un-delayed shake fires before
  the foot lands.
- **Measure falloff from the camera's follow target, not the camera.** The camera is offset back by
  `zoomDistance` (`FollowCamera.cs:183`), so distance from the camera makes the same explosion feel
  weaker the further a player zooms out.
- **The UI camera comes along for free.** `CharacterUICamera` is a child at local zero, and
  `CopyCamera` copies lens properties only, never the transform (`Scripts/Utils/CopyCamera.cs`).
  Shaking the root keeps world-space UI locked to the world.
- **The camera instance is destroyed and rebuilt with the controller.**
  `DefaultGameplayCameraController.cs:35` and `:62`. Any static pointing at the shaker must be
  cleared in `OnDisable`, or the second character of a session shakes a destroyed camera.
- **The MMO scene is not wired to our prefabs.** `00Init_MMO.unity` still uses the kit's
  `GameInstance.prefab` with the stock controller and camera (`CLAUDE.md`). A shaker installed on
  `TopDownGameplayCamera.prefab` therefore does nothing in MMO mode until that is fixed. Tiers 1–3
  are all unaffected on the server side; it is purely the receiving end that is missing.
- **Renaming a profile asset changes its id.** `Id` falls back to the asset name when `id` is empty
  (`BaseGameData.cs:30` for the kit's own types; the same convention should be copied here), and the
  hash is taken from that string (`:180`, `:184`). Set `id` explicitly on profiles from the start.

## Part 9 — Build order

Riskiest assumption first.

1. **`CameraShaker` + one profile + a debug key.** Confirms the `LateUpdate` ordering assumption
   against the real camera prefab. If the shake does not appear, every later step is built on sand.
2. **`PlayAt` with distance falloff**, plus the summed clamp and the instance cap. Test by walking
   away from a fixed emitter and watching the strength drop off.
3. **Tier 1 end to end.** `CameraShakeEmitter` on a monster skill's effect prefab, wired through
   `onGetInstance`. Test on a LAN host with two clients: both shake when in range, the far one does
   not. This is the step that proves "server-decided" needs no networking.
4. **The player setting**, wired to a slider in `UIDialogs_G.prefab`, with 0 fully disabling.
5. **Tier 2**, only once step 3's limits actually bite — a scripted phase cue with no effect prefab
   to hang on.
6. **Tier 3**, only for a real zone-wide event.

Steps 1–3 are half a day's work and cover the boss stomp. Steps 5 and 6 may never be needed.

The aim-decoupling step that used to sit second here was dropped on 2026-09-03: the wobble is
accepted (Part 2).

## Open decisions

- ~~**Is camera shake acceptable at all in a cursor-aim game?**~~ **Answered 2026-09-03:** yes, and
  the aim wobble it causes is accepted rather than corrected (Part 2). This removed a build step and
  the `UnshakenPosition`/`UnshakenRotation` requirement from the shaker.
- **Profile registry: standalone set, or a partial `GameDatabase` field?** Recommended standalone
  (Part 5). The `GameDatabase` route is available and would give the profiles a conventional
  `DataId`, at the cost of putting presentation tuning into the asset that governs items and skills.
- **Should any shake reach players who cannot see its source?** Tier 1 and 2 say no by construction,
  which is almost certainly right for a stomp and definitely wrong for a scripted zone event.
- **MMO parity.** Whether to fix `00Init_MMO.unity`'s prefab wiring as part of this work or leave
  shake as a LAN-only feature until that scene is addressed separately.

## Related

- `Documentation/EXTENDING.md` — the partial-class, DevExt and prefab-side mechanisms used above.
- `Documentation/Systems/03_BOSS_ENCOUNTER_DESIGN.md` — where a scripted, non-skill shake would be
  triggered from.
- `Documentation/Systems/00_PROJECT_CUSTOMIZATIONS_AND_KIT_DIVERGENCE.md` — ours versus vendored.
- `CLAUDE.md` — where new work goes, and why `Core/` must not be edited.
