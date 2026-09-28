using System;
using MelonLoader;
using UnityEngine;

namespace PhxCore
{
    /// <summary>
    /// Window mode row on the PhxCore SETTINGS page. Unity remembers the chosen mode between launches on its own.
    /// </summary>
    static class Display
    {
        internal static readonly string[] Modes = { "FULLSCREEN", "BORDERLESS", "WINDOWED" };

        internal static void Register()
        {
            Registry.Core.Linked("Display mode", Modes, Current, Apply);
        }

        internal static int Current()
        {
            switch (Screen.fullScreenMode)
            {
                case FullScreenMode.ExclusiveFullScreen:
                    return 0;
                case FullScreenMode.FullScreenWindow:
                    return 1;
                default:
                    return 2;
            }
        }

        internal static void Apply(int index)
        {
            try
            {
                int screenWidth = UnityEngine.Display.main.systemWidth;
                int screenHeight = UnityEngine.Display.main.systemHeight;
                switch (index)
                {
                    case 0:
                        Screen.SetResolution(screenWidth, screenHeight, FullScreenMode.ExclusiveFullScreen);
                        break;
                    case 1:
                        Screen.SetResolution(screenWidth, screenHeight, FullScreenMode.FullScreenWindow);
                        break;
                    default:
                        int width = Mathf.Min(1600, Mathf.RoundToInt(screenWidth * 0.75f));
                        int height = Mathf.RoundToInt(width * 9f / 16f);
                        Screen.SetResolution(width, height, FullScreenMode.Windowed);
                        break;
                }
                MelonLogger.Msg("Display mode set to " + Modes[index].ToLowerInvariant() + ".");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not change the display mode: " + e.Message);
            }
        }
    }
}
