using System.Collections;
using TMPro;
using UnityEngine;

namespace MultiplayerARPG
{
    /// <summary>
    /// Drives the Synty Fantasy Menus loading screen (Screen_FantasyMenus_Loading_01): plays its
    /// In/Out animation through the screen Animator's "Active" bool, fades the whole canvas in and
    /// out through a root CanvasGroup, keeps a fallback camera alive while the screen is up, and
    /// shows a random tip each time the screen appears.
    ///
    /// Both loading paths call into this - <see cref="UISceneLoading_G"/> for the home scene and
    /// <see cref="UINetworkSceneLoading_G"/> for map scenes - so the visuals live in one place.
    /// </summary>
    public class UILoadingScreenView : MonoBehaviour
    {
        private const string ActiveParam = "Active";

        [Header("Synty screen")]
        [Tooltip("Animator on the Synty screen root. Its 'Active' bool picks the In (true) or Out (false) state.")]
        public Animator screenAnimator;

        [Tooltip("Label_Tooltip on the Synty screen.")]
        public TMP_Text tipLabel;

        [Header("Fade")]
        [Tooltip("CanvasGroup on the loading canvas root. Alpha is driven from 0 to 1 on show and back on hide, on unscaled time.")]
        public CanvasGroup fadeGroup;
        public float fadeInDuration = 0.35f;
        [Tooltip("Keep this at or below the loaders' `finishedDelay`, which is how long the root stays active after loading completes.")]
        public float fadeOutDuration = 0.5f;

        [Header("Fallback camera")]
        [Tooltip("A camera that renders nothing (culling mask 0, solid black clear, low depth). Enabled while the screen is up so the " +
                 "editor's 'No cameras rendering' notice never appears between the old scene's camera being destroyed and the new one spawning.")]
        public Camera fallbackCamera;

        [Header("Tips")]
        [TextArea]
        [Tooltip("One is picked at random every time the screen shows; the same tip is never shown twice in a row.")]
        public string[] tips = new string[]
        {
            "B opens your inventory, N your hero, P your skills.",
            "Quests are on L, your party on O, the guild on G and friends on J.",
            "You can keep moving while you swing, but you cannot turn until the swing is done.",
            "Loot from your own kills is reserved for you for the first five seconds.",
            "Chest, legs, boots, gloves and cloaks all show on your character - dress for the occasion.",
        };

        private int _lastTip = -1;
        private Coroutine _fade;

        /// <summary>Called right after the screen's root object is activated.</summary>
        public void Show()
        {
            ShowRandomTip();
            if (fallbackCamera != null)
                fallbackCamera.enabled = true;
            if (screenAnimator != null && screenAnimator.isActiveAndEnabled)
                screenAnimator.SetBool(ActiveParam, true);
            StartFade(1f, fadeInDuration);
        }

        /// <summary>
        /// Starts the Out animation and the fade-out. The caller keeps the root active for at least
        /// `fadeOutDuration` before deactivating it, via the kit's `finishedDelay`.
        /// </summary>
        public void Hide()
        {
            if (screenAnimator != null && screenAnimator.isActiveAndEnabled)
                screenAnimator.SetBool(ActiveParam, false);
            StartFade(0f, fadeOutDuration);
        }

        public void ShowRandomTip()
        {
            if (tipLabel == null || tips == null || tips.Length == 0)
                return;
            int index = Random.Range(0, tips.Length);
            if (tips.Length > 1 && index == _lastTip)
                index = (index + 1) % tips.Length;
            _lastTip = index;
            tipLabel.text = tips[index];
        }

        private void LateUpdate()
        {
            // The loaders deactivate the screen root without telling us; release the camera then.
            if (fallbackCamera != null && fallbackCamera.enabled && screenAnimator != null && !screenAnimator.gameObject.activeInHierarchy)
                fallbackCamera.enabled = false;
        }

        private void StartFade(float target, float duration)
        {
            if (fadeGroup == null)
                return;
            if (_fade != null)
                StopCoroutine(_fade);
            _fade = StartCoroutine(FadeRoutine(target, duration));
        }

        private IEnumerator FadeRoutine(float target, float duration)
        {
            fadeGroup.blocksRaycasts = true;
            float from = target > 0.5f ? 0f : fadeGroup.alpha;
            if (target > 0.5f)
                fadeGroup.alpha = 0f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                fadeGroup.alpha = Mathf.Lerp(from, target, duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            fadeGroup.alpha = target;
            fadeGroup.blocksRaycasts = target > 0f;
            _fade = null;
        }
    }
}
