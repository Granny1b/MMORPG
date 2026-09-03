using MultiplayerARPG.GameData.Model.Playables;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MultiplayerARPG
{
    /// <summary>
    /// Re-evaluates the action layer's avatar mask while an attack is playing, so the legs hand back
    /// to locomotion the moment the character starts moving.
    ///
    /// `AnimationPlayableBehaviour.PlayAction` chooses the mask once, at the start of the swing:
    /// `avatarMaskWhileMoving` if the character is grounded and moving at that instant, otherwise a
    /// fall-through ending at `EmptyMask` (full body). It never revisits that choice. Start an
    /// attack standing still and then walk, and the legs stay welded to the attack clip for the rest
    /// of the swing while the character slides - the lower body reads as broken.
    ///
    /// This watches the movement state each frame and swaps the mask on any active action layer when
    /// it changes, which is the same decision the kit makes, just kept up to date.
    ///
    /// Reads only public API (`Behaviour`, `LayerMixer`, `ACTION_LAYER`, `EmptyMask`), so no kit file
    /// is modified - but it depends on those staying public across kit updates.
    /// </summary>
    [RequireComponent(typeof(PlayableCharacterModel))]
    public class ActionLayerMaskUpdater : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Applied to the action layer while the character is moving on the ground. Leave empty to disable.")]
        private AvatarMask movingMask;

        private PlayableCharacterModel _model;
        private DirectionalRollDash _roll;
        private bool _hasApplied;
        private bool _appliedMasked;

        private void Awake()
        {
            _model = GetComponent<PlayableCharacterModel>();
            _roll = GetComponent<DirectionalRollDash>();
        }

        private void LateUpdate()
        {
            if (movingMask == null || _model == null)
                return;

            AnimationPlayableBehaviour behaviour = _model.Behaviour;
            if (behaviour == null)
                return;

            AnimationLayerMixerPlayable mixer = behaviour.LayerMixer;
            if (!mixer.IsValid())
                return;

            // Action layers start at ACTION_LAYER; anything below is locomotion and left alone.
            int inputCount = mixer.GetInputCount();
            bool anyActionPlaying = false;
            for (int layer = AnimationPlayableBehaviour.ACTION_LAYER; layer < inputCount; ++layer)
            {
                if (mixer.GetInputWeight(layer) > 0f)
                {
                    anyActionPlaying = true;
                    break;
                }
            }

            if (!anyActionPlaying)
            {
                // Forget the last decision so the next swing is evaluated fresh.
                _hasApplied = false;
                return;
            }

            // The same condition PlayAction uses, so this only ever continues the kit's own logic.
            bool shouldMask = _model.MovementState.HasDirectionMovement()
                && _model.MovementState.Has(MovementState.IsGrounded);
            // A dodge roll is a full-body action that moves the character; never trim it to the upper body.
            if (_roll != null && _roll.IsRolling)
                shouldMask = false;

            if (_hasApplied && _appliedMasked == shouldMask)
                return;

            AvatarMask mask = shouldMask ? movingMask : AnimationPlayableBehaviour.EmptyMask;
            for (int layer = AnimationPlayableBehaviour.ACTION_LAYER; layer < inputCount; ++layer)
            {
                if (mixer.GetInputWeight(layer) > 0f)
                    mixer.SetLayerMaskFromAvatarMask((uint)layer, mask);
            }

            _hasApplied = true;
            _appliedMasked = shouldMask;
        }
    }
}
