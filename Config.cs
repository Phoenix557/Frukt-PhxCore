using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine.InputSystem;

namespace PhxCore
{
    /// <summary>
    /// Plain key=value settings in UserData/PhxCore.cfg. On first run it picks up values from the old Phx Pause file (Phx.cfg).
    /// Values for mods that are not installed right now are kept, so removing a mod does not lose its settings.
    /// </summary>
    internal static class Config
    {
        static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool _loaded;

        internal static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            try
            {
                bool migrated = false;
                string path = FilePath("PhxCore.cfg");
                if (!File.Exists(path))
                {
                    // Older names of this framework: PhxLib (PhxLib.cfg) and Phx Pause (Phx.cfg).
                    string[] older = { "PhxLib.cfg", "Phx.cfg" };
                    path = null;
                    for (int i = 0; i < older.Length && path == null; i++)
                    {
                        if (File.Exists(FilePath(older[i])))
                            path = FilePath(older[i]);
                    }
                    if (path == null)
                        return;
                    MelonLogger.Msg("Carrying settings over from " + Path.GetFileName(path) + ".");
                    migrated = true;
                }

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    int split = line.IndexOf('=');
                    if (split <= 0)
                        continue;
                    string key = line.Substring(0, split).Trim();
                    if (key.StartsWith("PhxLib.", StringComparison.OrdinalIgnoreCase))
                        key = "PhxCore." + key.Substring("PhxLib.".Length);
                    Values[key] = line.Substring(split + 1).Trim();
                }

                // Write PhxCore.cfg straight away, so there is a file to edit before any setting is changed in game.
                if (migrated)
                    Save();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not read settings: " + e.Message);
            }
        }

        internal static Key ReadKey(string mod, string label, Key fallback)
        {
            EnsureLoaded();
            if (Values.TryGetValue(Slot(mod, label), out string text) && Enum.TryParse(text, true, out Key key) && key != Key.None)
                return key;
            return fallback;
        }

        internal static bool Flag(string mod, string label, bool fallback)
        {
            EnsureLoaded();
            if (!Values.TryGetValue(Slot(mod, label), out string text))
                return fallback;
            if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1")
                return true;
            if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0")
                return false;
            return fallback;
        }

        internal static int ReadChoice(string mod, string label, string[] options, int fallback)
        {
            EnsureLoaded();
            if (!Values.TryGetValue(Slot(mod, label), out string text))
                return fallback;
            for (int i = 0; i < options.Length; i++)
            {
                if (string.Equals(options[i], text, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return fallback;
        }

        internal static void Save()
        {
            try
            {
                Capture(Registry.Core);
                for (int i = 0; i < Registry.All.Count; i++)
                    Capture(Registry.All[i]);

                var keys = new List<string>(Values.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);
                var lines = new List<string> { "# PhxCore settings. Change these in game: pause > PHX MODS." };
                for (int i = 0; i < keys.Count; i++)
                    lines.Add(keys[i] + "=" + Values[keys[i]]);
                File.WriteAllText(FilePath("PhxCore.cfg"), string.Join("\n", lines.ToArray()) + "\n");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not save settings: " + e.Message);
            }
        }

        static void Capture(ModEntry mod)
        {
            for (int k = 0; k < mod.Keys.Count; k++)
                Values[Slot(mod.Name, mod.Keys[k].Label)] = mod.Keys[k].Current.ToString();
            for (int f = 0; f < mod.Flags.Count; f++)
                Values[Slot(mod.Name, mod.Flags[f].Label)] = mod.Flags[f].On ? "true" : "false";
            for (int c = 0; c < mod.Choices.Count; c++)
            {
                if (mod.Choices[c].Stored)
                    Values[Slot(mod.Name, mod.Choices[c].Label)] = mod.Choices[c].Value;
            }
        }

        static string Slot(string mod, string label)
        {
            return mod + "." + label;
        }

        static string FilePath(string name)
        {
            return Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, name);
        }
    }
}
