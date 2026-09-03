using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>
    /// Marks a set of objects that <see cref="TopDownCameraOccluder"/> should hide and show as one
    /// piece. Put this on the parent of a modular build - a roof, a bridge deck, a whole prop.
    ///
    /// This exists because modular kits are assembled from many small prefabs, each with its own
    /// collider. Without a group, a cast that hits one roof tile hides exactly that tile, and the
    /// character is revealed through a one-tile hole in an otherwise solid roof. With a group, any
    /// tile hit hides every renderer under <see cref="RenderRoot"/>, so the whole roof lifts off
    /// together and drops back together.
    ///
    /// Where you attach it decides what disappears, and the useful distinction is the floor. A group
    /// on a building root hides the floor along with the roof, and because a floor is never between
    /// the character's chest and a camera above them, the only thing that reaches is a hole in the
    /// ground. Two setups avoid that: attach this to a child holding just the roof, or attach it to
    /// the building root and leave `restrictToOccluderLayers` on so only the tiles actually marked
    /// as occluders are hidden.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraOccluderGroup : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Hide every renderer under this transform when any part of the group is blocking. Leave empty to use this object.")]
        private Transform renderRoot;

        /// <summary>
        /// The transform whose children are hidden. Falls back to this object, which is the normal
        /// case - <see cref="renderRoot"/> is only for pointing at a sibling subtree.
        /// </summary>
        public Transform RenderRoot
        {
            get { return renderRoot != null ? renderRoot : transform; }
        }
    }
}
