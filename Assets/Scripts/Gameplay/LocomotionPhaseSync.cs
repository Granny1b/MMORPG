using System.Collections.Generic;
using MultiplayerARPG.GameData.Model.Playables;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MultiplayerARPG
{
    /// <summary>
    /// Keeps the stride phase continuous when the character model switches locomotion clips.
    ///
    /// `AnimationPlayableBehaviour.PrepareForNewState` creates a fresh `AnimationClipPlayable` for
    /// the incoming clip, which starts at time 0, then cross-fades to it from wherever the outgoing
    /// clip happened to be in its cycle. For the length of the transition the mixer averages two
    /// stride poses that have nothing to do with each other, which reads as the feet fighting and
    /// the arms going limp. This carries the outgoing playhead into the incoming clip instead.
    ///
    /// Synty's strafe sets are authored for a blend tree, so within a family the clips are already
    /// phase-locked and carrying the playhead straight across is enough. The `FwdStrafe` and
    /// `BckStrafe` families, however, are authored roughly 0.3-0.45 of a cycle apart, and the exact
    /// figure differs per move type - measured on this project's masculine set, the backward clips
    /// sit 0.42 (run), 0.40 (walk) and 0.32 (crouch) of a cycle from their forward counterparts.
    /// Jogging forward and then backward therefore blends at close to the worst possible alignment.
    /// <see cref="clipPhases"/> corrects for that: each clip declares its phase relative to a shared
    /// origin, and the sync maps the playhead through those.
    ///
    /// The table is filled by `Tools/MMORPG KIT/Synty Locomotion Animation Builder`, which measures
    /// each clip rather than assuming the figures above, since a different animation set - the
    /// feminine half of the pack included - will not share them.
    ///
    /// This reads only public API (`PlayableCharacterModel.Behaviour`, `BaseLayerMixer`,
    /// `LeftHandWieldingLayerMixer`), so no kit file is modified - but it does depend on those
    /// staying public across kit updates. If a future version hides them this stops compiling, which
    /// is the failure mode to want: loud, not silent.
    /// </summary>
    [RequireComponent(typeof(PlayableCharacterModel))]
    public class LocomotionPhaseSync : MonoBehaviour
    {
        [System.Serializable]
        public class ClipPhase
        {
            public AnimationClip clip;
            [Range(0f, 1f)]
            [Tooltip("Where this clip sits in the shared stride cycle. A clip absent from the table counts as 0.")]
            public float offset;
        }

        [SerializeField]
        [Tooltip("Skip the sync when the two clips' lengths differ by more than this factor either way. Guards against carrying a stride phase into something that is not a stride.")]
        private float maxLengthRatio = 1.5f;

        [SerializeField]
        [Tooltip("Per-clip stride phase, filled by the Synty Locomotion Animation Builder. Clips not listed are treated as phase 0.")]
        private ClipPhase[] clipPhases = new ClipPhase[0];

        public ClipPhase[] ClipPhases
        {
            get { return clipPhases; }
            set { clipPhases = value; _phaseLookup = null; }
        }

        private PlayableCharacterModel _model;
        private Dictionary<AnimationClip, float> _phaseLookup;
        private Playable _lastSyncedBase;
        private Playable _lastSyncedLeftHand;

        private void Awake()
        {
            _model = GetComponent<PlayableCharacterModel>();
        }

        /// <summary>
        /// Runs after the entity's animation update, so the sync lands one frame into the
        /// transition - at which point the incoming clip is still only `deltaTime /
        /// transitionDuration` of the way in, and the rest of the blend is aligned.
        /// </summary>
        private void LateUpdate()
        {
            AnimationPlayableBehaviour behaviour = _model != null ? _model.Behaviour : null;
            if (behaviour == null)
                return;

            Sync(behaviour.BaseLayerMixer, ref _lastSyncedBase);
            Sync(behaviour.LeftHandWieldingLayerMixer, ref _lastSyncedLeftHand);
        }

        private void Sync(AnimationMixerPlayable mixer, ref Playable lastSynced)
        {
            if (!mixer.IsValid())
                return;

            // One input means nothing is transitioning. The kit collapses back to a single input as
            // soon as the incoming clip reaches full weight.
            int inputCount = mixer.GetInputCount();
            if (inputCount < 2)
                return;

            // The incoming clip is always appended at the last port.
            Playable incoming = mixer.GetInput(inputCount - 1);
            if (!incoming.IsValid())
                return;
            if (lastSynced.IsValid() && incoming.Equals(lastSynced))
                return;

            // Whichever fading port still carries the most weight is the pose being blended out of.
            // There can be more than one: `PrepareForNewState` runs before `UpdateState`, so a new
            // port can be appended on the same frame the previous transition completes.
            int outgoingPort = -1;
            float outgoingWeight = 0f;
            for (int i = 0; i < inputCount - 1; ++i)
            {
                float weight = mixer.GetInputWeight(i);
                if (weight > outgoingWeight)
                {
                    outgoingWeight = weight;
                    outgoingPort = i;
                }
            }
            if (outgoingPort < 0)
                return;

            AnimationClip incomingClip = GetClip(incoming);
            AnimationClip outgoingClip = GetClip(mixer.GetInput(outgoingPort));
            if (incomingClip == null || outgoingClip == null)
                return;

            // Mark it handled either way, so a clip that must not be synced is not re-examined every
            // frame for the rest of its transition.
            lastSynced = incoming;

            if (!ShouldSync(outgoingClip, incomingClip))
                return;

            // The outgoing clip shows the shared cycle at (its playhead - its own phase); put the
            // incoming clip wherever it shows that same point.
            double normalized = mixer.GetInput(outgoingPort).GetTime() / outgoingClip.length;
            normalized += GetPhase(incomingClip) - GetPhase(outgoingClip);
            normalized %= 1.0;
            if (normalized < 0.0)
                normalized += 1.0;

            incoming.SetTime(normalized * incomingClip.length);
        }

        /// <summary>
        /// A jump, landing or death has no stride to inherit and has to start at its first frame, so
        /// only looping clips of comparable length carry their phase across.
        /// </summary>
        private bool ShouldSync(AnimationClip outgoing, AnimationClip incoming)
        {
            if (!outgoing.isLooping || !incoming.isLooping)
                return false;
            if (outgoing.length <= 0f || incoming.length <= 0f)
                return false;

            float ratio = incoming.length / outgoing.length;
            return ratio <= maxLengthRatio && ratio >= 1f / maxLengthRatio;
        }

        private float GetPhase(AnimationClip clip)
        {
            if (_phaseLookup == null)
            {
                _phaseLookup = new Dictionary<AnimationClip, float>();
                for (int i = 0; i < clipPhases.Length; ++i)
                {
                    if (clipPhases[i] == null || clipPhases[i].clip == null)
                        continue;
                    _phaseLookup[clipPhases[i].clip] = clipPhases[i].offset;
                }
            }

            float phase;
            return _phaseLookup.TryGetValue(clip, out phase) ? phase : 0f;
        }

        private static AnimationClip GetClip(Playable playable)
        {
            if (!playable.IsValid() || !playable.IsPlayableOfType<AnimationClipPlayable>())
                return null;
            return ((AnimationClipPlayable)playable).GetAnimationClip();
        }
    }
}
