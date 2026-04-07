using UnityEngine;

namespace DurakGame.Build
{
    public static class BuildVersionProvider
    {
        private const string StampResourcePath = "BuildVersionStamp";

        public static string GetDisplayVersion()
        {
            var appVersion = Application.version;
            if (string.IsNullOrWhiteSpace(appVersion))
            {
                appVersion = "0.0.0-dev";
            }

            var stampAsset = Resources.Load<TextAsset>(StampResourcePath);
            if (stampAsset == null || string.IsNullOrWhiteSpace(stampAsset.text))
            {
                return "Version " + appVersion;
            }

            return "Version " + appVersion + " (" + stampAsset.text.Trim() + ")";
        }
    }
}
