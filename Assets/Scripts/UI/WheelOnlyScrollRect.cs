using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MultiplayerARPG
{
    /// <summary>
    /// A <see cref="ScrollRect"/> that only ever scrolls from the mouse wheel and from its own
    /// scrollbar handle. Two ways of moving the content are taken away:
    ///
    /// 1. Pointer dragging. Stock `ScrollRect` pans `content` on any left-button drag inside the
    ///    viewport, which reads as the window itself being dragged around. The four drag callbacks
    ///    are overridden to do nothing. They are still *implemented*, so
    ///    `ExecuteEvents.GetEventHandler&lt;IDragHandler&gt;` still stops at this scroll view and
    ///    the drag is swallowed rather than bubbling to an ancestor. Item drag and drop is
    ///    untouched: `UIDragHandler` sits on the item icon, deeper in the tree, so the handler walk
    ///    finds it first.
    ///
    /// 2. Navigation scrolling. Slots are `Button001` + `NavigationChild`, and the sibling
    ///    `NavigationGroup` rewrites each slot's navigation to `Explicit` with computed neighbours
    ///    whenever the slot count or a sibling index changes. The movement axes then walk that grid
    ///    - arrow keys and WASD are both bound to Horizontal/Vertical in the legacy Input Manager -
    ///    and `Button001.OnSelect` calls `SelectableNavigationUtils.ScrollSnap`, which writes
    ///    `content.anchoredPosition` directly. This component overwrites those links with dead ends
    ///    every frame, so `Selectable.OnMove` finds nothing to move to and no `OnSelect` fires.
    ///
    /// The slots stay `Explicit` rather than `None` on purpose. `Selectable.OnPointerDown` only
    /// selects when `navigation.mode != None`, so `None` would leave
    /// `EventSystem.currentSelectedGameObject` null while an item is being dragged, and
    /// `UICharacterItemDragHandler.OnEndDrag` dereferences it without a null check whenever the
    /// pointer is over UI. Explicit-with-no-links keeps selection behaving exactly as it does today
    /// while still going nowhere.
    ///
    /// The scrollbar is deliberately left alone. `Scrollbar.OnMove` scrolls on arrow keys whenever
    /// `FindSelectableOn*()` returns null, which is true for both `None` and dead-end `Explicit`, so
    /// no navigation setting can stop it. It is safe only because its mode is `None` and it is
    /// therefore never selected by a click in the first place.
    /// </summary>
    [AddComponentMenu("UI/Wheel Only Scroll Rect")]
    [DefaultExecutionOrder(100)]
    public class WheelOnlyScrollRect : ScrollRect
    {
        private static readonly Navigation DeadEnd = new Navigation()
        {
            mode = Navigation.Mode.Explicit,
            selectOnUp = null,
            selectOnDown = null,
            selectOnLeft = null,
            selectOnRight = null,
        };

        [Tooltip("Keep the movement keys (arrows / WASD) and gamepad navigation from scrolling this view. Turn it off to hand keyboard navigation back to the slots.")]
        public bool blockNavigationScrolling = true;

        private NavigationGroup _navigationGroup;
        private bool _resolvedNavigationGroup;

        public override void OnInitializePotentialDrag(PointerEventData eventData) { }

        public override void OnBeginDrag(PointerEventData eventData) { }

        public override void OnDrag(PointerEventData eventData) { }

        public override void OnEndDrag(PointerEventData eventData) { }

        /// <summary>
        /// Runs at execution order 100 so it lands after `NavigationGroup.LateUpdate`, which is left
        /// at the default. Without that the two would fight on any frame the slot list is rebuilt.
        /// </summary>
        protected override void LateUpdate()
        {
            base.LateUpdate();

            if (!blockNavigationScrolling || !Application.isPlaying)
                return;

            if (!_resolvedNavigationGroup)
            {
                _resolvedNavigationGroup = true;
                _navigationGroup = GetComponent<NavigationGroup>();
                if (_navigationGroup == null)
                    _navigationGroup = GetComponentInChildren<NavigationGroup>(true);
            }

            if (_navigationGroup == null)
                return;

            List<NavigationChild> childs = _navigationGroup.childs;
            for (int i = 0; i < childs.Count; ++i)
            {
                NavigationChild child = childs[i];
                if (child == null || child.Selectable == null)
                    continue;
                Navigation navigation = child.Selectable.navigation;
                if (navigation.mode == Navigation.Mode.Explicit &&
                    navigation.selectOnUp == null && navigation.selectOnDown == null &&
                    navigation.selectOnLeft == null && navigation.selectOnRight == null)
                    continue;
                child.Selectable.navigation = DeadEnd;
            }
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// `ScrollRectEditor` is registered with `editorForChildClasses` and draws an explicit property
    /// list, so without this the extra field would never show up in the inspector. Same pattern the
    /// kit uses in `Scrollbar001.cs`.
    /// </summary>
    [CustomEditor(typeof(WheelOnlyScrollRect), true)]
    [CanEditMultipleObjects]
    public class WheelOnlyScrollRectEditor : UnityEditor.UI.ScrollRectEditor
    {
        protected SerializedProperty blockNavigationScrolling;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (blockNavigationScrolling == null)
                blockNavigationScrolling = serializedObject.FindProperty(nameof(WheelOnlyScrollRect.blockNavigationScrolling));

            serializedObject.Update();
            EditorGUILayout.PropertyField(blockNavigationScrolling, new GUIContent("Block Navigation Scrolling"));
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
