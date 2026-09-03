using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>
    /// Turns off sprint, crouch, crawl and jump for the character this sits on, at the entity level.
    ///
    /// The kit validates every extra movement state through the entity's
    /// <c>onCanSprintValidated</c> / <c>onCanCrouchValidated</c> / <c>onCanCrawlValidated</c> /
    /// <c>onCanJumpValidated</c>
    /// events before the movement system applies it (EntityMovementFunctions.ValidateExtraMovementState),
    /// so answering "no" here makes the state fall back to None regardless of which controller,
    /// key binding or UI button asked for it. Nothing in the kit or the controllers is edited.
    /// </summary>
    [RequireComponent(typeof(BaseGameEntity))]
    public class DisabledMovementStates : MonoBehaviour
    {
        public bool disableSprint = true;
        public bool disableCrouch = true;
        public bool disableCrawl = true;
        [Tooltip("Jump is replaced by the dash roll on this character; keep this on so no controller or UI button can still jump.")]
        public bool disableJump = true;

        private BaseGameEntity _entity;

        private void Awake()
        {
            _entity = GetComponent<BaseGameEntity>();
            _entity.onCanSprintValidated += OnCanSprint;
            _entity.onCanCrouchValidated += OnCanCrouch;
            _entity.onCanCrawlValidated += OnCanCrawl;
            _entity.onCanJumpValidated += OnCanJump;
        }

        private void OnDestroy()
        {
            if (_entity == null)
                return;
            _entity.onCanSprintValidated -= OnCanSprint;
            _entity.onCanCrouchValidated -= OnCanCrouch;
            _entity.onCanCrawlValidated -= OnCanCrawl;
            _entity.onCanJumpValidated -= OnCanJump;
        }

        private void OnCanSprint(BaseGameEntity entity, ref bool canSprint)
        {
            if (disableSprint)
                canSprint = false;
        }

        private void OnCanCrouch(BaseGameEntity entity, ref bool canCrouch)
        {
            if (disableCrouch)
                canCrouch = false;
        }

        private void OnCanCrawl(BaseGameEntity entity, ref bool canCrawl)
        {
            if (disableCrawl)
                canCrawl = false;
        }

        private void OnCanJump(BaseGameEntity entity, ref bool canJump)
        {
            if (disableJump)
                canJump = false;
        }
    }
}
