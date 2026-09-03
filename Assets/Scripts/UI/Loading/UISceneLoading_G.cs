using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !DISABLE_ADDRESSABLES
using Insthync.AddressableAssetTools;
#endif

namespace MultiplayerARPG
{
    /// <summary>
    /// Project loading screen for the non-networked scene loads (GameInstance.LoadHomeScene).
    /// Adds the Synty In/Out animation and tips on top of the kit's <see cref="UISceneLoading"/>.
    ///
    /// Also works around a kit bug: <see cref="UISceneLoading.Singleton"/> has a private setter
    /// that nothing in the kit ever calls, so <c>GameInstance.LoadHomeScene()</c> always takes its
    /// "no loading UI" branch. This sets the property through reflection; once upstream assigns
    /// it themselves the extra write is a no-op.
    /// </summary>
    public class UISceneLoading_G : UISceneLoading
    {
        public UILoadingScreenView view;

        protected override void Awake()
        {
            base.Awake();
            if (Singleton == null && this != null)
            {
                PropertyInfo prop = typeof(UISceneLoading).GetProperty("Singleton", BindingFlags.Public | BindingFlags.Static);
                MethodInfo setter = prop != null ? prop.GetSetMethod(true) : null;
                if (setter != null)
                    setter.Invoke(null, new object[] { this });
            }
        }

        public override async UniTask LoadScene(string sceneName)
        {
            // Mirror the base early-out so the screen does not flash when there is nothing to load.
            if (SceneManager.GetActiveScene().name.Equals(sceneName))
                return;
            await LoadWithView(base.LoadScene(sceneName));
        }

#if !DISABLE_ADDRESSABLES
        public override async UniTask LoadScene(AssetReferenceScene sceneRef)
        {
            if (SceneManager.GetActiveScene().name.Equals(sceneRef.SceneName))
                return;
            await LoadWithView(base.LoadScene(sceneRef));
        }
#endif

        private async UniTask LoadWithView(UniTask load)
        {
            if (rootObject != null)
                rootObject.SetActive(true);
            if (view != null)
                view.Show();
            // The base method hides the root `finishedDelay` seconds after the new scene is in;
            // sceneLoaded fires at that moment, so the Out animation runs inside that delay.
            SceneManager.sceneLoaded += OnSceneLoaded;
            try
            {
                await load;
            }
            finally
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (view != null)
                view.Hide();
        }
    }
}
