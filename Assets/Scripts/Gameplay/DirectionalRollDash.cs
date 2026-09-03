using System.Collections.Generic;
using Insthync.CameraAndInput;
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
    /// Direction comes from the owner's raw input through the controller's own
    /// <c>GetMoveDirection</c>; the entity's synced flags cannot be used because
    /// <c>PlayerCharacterController</c> stamps every movement as plain Forward. Anywhere without
    /// that input (server copy of a remote player) the roll goes along facing with the forward clip.
    /// </summary>
    [RequireComponent(typeof(BaseGameEntity))]
    public class DirectionalRollDash : MonoBehaviour, IEntityMovementForceUpdateListener
    {
        [Header("Distance")]
        [Tooltip("Ground distance the profile covers. The authored Synty roll travels 4.42 m; holding walking speed through the slow middle adds a little on top.")]
        public float rollDistance = 3.7f;

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

        /// <summary>True from the roll's first movement tick until <see cref="rollDuration"/> has elapsed.</summary>
        public bool IsRolling { get { return _rollStartTime >= 0f && Time.time < _rollStartTime + rollDuration; } }

        private BaseGameEntity _entity;
        private PlayableCharacterModel _model;
        private float _rollStartTime = -1f;
        private float _lastDashTickTime = -1f;
        private float _lockedYaw;

        private void Awake()
        {
            _entity = GetComponent<BaseGameEntity>();
            _model = GetComponent<PlayableCharacterModel>();
            _entity.onCanMoveValidated += OnCanMove;
        }

        private void OnDestroy()
        {
            if (_entity != null)
                _entity.onCanMoveValidated -= OnCanMove;
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
            if (!IsRolling)
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
                _entity.SetLookRotation(Quaternion.Euler(0f, _lockedYaw, 0f), true);
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

        private void CancelRoll()
        {
            if (_model != null)
                _model.StopCustomAnimation();
            _rollStartTime = -1f;
        }

        private void BeginRoll(EntityMovementForceApplier applier)
        {
            Vector3 facing = transform.forward;
            facing.y = 0f;
            facing = facing.sqrMagnitude > 0.001f ? facing.normalized : Vector3.forward;
            Vector3 direction = GetInputDirection();
            if (direction.sqrMagnitude <= 0.001f)
                direction = facing;

            applier.Direction = direction;
            applier.Deceleration = 0f;
            applier.Duration = rollDuration;

            _rollStartTime = Time.time;
            _lockedYaw = transform.eulerAngles.y;
            PlayRollClip(facing, direction);
        }

        private void PlayRollClip(Vector3 facing, Vector3 direction)
        {
            if (_model == null)
                return;
            float angle = Vector3.SignedAngle(facing, direction, Vector3.up); // positive = to the right
            float abs = Mathf.Abs(angle);
            int index = abs <= 45f ? forwardClipIndex : abs >= 135f ? backwardClipIndex : angle > 0f ? rightClipIndex : leftClipIndex;
            _model.PlayCustomAnimation(index, false);
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
            float walking = _entity.GetMoveSpeed(MovementState.Forward, ExtraMovementState.None);
            return Mathf.Max(travel / dt, walking + 0.01f);
        }

        private Vector3 GetInputDirection()
        {
            PlayerCharacterController controller = BasePlayerCharacterController.Singleton as PlayerCharacterController;
            if (controller == null || (BaseGameEntity)controller.PlayingCharacterEntity != _entity)
                return Vector3.zero;
            float horizontal = InputManager.GetAxis("Horizontal", false);
            float vertical = InputManager.GetAxis("Vertical", false);
            Vector3 direction = controller.GetMoveDirection(horizontal, vertical);
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.zero;
        }

        private void OnCanMove(BaseGameEntity entity, ref bool canMove)
        {
            if (!lockMovementDuringTail || _rollStartTime < 0f)
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
