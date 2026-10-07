using UnityEngine;

namespace WildCardPoker.Unity
{
    /// <summary>
    /// Press Play in any scene: if the scene has no table yet, one is created with default settings.
    /// To customise a table, add a <see cref="TableController"/> to a GameObject in your scene and edit it in the Inspector.
    /// </summary>
    public static class GameBootstrap
    {
        public static bool AutoStart = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!AutoStart || Object.FindAnyObjectByType<TableController>() != null) return;
            Application.targetFrameRate = 60;
            new GameObject("Wild Card Poker").AddComponent<TableController>();
        }
    }
}
