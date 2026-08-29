using Insthync.CameraAndInput;
using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>
    /// Adds top-down cursor aiming on top of the stock controller: the character turns to
    /// face the mouse cursor, projected onto a horizontal plane at its feet, and attacks
    /// fire in that direction rather than toward the movement direction or a nearby enemy.
    /// WASD movement, activating and hotkeys are all inherited unchanged.
    /// </summary>
    public class TopDownAimController : PlayerCharacterController
    {
        [Header("Top-Down Aiming")]
        [SerializeField]
        [Tooltip("Turn the character to face the mouse cursor.")]
        protected bool faceCursor = true;
        [SerializeField]
        [Tooltip("Snap to the aim rotation instantly. Uncheck to let the movement component smooth the turn.")]
        protected bool turnImmediately = true;
        [SerializeField]
        [Tooltip("Always attack and cast toward the cursor. Uncheck to let the kit turn the character toward a nearby enemy instead.")]
        protected bool strictCursorAim = true;
        [SerializeField]
        [Tooltip("Keep facing the cursor while an attack/skill animation is playing. Turn this off and the movement component re-aims the character at its move direction mid-attack.")]
        protected bool faceCursorWhileDoingAction = true;
        [SerializeField]
        [Tooltip("Stop updating the aim while the cursor is over a UI element.")]
        protected bool ignoreWhileCursorOverUI = true;
        [SerializeField]
        [Tooltip("Suppress the Attack input while the cursor is over a UI element, so clicking inventory or shop windows does not also swing the weapon.")]
        protected bool blockAttackWhenCursorOverUI = true;
        [SerializeField]
        [Tooltip("Height of the aim plane, relative to the character's feet.")]
        protected float aimPlaneHeightOffset = 0f;
        [SerializeField]
        [Tooltip("Ignore aim points closer than this to the character, to avoid spinning when the cursor is on top of it.")]
        protected float minAimDistance = 0.2f;

        [Header("Strafe Animation")]
        [SerializeField]
        [Tooltip("Report the movement direction relative to where the character is facing, so strafing and back-pedalling play their own animations instead of the forward run. Needs the model's eight directional move states filled in.")]
        protected bool strafeMovementStates = true;

        /// <summary>Last valid cursor position projected onto the aim plane.</summary>
        public Vector3 AimWorldPosition { get; protected set; }

        /// <summary>Whether <see cref="AimWorldPosition"/> has been resolved at least once.</summary>
        public bool HasAimWorldPosition { get; protected set; }

        /// <summary>Aim position to hand to <see cref="UseHotkey"/> for cursor-aimed skills.</summary>
        public AimPosition CurrentAimPosition
        {
            get
            {
                return HasAimWorldPosition
                    ? AimPosition.CreatePosition(AimWorldPosition)
                    : AimPosition.CreatePosition(EntityTransform.position + EntityTransform.forward);
            }
        }

        public override void UpdateWASDInput()
        {
            base.UpdateWASDInput();
            // The aim rotation must land here rather than at the end of ManagedUpdate:
            // the base calls UpdateWASDInput() -> UpdateWASDAttack() in the same frame, so
            // applying it later would let an attack fire using the movement direction.
            UpdateTopDownAim();
            ApplyStrafeMovementState();
            RedirectPendingActionToCursor();
        }

        public override void UpdateInput()
        {
            // `UpdateWASDAttack` is not virtual and reads the "Attack" button directly, so the
            // input itself is muted for the duration of the base call rather than intercepted.
            bool suppressAttack = blockAttackWhenCursorOverUI
                && UISceneGameplay != null
                && UISceneGameplay.IsPointerOverUIObject();

            if (!suppressAttack)
            {
                base.UpdateInput();
                return;
            }

            SetAttackInputEnabled(false);
            try
            {
                base.UpdateInput();
            }
            finally
            {
                SetAttackInputEnabled(true);
            }
        }

        /// <summary>
        /// Enables/disables the "Attack" input action. A disabled action reports not-pressed, and
        /// there is no "Attack" axis in the legacy Input Manager for `InputManager.GetButton` to
        /// fall back to, so this reliably mutes the attack input for one frame.
        /// </summary>
        protected virtual void SetAttackInputEnabled(bool enabled)
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.InputAction attackAction;
            if (!InputManager.TryGetInputAction("Attack", out attackAction) || attackAction == null)
                return;
            if (enabled)
                attackAction.Enable();
            else
                attackAction.Disable();
#endif
        }

        public override void ManagedUpdate()
        {
            base.ManagedUpdate();
            // UpdateWASDAttack() sets the pending-action target *after* UpdateWASDInput() has
            // run, so catch that here too and the redirect is in place for the next frame.
            RedirectPendingActionToCursor();
        }

        /// <summary>
        /// The kit defers attacks through `_turnToTargetActionType`, turning the character
        /// toward the selected entity before firing. Point that at the cursor instead so the
        /// attack goes where the player is aiming.
        /// </summary>
        protected virtual void RedirectPendingActionToCursor()
        {
            if (!strictCursorAim || !HasAimWorldPosition)
                return;

            if (_turnToTargetActionType == TargetActionType.Attack ||
                _turnToTargetActionType == TargetActionType.UseSkill)
            {
                _turnToTargetPosition = AimWorldPosition;
            }
        }

        /// <summary>
        /// The stock controller reports <see cref="MovementState.Forward"/> for any WASD input,
        /// because it turns the character into the move direction before sending it. Facing the
        /// cursor breaks that assumption, so the direction is recomputed relative to the
        /// character's actual facing and re-sent.
        ///
        /// Re-sending in the same frame is safe: `KeyMovement` only overwrites the pending state,
        /// and the jump/dash bits dropped here are re-derived from the `_isJumping`/`_isDashing`
        /// latches the base call already set, in `AfterMovementUpdate`.
        /// </summary>
        protected virtual void ApplyStrafeMovementState()
        {
            if (!strafeMovementStates || _moveDirection.sqrMagnitude <= 0f)
                return;

            if (PlayingCharacterEntity == null || PlayingCharacterEntity.IsDead())
                return;

            // Swimming and ladder climbing carry the Up/Down direction bits, which this would
            // overwrite, and neither has a strafe animation set.
            if (PlayingCharacterEntity.MovementState.Has(MovementState.IsUnderWater))
                return;
            if (PlayingCharacterEntity.LadderComponent != null && PlayingCharacterEntity.LadderComponent.ClimbingLadder)
                return;

            PlayingCharacterEntity.KeyMovement(_moveDirection, GameplayUtils.GetMovementStateByDirection(_moveDirection, MovementTransform.forward));
        }

        protected virtual void UpdateTopDownAim()
        {
            if (!faceCursor)
                return;

            if (PlayingCharacterEntity == null || PlayingCharacterEntity.IsDead())
                return;

            if (ignoreWhileCursorOverUI && UISceneGameplay != null && UISceneGameplay.IsPointerOverUIObject())
                return;

            Vector3 aimPosition;
            if (!TryGetCursorWorldPosition(out aimPosition))
                return;

            AimWorldPosition = aimPosition;
            HasAimWorldPosition = true;

            if (!faceCursorWhileDoingAction)
            {
                if (_turnToTargetActionType != TargetActionType.None)
                    return;
                if (PlayingCharacterEntity.IsPlayingAttackOrUseSkillAnimation())
                    return;
            }

            Vector3 direction = aimPosition - EntityTransform.position;
            direction.y = 0f;
            if (direction.magnitude < minAimDistance)
                return;

            PlayingCharacterEntity.SetLookRotation(Quaternion.LookRotation(direction.normalized), turnImmediately);
        }

        /// <summary>
        /// Projects the mouse cursor onto a horizontal plane at the character's feet.
        /// A plane is used rather than a physics raycast so aiming still works over
        /// pits, water and gaps in level geometry.
        /// </summary>
        public virtual bool TryGetCursorWorldPosition(out Vector3 result)
        {
            result = Vector3.zero;

            Camera camera = CacheGameplayCameraController != null ? CacheGameplayCameraController.Camera : Camera.main;
            if (camera == null)
                return false;

            Ray ray = camera.ScreenPointToRay(InputManager.MousePosition());
            Plane plane = new Plane(Vector3.up, new Vector3(0f, EntityTransform.position.y + aimPlaneHeightOffset, 0f));
            float enter;
            if (!plane.Raycast(ray, out enter))
                return false;

            result = ray.GetPoint(enter);
            return true;
        }
    }
}
