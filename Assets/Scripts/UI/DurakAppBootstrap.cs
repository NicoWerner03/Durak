using DurakGame.Network;
using UnityEngine;

namespace DurakGame.UI
{
    public static class DurakAppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntime()
        {
            if (Object.FindFirstObjectByType<DurakAppController>() != null)
            {
                return;
            }

            var root = new GameObject("DurakApp");
            Object.DontDestroyOnLoad(root);

            root.AddComponent<UnityNetworkSessionService>();
            root.AddComponent<DurakNetcodeBridge>();
            root.AddComponent<DurakAppController>();
            root.AddComponent<DurakScenarioAutomation>();
            root.AddComponent<DurakCanvasView>();
        }
    }
}
