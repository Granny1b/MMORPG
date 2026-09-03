using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MultiplayerARPG
{
    /// <summary>
    /// Hides world geometry that stands between the gameplay camera and the played character, so a
    /// roof or a tree cannot park itself on top of the character in a top-down view.
    ///
    /// Hiding is grouped, not per-collider. A modular kit builds a roof out of dozens of separate
    /// tile prefabs, each with its own collider, so hiding only what the cast touched punches a
    /// one-tile hole in an otherwise solid roof - worse than no occlusion at all. Add a
    /// <see cref="CameraOccluderGroup"/> to the parent and every tile under it resolves to the same
    /// key here, so the roof lifts and drops as one piece. Colliders with no group above them fall
    /// back to hiding themselves, which is right for a standalone prop.
    ///
    /// Sits on the gameplay camera and runs in <c>LateUpdate</c> at the default execution order, so
    /// it reads the camera pose *after* <see cref="Insthync.CameraAndInput.FollowCamera"/> has
    /// written it - that component is <c>[DefaultExecutionOrder(int.MinValue)]</c> and assigns the
    /// transform outright each frame, so anything reading it earlier would see last frame's pose.
    ///
    /// Deliberately does NOT move the camera. The kit already ships that behaviour as
    /// `FollowCamera.enableWallHitSpring`, and it is switched off on our camera prefab on purpose:
    /// at a 40 degree pitch and 20m of zoom, springing the camera in to clear a tree swings the
    /// whole frame and reads far worse than the tree briefly disappearing. Detection here is
    /// independent of that flag, so the two can be used together if a wall ever needs both.
    ///
    /// The hide is done with <see cref="ShadowCastingMode.ShadowsOnly"/> rather than an alpha fade.
    /// That choice is the reason this works on any art in the project without preparation: it is a
    /// renderer setting, not a material one, so it needs no shader property, allocates no material
    /// instance, breaks no batching, and behaves identically on Synty's URP materials and on
    /// whatever shader stack replaces them later. The object keeps casting its shadow while hidden,
    /// which is what stops a building from visually evaporating. The cost is that it pops rather
    /// than fades; see <see cref="ApplyHidden"/> for where a fade would attach.
    /// </summary>
    public class TopDownCameraOccluder : MonoBehaviour
    {
        public enum HideMode
        {
            /// <summary>Keeps the shadow, hides the mesh. Preferred - the world keeps its lighting.</summary>
            ShadowsOnly,
            /// <summary>Disables the renderer outright. The shadow disappears too.</summary>
            DisableRenderer,
        }

        [Header("Detection")]
        [SerializeField]
        [Tooltip("Which layers can hide the character. Set this to your occluder layers (for example Occluder, Building, Harvestable). Leaving it empty disables the component.")]
        private LayerMask occluderLayers = 0;

        [SerializeField]
        [Tooltip("Radius of the cast. Widen it to catch thin posts and trunks that a plain ray would slip past.")]
        private float castRadius = 0.4f;

        [SerializeField]
        [Tooltip("Height above the character's feet to aim at. Chest height - aiming at the feet makes the cast graze the ground.")]
        private float targetHeightOffset = 1.2f;

        [SerializeField]
        [Tooltip("Stops the cast short of the camera, so geometry right against the lens is not counted.")]
        private float cameraPadding = 0.3f;

        [SerializeField]
        [Tooltip("Seconds between casts. Occlusion changes slowly; there is no reason to do this every frame.")]
        private float castInterval = 0.05f;

        [SerializeField]
        [Tooltip("Whether the cast hits trigger colliders as well as solid ones.")]
        private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Behaviour")]
        [SerializeField]
        private HideMode hideMode = HideMode.ShadowsOnly;

        [SerializeField]
        [Tooltip("How long an occluder stays hidden after it stops blocking. Prevents strobing when something sits on the edge of the cast.")]
        private float restoreDelay = 0.15f;

        [SerializeField]
        [Tooltip("Within a CameraOccluderGroup, hide only the renderers whose layer is in the mask above. Lets a group sit on a building root while only the roof tiles are marked as occluders. If no renderer in the group matches, the whole group is hidden instead.")]
        private bool restrictToOccluderLayers = true;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Draws the cast in the scene view while this object is selected and the game is playing.")]
        private bool drawGizmos = false;

        /// <summary>
        /// Everything currently hidden, keyed by the root it was grouped under - a
        /// <see cref="CameraOccluderGroup"/>'s render root where one exists, otherwise the hit
        /// collider's own transform. Keying by group rather than by collider is what makes a whole
        /// roof lift as one piece: many tiles resolve to the same key, so they are captured, hidden
        /// and restored together instead of one hole at a time. Renderers are resolved once per
        /// entry and cached - <c>GetComponentsInChildren</c> on a Synty building every frame would
        /// cost more than the cast itself.
        /// </summary>
        private readonly Dictionary<Transform, Occluder> _occluders = new Dictionary<Transform, Occluder>();
        private readonly List<Transform> _expired = new List<Transform>();

        // Reused so the cast does not allocate. A full buffer means hits were dropped, which is
        // reported once rather than silently thinning the set.
        private const int HitBufferSize = 32;
        private readonly RaycastHit[] _hits = new RaycastHit[HitBufferSize];
        private bool _warnedBufferFull;

        private Transform _cameraTransform;
        private float _nextCastTime;
        private Vector3 _debugFrom;
        private Vector3 _debugTo;

        private class Occluder
        {
            public Renderer[] renderers;
            public ShadowCastingMode[] shadowModes;
            public bool[] enabledStates;
            public float lastSeenTime;
        }

        private void Awake()
        {
            _cameraTransform = transform;
            if (occluderLayers == 0)
                Debug.LogWarning($"[{nameof(TopDownCameraOccluder)}] '{name}' has no occluder layers set, so nothing will ever be hidden. Assign the layers your blocking geometry lives on.", this);
        }

        private void LateUpdate()
        {
            if (occluderLayers == 0)
                return;

            if (Time.unscaledTime >= _nextCastTime)
            {
                _nextCastTime = Time.unscaledTime + castInterval;
                Cast();
            }

            ReleaseExpired();
        }

        private void Cast()
        {
            BasePlayerCharacterEntity character = GameInstance.PlayingCharacterEntity;
            if (character == null)
                return;

            Transform characterTransform = character.EntityTransform;
            if (characterTransform == null)
                return;

            Vector3 origin = characterTransform.position + (Vector3.up * targetHeightOffset);
            Vector3 toCamera = _cameraTransform.position - origin;
            float distance = toCamera.magnitude - cameraPadding;

            _debugFrom = origin;
            _debugTo = _cameraTransform.position;

            if (distance <= 0f)
                return;

            // Cast from the character outwards, never from the camera inwards. A sphere cast ignores
            // colliders it is already touching at the origin, so a camera that has ended up inside a
            // roof would miss that roof entirely - exactly the case this component exists for. The
            // character end is reliably in open space, and the same rule there means an object the
            // character is standing inside is left alone, which is the behaviour we want anyway.
            int count = Physics.SphereCastNonAlloc(origin, castRadius, toCamera.normalized, _hits, distance, occluderLayers, triggerInteraction);

            if (count >= HitBufferSize && !_warnedBufferFull)
            {
                _warnedBufferFull = true;
                Debug.LogWarning($"[{nameof(TopDownCameraOccluder)}] Hit buffer of {HitBufferSize} was filled; some occluders were skipped. Narrow the occluder layer mask or reduce the cast radius.", this);
            }

            float now = Time.unscaledTime;
            for (int i = 0; i < count; ++i)
            {
                Collider collider = _hits[i].collider;
                if (collider == null)
                    continue;

                // The character's own colliders should be excluded by the layer mask, but a project
                // that puts a building and a player on one layer should not blink the player out.
                if (collider.transform.IsChildOf(characterTransform))
                    continue;

                // Resolve the hit to the piece of world it belongs to before doing anything else.
                // Every tile of a grouped roof lands on the same key here, so the second and later
                // tiles in one cast just refresh the timer of an entry that is already hidden.
                CameraOccluderGroup group = collider.GetComponentInParent<CameraOccluderGroup>();
                Transform root = group != null ? group.RenderRoot : collider.transform;
                if (root == null)
                    continue;

                if (_occluders.TryGetValue(root, out Occluder tracked))
                {
                    tracked.lastSeenTime = now;
                    continue;
                }

                Occluder occluder = Capture(root);
                if (occluder == null)
                    continue;

                occluder.lastSeenTime = now;
                _occluders.Add(root, occluder);
                Hide(occluder);
            }
        }

        /// <summary>
        /// Records a root's renderers and their original state, so the hide can be undone exactly.
        /// Returns null when there is nothing to draw under it, which keeps pure physics volumes out
        /// of the dictionary entirely.
        /// </summary>
        private Occluder Capture(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return null;

            if (restrictToOccluderLayers)
                renderers = FilterToOccluderLayers(renderers);

            Occluder occluder = new Occluder
            {
                renderers = renderers,
                shadowModes = new ShadowCastingMode[renderers.Length],
                enabledStates = new bool[renderers.Length],
            };

            for (int i = 0; i < renderers.Length; ++i)
            {
                occluder.shadowModes[i] = renderers[i].shadowCastingMode;
                occluder.enabledStates[i] = renderers[i].enabled;
            }

            return occluder;
        }

        /// <summary>
        /// Narrows a group to the renderers actually marked as occluders, so a group can sit on a
        /// building root while only the roof tiles vanish and the floor stays put.
        ///
        /// Falls back to the full set when nothing matches, which covers the common prefab shape
        /// where the collider carries the occluder layer but the meshes underneath were left on
        /// Default. Without that fallback such a prop would be tracked and then hide nothing, which
        /// looks exactly like the component being broken.
        /// </summary>
        private Renderer[] FilterToOccluderLayers(Renderer[] renderers)
        {
            int mask = occluderLayers.value;
            int matched = 0;
            for (int i = 0; i < renderers.Length; ++i)
            {
                if ((mask & (1 << renderers[i].gameObject.layer)) != 0)
                    ++matched;
            }

            if (matched == 0 || matched == renderers.Length)
                return renderers;

            Renderer[] filtered = new Renderer[matched];
            int write = 0;
            for (int i = 0; i < renderers.Length; ++i)
            {
                if ((mask & (1 << renderers[i].gameObject.layer)) != 0)
                    filtered[write++] = renderers[i];
            }
            return filtered;
        }

        /// <summary>
        /// Restores anything that has not been hit for <see cref="restoreDelay"/>, and drops entries
        /// whose root has been destroyed - level geometry can be pooled or streamed out while it is
        /// hidden, and holding the reference would leak it.
        /// </summary>
        private void ReleaseExpired()
        {
            if (_occluders.Count == 0)
                return;

            float now = Time.unscaledTime;
            _expired.Clear();

            foreach (KeyValuePair<Transform, Occluder> pair in _occluders)
            {
                if (pair.Key == null || now - pair.Value.lastSeenTime >= restoreDelay)
                    _expired.Add(pair.Key);
            }

            for (int i = 0; i < _expired.Count; ++i)
            {
                Transform root = _expired[i];
                if (_occluders.TryGetValue(root, out Occluder occluder))
                {
                    if (root != null)
                        Restore(occluder);
                    _occluders.Remove(root);
                }
            }

            _expired.Clear();
        }

        private void Hide(Occluder occluder)
        {
            for (int i = 0; i < occluder.renderers.Length; ++i)
            {
                Renderer renderer = occluder.renderers[i];
                if (renderer != null)
                    ApplyHidden(renderer);
            }
        }

        private void Restore(Occluder occluder)
        {
            for (int i = 0; i < occluder.renderers.Length; ++i)
            {
                Renderer renderer = occluder.renderers[i];
                if (renderer != null)
                    ApplyVisible(renderer, occluder.shadowModes[i], occluder.enabledStates[i]);
            }
        }

        /// <summary>
        /// Hides one renderer. Override this together with <see cref="ApplyVisible"/> to swap in a
        /// dissolve or an alpha fade - the detection above needs no changes. Note that any fade
        /// needs cooperation from the shader (a transparent or alpha-clipped variant plus a property
        /// to drive), which is why it is not the default: the shader stack for this project's world
        /// art is not settled yet, and the shadow trick needs nothing from it.
        /// </summary>
        protected virtual void ApplyHidden(Renderer renderer)
        {
            switch (hideMode)
            {
                case HideMode.DisableRenderer:
                    renderer.enabled = false;
                    break;
                default:
                    renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    break;
            }
        }

        /// <summary>
        /// Puts a renderer back exactly as it was found, rather than assuming the default - a
        /// renderer that was already <c>ShadowsOnly</c>, or already disabled, must stay that way.
        /// </summary>
        protected virtual void ApplyVisible(Renderer renderer, ShadowCastingMode shadowMode, bool enabled)
        {
            renderer.shadowCastingMode = shadowMode;
            renderer.enabled = enabled;
        }

        /// <summary>
        /// Restores everything when the component is switched off, the camera is destroyed, or the
        /// scene is unloaded. Without this, anything hidden at that instant stays hidden for the
        /// lifetime of the object.
        /// </summary>
        private void OnDisable()
        {
            foreach (KeyValuePair<Transform, Occluder> pair in _occluders)
            {
                if (pair.Key != null)
                    Restore(pair.Value);
            }
            _occluders.Clear();
            _expired.Clear();
            _nextCastTime = 0f;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !Application.isPlaying)
                return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(_debugFrom, _debugTo);
            Gizmos.DrawWireSphere(_debugFrom, castRadius);
            Gizmos.DrawWireSphere(_debugTo, castRadius);
        }
    }
}
