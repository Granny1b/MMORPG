using System.Collections.Generic;
using Insthync.CameraAndInput;
using LiteNetLibManager;
using MultiplayerARPG.GameData.Model.Playables;
using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>
    /// Turns the kit's dash into a directional dodge roll: WASD picks the direction, the facing
    /// stays where it was (the aim), one of four Synty roll clips plays full-body, and the
    /// movement follows the authored root-motion curve so the feet match. Sits next to
    /// <c>CharacterControllerEntityMovement</c>, which collects every
    /// <see cref="IEntityMovementForceUpdateListener"/> on the same object at setup.
    ///
    /// Why not the kit's own dash animation: the kit dashes along facing, turns the character into
    /// the dash, and has one dash-start clip. Under a top-down aim controller the facing is the
    /// mouse, so every roll but a forward one needs a different clip and no turn. So the model's
    /// <c>dashStartState</c> is left empty and the clip is chosen here and played through
    /// <see cref="PlayableCharacterModel.PlayCustomAnimation"/> (indices below).
    ///
    /// Networking: only the local player has the movement input, so only it picks the direction and
    /// the clip. It plays the roll at once and broadcasts the choice with <see cref="RpcPlayRoll"/>,
    /// an <c>[AllRpc]</c> - hence <see cref="BaseNetworkedGameEntityComponent{T}"/> rather than a
    /// plain MonoBehaviour. Every other copy of the character plays nothing on its own and waits for
    /// that call, so observers see the same clip instead of always the forward roll. The server
    /// echoes an All-RPC back to its sender, so <see cref="StartRoll"/> ignores a repeat of the same
    /// clip inside <see cref="DuplicateWindow"/> and the local roll never restarts mid-animation.
    /// </summary>
    [RequireComponent(typeof(BaseCharacterEntity))]
    public class DirectionalRollDash : BaseNetworkedGameEntityComponent<BaseCharacterEntity>, IEntityMovementForceUpdateListener
    {
        /// <summary>An echo of our own broadcast arrives within a round trip; a second roll cannot start this soon.</summary>
        private const float DuplicateWindow = 0.3f;

        [Header("Cost")]
        [Tooltip("Seconds from the start of one roll before another may begin. Must exceed the roll's own length (1.167 s) or rolls can chain into each other.")]
        public float rollCooldown = 1.5f;

        [Tooltip("Stamina taken per roll, deducted by the server. The character pool is 100 and recovers 3/s, so 20 gives five rolls back to back and about 6.7 s to earn one back. Set to 0 for a free roll on cooldown alone.")]
        public int staminaCost = 20;

        [Tooltip("Refuse attacks for as long as the roll lasts. Cancelling the get-up early by moving ends the roll, and with it this block.")]
        public bool blockAttackWhileRolling = true;

        [Tooltip("Also refuse skills and skill items while rolling. Off by default, so only plain attacks are blocked; turn it on to close that gap.")]
        public bool blockSkillsWhileRolling = false;

        [Header("Distance")]
        [Tooltip("Ground distance the profile covers. The delivered distance is higher because speed is floored at walking pace through the slow middle - 4.98 here measures out at about 5.07 m on the ground.")]
        public float rollDistance = 4.98f;

        [Tooltip("Length of the roll clips. The stand-up lock and the facing lock run until this has elapsed since the roll began.")]
        public float rollDuration = 1.167f;

        [Tooltip("Normalized clip time -> normalized distance travelled. Baked from A_DodgeRoll_F_RootMotion_Sword's root curve; front-loaded, 80% of the travel is done at half the clip.")]
        public AnimationCurve distanceProfile = BuildLinear(
            new float[] { 0.00f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.30f, 0.35f, 0.40f, 0.45f, 0.50f, 0.55f, 0.60f, 0.65f, 0.70f, 0.75f, 0.80f, 0.85f, 0.90f, 0.95f, 1.00f },
            new float[] { 0.000f, 0.021f, 0.071f, 0.152f, 0.270f, 0.383f, 0.477f, 0.561f, 0.670f, 0.746f, 0.796f, 0.834f, 0.876f, 0.918f, 0.954f, 0.976f, 0.986f, 0.995f, 0.998f, 0.999f, 1.000f });

        [Tooltip("The kit drops a dash the moment its speed is under walking speed, which the authored roll is for most of its length (it averages 3.8 m/s). The speed is therefore never allowed below walking speed until this fraction of the profile's travel is done; then it is released and the stand-up lock takes over.")]
        [Range(0.5f, 1f)]
        public float releaseAtTravel = 0.9f;

        [Tooltip("Ignore movement input while the stand-up part of the clip plays, so the feet stay put.")]
        public bool lockMovementDuringTail = true;

        [Tooltip("Normalized clip time at which movement input is accepted again. The dash itself releases around 0.64; anything between that and 1 is stand-up. Lower = snappier, higher = the full get-up every time.")]
        [Range(0f, 1f)]
        public float movementUnlockAt = 0.7f;

        [Tooltip("Once unlocked, a held movement key stops the roll clip so the run blends in at once instead of waiting for the get-up to finish.")]
        public bool cancelClipWhenMoving = true;

        [Header("Animation")]
        [Tooltip("Keep the facing the roll started with (the aim) for the whole roll, instead of the kit turning the character into the dash.")]
        public bool keepFacing = true;

        [Tooltip("Indices into PlayableCharacterModel.customAnimations. Forward is used within 45 degrees of facing, backward beyond 135, left/right in between.")]
        public int forwardClipIndex = 0;
        public int backwardClipIndex = 1;
        public int leftClipIndex = 2;
        public int rightClipIndex = 3;

        /// <summary>True from the roll's first frame until <see cref="rollDuration"/> has elapsed, on every copy of the character.</summary>
        public bool IsRolling { get { return _rollStartTime >= 0f && Time.time < _rollStartTime + rollDuration; } }

        /// <summary>The copy of this character that the player on this machine is steering; the only one with movement input.</summary>
        private bool IsLocalPlayer
        {
            get
            {
                BasePlayerCharacterController controller = BasePlayerCharacterController.Singleton;
                return controller != null && controller.PlayingCharacterEntity == Entity;
            }
        }

        private PlayableCharacterModel _model;
        private float _rollStartTime = -1f;
        private float _lastDashTickTime = -1f;
        private float _lockedYaw;
        private int _clipIndex = -1;
        private float _cooldownUntil = -1f;

        private void Awake()
        {
            _model = GetComponent<PlayableCharacterModel>();
            Entity.onCanMoveValidated += OnCanMove;
            Entity.onCanDashValidated += OnCanDash;
            Entity.onCanAttackValidated += OnCanAttack;
            Entity.onCanUseSkillValidated += OnCanUseSkill;
            Entity.onCanUseSkillItemValidated += OnCanUseSkill;
        }

        protected override void OnDestroy()
        {
            if (Entity != null)
            {
                Entity.onCanMoveValidated -= OnCanMove;
                Entity.onCanDashValidated -= OnCanDash;
                Entity.onCanAttackValidated -= OnCanAttack;
                Entity.onCanUseSkillValidated -= OnCanUseSkill;
                Entity.onCanUseSkillItemValidated -= OnCanUseSkill;
            }
            base.OnDestroy();
        }

        /// <summary>
        /// Refuses the dash the kit would otherwise start, which is what stops roll spam. Runs on
        /// every copy: the local player blocks its own input, and the server blocks a client that
        /// asks anyway. Safe to answer false during a roll - the kit only reads this when starting
        /// one, and an in-flight roll is carried by its force applier, not by this flag.
        /// </summary>
        private void OnCanDash(BaseGameEntity entity, ref bool canDash)
        {
            if (Time.time < _cooldownUntil)
                canDash = false;
            else if (staminaCost > 0 && Entity.CurrentStamina < staminaCost)
                canDash = false;
        }

        /// <summary>
        /// No swinging mid-roll. The kit runs this validation on the server as well as the attacker,
        /// and <see cref="IsRolling"/> is true on every copy, so a client cannot attack by asking twice.
        /// </summary>
        private void OnCanAttack(BaseGameEntity entity, ref bool canAttack)
        {
            if (blockAttackWhileRolling && IsRolling)
                canAttack = false;
        }

        private void OnCanUseSkill(BaseGameEntity entity, ref bool canUseSkill)
        {
            if (blockSkillsWhileRolling && IsRolling)
                canUseSkill = false;
        }

        public void OnPreUpdateForces(IList<EntityMovementForceApplier> forceAppliers)
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < forceAppliers.Count; ++i)
            {
                EntityMovementForceApplier applier = forceAppliers[i];
                if (applier.Mode != ApplyMovementForceMode.Dash)
                    continue;
                _lastDashTickTime = Time.time;
                if (applier.Elasped <= 0f)
                    BeginRoll(applier);
                applier.CurrentSpeed = SpeedForTick(applier.Elasped, dt);
            }
        }

        public void OnPostUpdateForces(IList<EntityMovementForceApplier> forceAppliers)
        {
        }

        private void LateUpdate()
        {
            if (!IsRolling || !IsLocalPlayer)
                return;
            // Past the unlock point with a movement key held: cut the get-up short and let the run blend in.
            if (cancelClipWhenMoving && !IsDashAlive() && Time.time >= UnlockTime() && GetInputDirection().sqrMagnitude > 0.001f)
            {
                CancelRoll();
                return;
            }
            // The kit re-targets the yaw to the dash direction every movement tick; put the aim back
            // after it, before rendering, so the character rolls sideways or backwards without turning.
            if (keepFacing)
                Entity.SetLookRotation(Quaternion.Euler(0f, _lockedYaw, 0f), true);
        }

        private float UnlockTime()
        {
            return _rollStartTime + movementUnlockAt * rollDuration;
        }

        private bool IsDashAlive()
        {
            float tick = Mathf.Max(Time.deltaTime, Time.fixedDeltaTime) * 1.5f;
            return Time.time - _lastDashTickTime <= tick;
        }

        private void BeginRoll(EntityMovementForceApplier applier)
        {
            Vector3 facing = transform.forward;
            facing.y = 0f;
            facing = facing.sqrMagnitude > 0.001f ? facing.normalized : Vector3.forward;
            Vector3 direction = IsLocalPlayer ? GetInputDirection() : Vector3.zero;
            if (direction.sqrMagnitude <= 0.001f)
                direction = facing;

            applier.Direction = direction;
            applier.Deceleration = 0f;
            applier.Duration = rollDuration;

            // Only the copy holding the input decides; every other copy waits for the broadcast below,
            // otherwise it would guess "forward" and then be corrected a round trip later.
            if (!IsLocalPlayer)
                return;

            int index = ChooseClipIndex(facing, direction);
            StartRoll(index);
            if (IsServer || IsOwnerClient)
                RPC(RpcPlayRoll, (byte)index);
        }

        private int ChooseClipIndex(Vector3 facing, Vector3 direction)
        {
            float angle = Vector3.SignedAngle(facing, direction, Vector3.up); // positive = to the right
            float abs = Mathf.Abs(angle);
            return abs <= 45f ? forwardClipIndex : abs >= 135f ? backwardClipIndex : angle > 0f ? rightClipIndex : leftClipIndex;
        }

        /// <summary>Plays the roll and starts its clocks. Ignores the echo of our own broadcast.</summary>
        private void StartRoll(int index)
        {
            if (_clipIndex == index && _rollStartTime >= 0f && Time.time - _rollStartTime < DuplicateWindow)
                return;
            _rollStartTime = Time.time;
            _clipIndex = index;
            _lockedYaw = transform.eulerAngles.y;
            // Kept apart from _rollStartTime, which an early get-up cancel clears; the cooldown must not be cancelable.
            _cooldownUntil = Time.time + rollCooldown;
            // Stamina is a ServerToClients field, so only the server's write propagates. Every copy runs
            // this method, and the server's copy runs it too when it relays the broadcast, so it lands once.
            if (IsServer && staminaCost > 0)
                Entity.CurrentStamina = Mathf.Max(0, Entity.CurrentStamina - staminaCost);
            if (_model != null)
                _model.PlayCustomAnimation(index, false);
        }

        private void StopRollLocal()
        {
            if (_model != null)
                _model.StopCustomAnimation();
            _rollStartTime = -1f;
            _clipIndex = -1;
        }

        private void CancelRoll()
        {
            StopRollLocal();
            if (IsServer || IsOwnerClient)
                RPC(RpcStopRoll);
        }

        [AllRpc]
        private void RpcPlayRoll(byte clipIndex)
        {
            StartRoll(clipIndex);
        }

        [AllRpc]
        private void RpcStopRoll()
        {
            StopRollLocal();
        }

        private float SpeedForTick(float elapsed, float dt)
        {
            if (dt <= 0f || rollDuration <= 0f)
                return 0f;
            float t0 = Mathf.Clamp01(elapsed / rollDuration);
            float t1 = Mathf.Clamp01((elapsed + dt) / rollDuration);
            float done = distanceProfile.Evaluate(t0);
            if (done >= releaseAtTravel)
                return 0f; // released: the kit removes the applier, the tail lock holds the feet
            float travel = (distanceProfile.Evaluate(t1) - done) * rollDistance;
            float walking = Entity.GetMoveSpeed(MovementState.Forward, ExtraMovementState.None);
            return Mathf.Max(travel / dt, walking + 0.01f);
        }

        private Vector3 GetInputDirection()
        {
            PlayerCharacterController controller = BasePlayerCharacterController.Singleton as PlayerCharacterController;
            if (controller == null || controller.PlayingCharacterEntity != Entity)
                return Vector3.zero;
            float horizontal = InputManager.GetAxis("Horizontal", false);
            float vertical = InputManager.GetAxis("Vertical", false);
            Vector3 direction = controller.GetMoveDirection(horizontal, vertical);
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.zero;
        }

        private void OnCanMove(BaseGameEntity entity, ref bool canMove)
        {
            // Remote copies are positioned by the network, so never hold their movement back here.
            if (!lockMovementDuringTail || _rollStartTime < 0f || !IsLocalPlayer)
                return;
            // Dash still alive (seen within the last tick or so)? Leave it alone: the kit cancels a dash whose entity cannot move.
            if (IsDashAlive())
                return;
            if (Time.time < UnlockTime())
                canMove = false;
        }

        private static AnimationCurve BuildLinear(float[] times, float[] values)
        {
            Keyframe[] keys = new Keyframe[times.Length];
            for (int i = 0; i < times.Length; ++i)
            {
                float inTangent = i > 0 ? (values[i] - values[i - 1]) / (times[i] - times[i - 1]) : 0f;
                float outTangent = i < times.Length - 1 ? (values[i + 1] - values[i]) / (times[i + 1] - times[i]) : 0f;
                keys[i] = new Keyframe(times[i], values[i], inTangent, outTangent);
            }
            return new AnimationCurve(keys);
        }
    }
}
