using Insthync.CameraAndInput;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>
    /// Closes every opened window when the escape key is pressed, then toggles the system menu
    /// when it is pressed again while no window is opened (World of Warcraft style).
    /// Remove the escape key entry from `UISceneGameplay`'s `toggleUis` when using this component,
    /// otherwise the system menu will be toggled while the windows are being closed.
    /// </summary>
    public class UIEscapeWindowsHandler : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("It will close opened windows or toggle `uiSystemMenu` when key with `keyCode` pressed or button with `buttonName` pressed.")]
        public KeyCode keyCode = KeyCode.Escape;
        [Tooltip("It will close opened windows or toggle `uiSystemMenu` when key with `keyCode` pressed or button with `buttonName` pressed.")]
        public string buttonName = "CloseUI";

        [Header("Windows")]
        [Tooltip("`UIBase` components which are attached to direct children of these transforms will be collected as closable windows.")]
        public List<Transform> windowContainers = new List<Transform>();
        [Tooltip("`UIBase` components which are attached to these transforms will be collected as closable windows.")]
        public List<Transform> windowObjects = new List<Transform>();
        [Tooltip("These UIs won't be closed even if they were collected from `windowContainers` or `windowObjects`.")]
        public List<UIBase> excludingWindows = new List<UIBase>();

        [Header("System Menu")]
        [Tooltip("This UI will be toggled while no window is opened.")]
        public UIBase uiSystemMenu;

        private readonly List<UIBase> _windows = new List<UIBase>();

        private void Awake()
        {
            CollectWindows();
        }

        private void OnDestroy()
        {
            windowContainers?.Clear();
            windowObjects?.Clear();
            excludingWindows?.Clear();
            uiSystemMenu = null;
            _windows.Clear();
        }

        private void Update()
        {
            if (GenericUtils.IsFocusInputField())
                return;

            if (!InputManager.GetKeyDown(keyCode) && !InputManager.GetButtonDown(buttonName))
                return;

            if (!CloseOpenedWindows() && uiSystemMenu != null)
                uiSystemMenu.Toggle();
        }

        [ContextMenu("Collect Windows")]
        public void CollectWindows()
        {
            _windows.Clear();
            foreach (Transform windowContainer in windowContainers)
            {
                if (windowContainer == null)
                    continue;
                for (int i = 0; i < windowContainer.childCount; ++i)
                {
                    AddWindows(windowContainer.GetChild(i));
                }
            }
            foreach (Transform windowObject in windowObjects)
            {
                AddWindows(windowObject);
            }
        }

        private void AddWindows(Transform windowTransform)
        {
            if (windowTransform == null)
                return;
            foreach (UIBase ui in windowTransform.GetComponents<UIBase>())
            {
                if (excludingWindows.Contains(ui) || _windows.Contains(ui))
                    continue;
                _windows.Add(ui);
            }
        }

        /// <summary>
        /// Hides every collected window which is currently visible.
        /// </summary>
        /// <returns>`TRUE` if at least one window was closed.</returns>
        public bool CloseOpenedWindows()
        {
            bool closedAnyWindow = false;
            foreach (UIBase window in _windows)
            {
                if (window == null || !window.IsVisible())
                    continue;
                window.Hide();
                closedAnyWindow = true;
            }
            if (closedAnyWindow)
                HideNpcDialog();
            return closedAnyWindow;
        }

        private void HideNpcDialog()
        {
            // Hiding the NPC dialog's UI is not enough, the server has to know that the player is not talking to the NPC anymore
            if (GameInstance.PlayingCharacterEntity == null)
                return;
            UISceneGameplay uiSceneGameplay = BaseUISceneGameplay.Singleton as UISceneGameplay;
            if (uiSceneGameplay == null)
                return;
            uiSceneGameplay.HideNpcDialog();
        }
    }
}
