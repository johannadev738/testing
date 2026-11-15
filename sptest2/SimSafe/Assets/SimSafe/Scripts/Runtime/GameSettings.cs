using UnityEngine;

namespace SimSafe
{
    /// <summary>
    /// Player-facing settings, stored locally with PlayerPrefs (browser storage on WebGL).
    /// No accounts, no network.
    /// </summary>
    public static class GameSettings
    {
        const string P = "SimSafe.";

        public static float MasterVolume
        {
            get { return PlayerPrefs.GetFloat(P + "volume", 0.8f); }
            set { PlayerPrefs.SetFloat(P + "volume", Mathf.Clamp01(value)); AudioListener.volume = Mathf.Clamp01(value); PlayerPrefs.Save(); }
        }

        public static float MouseSensitivity
        {
            get { return PlayerPrefs.GetFloat(P + "mouse", 1f); }
            set { PlayerPrefs.SetFloat(P + "mouse", Mathf.Clamp(value, 0.3f, 2.5f)); PlayerPrefs.Save(); }
        }

        /// <summary>Interface text size multiplier (0.8 - 1.5).</summary>
        public static float UiScale
        {
            get { return PlayerPrefs.GetFloat(P + "uiscale", 1f); }
            set { PlayerPrefs.SetFloat(P + "uiscale", Mathf.Clamp(value, 0.8f, 1.5f)); PlayerPrefs.Save(); }
        }

        public static bool ShowIndicators
        {
            get { return PlayerPrefs.GetInt(P + "indicators", 1) == 1; }
            set { PlayerPrefs.SetInt(P + "indicators", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Demo mode: shortens the 10-20 second processing-time waits.</summary>
        public static bool ShortWaits
        {
            get { return PlayerPrefs.GetInt(P + "shortwaits", 0) == 1; }
            set { PlayerPrefs.SetInt(P + "shortwaits", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static float WaitScale { get { return ShortWaits ? 0.25f : 1f; } }

        public static void Apply() { AudioListener.volume = MasterVolume; }
    }
}
