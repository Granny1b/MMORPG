#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UtilsComponents;

namespace MultiplayerARPG
{
    public partial class BaseGameEntity
    {
        [ContextMenu("Adjust Scale For Asset Store")]
        private void AdjustScaleForAssetStore()
        {
            if (transform.localScale.x != 1 ||
                transform.localScale.y != 1 ||
                transform.localScale.z != 1)
            {
                var comp = gameObject.AddComponent<SetScaleOnAwake>();
                comp.scale = transform.localScale;
                transform.localScale = Vector3.one;
                EditorUtility.SetDirty(this);
            }
        }
    }
}
#endif