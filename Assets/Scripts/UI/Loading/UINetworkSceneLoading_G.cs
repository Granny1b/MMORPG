using System.Collections;

namespace MultiplayerARPG
{
    /// <summary>
    /// Project loading screen for networked map loads. The network manager's
    /// onLoadSceneStart / Progress / Finish events call the inherited handlers; this adds the
    /// Synty In/Out animation and tips through <see cref="UILoadingScreenView"/>.
    /// </summary>
    public class UINetworkSceneLoading_G : UINetworkSceneLoading
    {
        public UILoadingScreenView view;

        public override void OnLoadSceneStart(string sceneName, bool isAdditive, bool isOnline, float progress)
        {
            base.OnLoadSceneStart(sceneName, isAdditive, isOnline, progress);
            if (view != null && rootObject != null && rootObject.activeSelf)
                view.Show();
        }

        protected override IEnumerator OnLoadSceneFinishRoutine()
        {
            // Out animation plays during the base routine's `finishedDelay` wait.
            if (view != null)
                view.Hide();
            return base.OnLoadSceneFinishRoutine();
        }
    }
}
