using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MelonLoader;
using UnityEngine.InputSystem;

namespace PhxCore
{
    /// <summary>
    /// Entry point for every Phoenix557 mod. Call <see cref="Register"/> once from the mod's OnInitializeMelon,
    /// then add the options its page in the PHX MODS menu should show.
    /// <code>
    /// var mod = PhxCore.Mods.Register("X-Ray");
    /// PhxCore.Hotkey key = mod.Key("Toggle", Key.X);
    /// PhxCore.Switch glow = mod.Toggle("Glow", true);
    /// PhxCore.Choice color = mod.Choice("Color", new[] { "RED", "GREEN", "BLUE" }, 0);
    /// </code>
    /// </summary>
    public static class Mods
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ModEntry Register(string name)
        {
            if (string.IsNullOrEmpty(name))
                name = "Mod";
            ModEntry entry = Registry.GetOrAdd(name);
            entry.Link(Assembly.GetCallingAssembly());
            return entry;
        }

        /// <summary>True once PhxCore has started. Mods can check this before relying on the menu.</summary>
        public static bool Ready => Registry.Started;

        /// <summary>The PhxCore version, for mods that need a minimum.</summary>
        public static string Version => PhxPlugin.Version;
    }

    /// <summary>One mod listed in PHX MODS, with the options on its page.</summary>
    public sealed class ModEntry
    {
        public string Name { get; }
        public string Version { get; internal set; }

        internal Assembly Assembly;
        internal MelonBase Melon;

        internal readonly List<Hotkey> Keys = new List<Hotkey>();
        internal readonly List<Switch> Flags = new List<Switch>();
        internal readonly List<Choice> Choices = new List<Choice>();

        internal ModEntry(string name)
        {
            Name = name;
        }

        internal bool HasOptions => Keys.Count + Flags.Count + Choices.Count > 0;

        internal void Link(Assembly assembly)
        {
            if (assembly == null || Assembly != null)
                return;
            Assembly = assembly;
            MelonBase melon = Registry.MelonOf(assembly);
            if (melon != null)
                Melon = melon;
            if (melon?.Info != null && string.IsNullOrEmpty(Version))
                Version = melon.Info.Version;
        }

        /// <summary>A rebindable key. Poll it with <see cref="Hotkey.Pressed"/> each frame.</summary>
        public Hotkey Key(string label, Key fallback)
        {
            for (int i = 0; i < Keys.Count; i++)
            {
                if (Keys[i].Label == label)
                    return Keys[i];
            }

            var option = new Hotkey(Name, label, Config.ReadKey(Name, label, fallback));
            Keys.Add(option);
            return option;
        }

        /// <summary>An ON/OFF option.</summary>
        public Switch Toggle(string label, bool fallback)
        {
            for (int i = 0; i < Flags.Count; i++)
            {
                if (Flags[i].Label == label)
                    return Flags[i];
            }

            var option = new Switch(Name, label, Config.Flag(Name, label, fallback));
            Flags.Add(option);
            return option;
        }

        /// <summary>An option that steps through a fixed list each time it is clicked. The chosen value is saved.</summary>
        public Choice Choice(string label, string[] options, int fallback)
        {
            for (int i = 0; i < Choices.Count; i++)
            {
                if (Choices[i].Label == label)
                    return Choices[i];
            }

            if (options == null || options.Length == 0)
                options = new[] { "-" };
            var option = new Choice(Name, label, options, Config.ReadChoice(Name, label, options, fallback));
            Choices.Add(option);
            return option;
        }

        /// <summary>A choice whose value lives outside PhxCore (read fresh each time, never saved to PhxCore.cfg).</summary>
        internal Choice Linked(string label, string[] options, Func<int> read, Action<int> write)
        {
            var option = new Choice(Name, label, options, read, write);
            Choices.Add(option);
            return option;
        }
    }

    public sealed class Hotkey
    {
        public string Label { get; }
        public Key Current { get; internal set; }
        internal string Mod { get; }

        /// <summary>Raised after the player rebinds this key in the menu.</summary>
        public event Action<Key> Changed;

        internal Hotkey(string mod, string label, Key current)
        {
            Label = label;
            Current = current;
            Mod = mod;
        }

        /// <summary>True on the frame the key goes down, unless the PHX menu is open or the player is frozen.</summary>
        public bool Pressed()
        {
            if (Menu.BlocksHotkeys || Player.Frozen || Current == Key.None)
                return false;
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard[Current].wasPressedThisFrame;
        }

        internal void Set(Key key)
        {
            Current = key;
            Config.Save();
            Registry.Raise(Changed, key, Mod, Label);
        }
    }

    public sealed class Switch
    {
        public string Label { get; }
        public bool On { get; internal set; }
        internal string Mod { get; }

        /// <summary>Raised after the player flips this option in the menu.</summary>
        public event Action<bool> Changed;

        internal Switch(string mod, string label, bool on)
        {
            Label = label;
            On = on;
            Mod = mod;
        }

        internal void Set(bool on)
        {
            On = on;
            Config.Save();
            Registry.Raise(Changed, on, Mod, Label);
        }
    }

    public sealed class Choice
    {
        public string Label { get; }
        public string[] Options { get; }
        internal string Mod { get; }

        readonly Func<int> _read;
        readonly Action<int> _write;
        int _index;

        /// <summary>Raised with the new index after the player changes this option in the menu.</summary>
        public event Action<int> Changed;

        internal Choice(string mod, string label, string[] options, int index)
        {
            Mod = mod;
            Label = label;
            Options = options;
            _index = Clamp(index);
        }

        internal Choice(string mod, string label, string[] options, Func<int> read, Action<int> write)
        {
            Mod = mod;
            Label = label;
            Options = options;
            _read = read;
            _write = write;
        }

        internal bool Stored => _read == null;

        public int Index => Clamp(_read != null ? _read() : _index);

        public string Value => Options[Index];

        internal int Next()
        {
            int next = (Index + 1) % Options.Length;
            if (_write != null)
                _write(next);
            else
            {
                _index = next;
                Config.Save();
            }
            Registry.Raise(Changed, next, Mod, Label);
            return next;
        }

        int Clamp(int index)
        {
            return index >= 0 && index < Options.Length ? index : 0;
        }
    }

    /// <summary>
    /// Shared player state. Mods that move the player must stand still while Frozen is true.
    /// Each caller passes its own holder, so one mod releasing does not undo another's hold.
    /// </summary>
    public static class Player
    {
        static readonly HashSet<object> Holders = new HashSet<object>();
        static readonly HashSet<object> Shields = new HashSet<object>();
        static Action _respawn;

        public static bool Frozen => Holders.Count > 0;

        public static void Freeze(object holder)
        {
            if (holder != null)
                Holders.Add(holder);
        }

        public static void Unfreeze(object holder)
        {
            if (holder != null)
                Holders.Remove(holder);
        }

        /// <summary>While any mod holds this, nothing should hurt the player.</summary>
        public static bool Invulnerable => Shields.Count > 0;

        public static void Protect(object holder)
        {
            if (holder != null)
                Shields.Add(holder);
        }

        public static void Unprotect(object holder)
        {
            if (holder != null)
                Shields.Remove(holder);
        }

        /// <summary>True while PhxCore's built-in walking is active (noclip is off).</summary>
        public static bool Walking => Movement.IsWalking;

        /// <summary>Switch to walking (true) or noclip (false) from another mod.</summary>
        public static void SetWalking(bool walking)
        {
            Movement.Request(walking);
        }

        /// <summary>
        /// Moves a walking player so their feet land at <paramref name="feet"/>, keeping PhxCore's walking in step (a game
        /// teleport alone is undone by walking on the next frame). Returns false while the player is in noclip or not
        /// spawned yet; use the game's own teleport then.
        /// </summary>
        public static bool Teleport(UnityEngine.Vector3 feet)
        {
            return Movement.Teleport(feet, null);
        }

        /// <summary>Called when the pause menu's RESPAWN button is pressed.</summary>
        public static void OnRespawn(Action handler)
        {
            if (handler != null)
                _respawn += handler;
        }

        public static void ForgetRespawn(Action handler)
        {
            if (handler != null)
                _respawn -= handler;
        }

        internal static bool CanRespawn => _respawn != null;

        internal static void Respawn()
        {
            Action handler = _respawn;
            if (handler == null)
                return;
            foreach (Action one in handler.GetInvocationList())
            {
                try
                {
                    one();
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("Respawn failed: " + e.Message);
                }
            }
        }
    }

    internal static class Registry
    {
        internal const string Author = "Phoenix557";

        internal static readonly List<ModEntry> All = new List<ModEntry>();
        internal static bool Started;

        /// <summary>Desired running state for mods the player flipped but has not applied yet. True means ON.</summary>
        static readonly Dictionary<string, bool> Pending = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>PhxCore's own page (SETTINGS). Kept out of the mod list but saved with everything else.</summary>
        internal static readonly ModEntry Core = new ModEntry("PhxCore");

        internal static ModEntry GetOrAdd(string name)
        {
            Config.EnsureLoaded();
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Name == name)
                    return All[i];
            }

            var entry = new ModEntry(name);
            All.Add(entry);
            All.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return entry;
        }

        /// <summary>
        /// Lists Phoenix557 mods that are installed but never called Register, so every one of them shows up in PHX MODS.
        /// </summary>
        internal static void Discover()
        {
            Assembly self = typeof(Registry).Assembly;
            foreach (MelonBase melon in MelonBase.RegisteredMelons)
            {
                if (melon?.Info == null || melon.MelonAssembly == null)
                    continue;
                Assembly assembly = melon.MelonAssembly.Assembly;
                if (assembly == null || assembly == self || !IsMine(melon.Info))
                    continue;
                if (melon.Info.Name == "PhxLib" || melon.Info.Name == "Phx Pause")
                    continue;

                bool known = false;
                for (int i = 0; i < All.Count; i++)
                {
                    if (All[i].Assembly == assembly)
                    {
                        known = true;
                        break;
                    }
                }
                if (known)
                    continue;

                ModEntry entry = GetOrAdd(melon.Info.Name);
                entry.Link(assembly);
            }
        }

        internal static bool IsMine(MelonInfoAttribute info)
        {
            return Contains(info.Author) || Contains(info.DownloadLink);
        }

        static bool Contains(string text)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(Author, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static MelonBase MelonOf(Assembly assembly)
        {
            foreach (MelonBase melon in MelonBase.RegisteredMelons)
            {
                if (melon?.MelonAssembly != null && melon.MelonAssembly.Assembly == assembly)
                    return melon;
            }
            return null;
        }

        /// <summary>PhxCore itself cannot be turned off.</summary>
        internal static bool CanToggle(ModEntry mod)
        {
            return mod != null
                && mod != Core
                && mod.Assembly != typeof(Registry).Assembly
                && !string.Equals(mod.Name, "PhxCore", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(mod.Name, Core.Name, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsRunning(ModEntry mod)
        {
            return mod != null && mod.Melon != null && mod.Melon.Registered;
        }

        /// <summary>ON while the mod is loaded, OFF while it is unloaded.</summary>
        internal static string PowerWord(ModEntry mod)
        {
            if (mod == null)
                return "OFF";
            Remember(mod);
            return IsRunning(mod) ? "ON" : "OFF";
        }

        internal static bool HasPending => Pending.Count > 0;

        /// <summary>ON or OFF for the plate: the staged choice when one is waiting, otherwise the live state.</summary>
        internal static string DesiredWord(ModEntry mod)
        {
            if (mod != null && Pending.TryGetValue(mod.Name, out bool staged))
                return staged ? "ON" : "OFF";
            return PowerWord(mod);
        }

        /// <summary>
        /// Records a flip without loading or unloading. Clicking back to the live state drops the mod from the pending set.
        /// </summary>
        internal static void StageToggle(ModEntry mod)
        {
            if (!CanToggle(mod))
                return;
            Remember(mod);
            bool live = IsRunning(mod);
            bool current = Pending.TryGetValue(mod.Name, out bool staged) ? staged : live;
            bool next = !current;
            if (next == live)
                Pending.Remove(mod.Name);
            else
                Pending[mod.Name] = next;
        }

        /// <summary>Loads and unloads every staged mod. A mod that fails to load is logged and the rest still apply.</summary>
        internal static void ApplyPending()
        {
            if (Pending.Count == 0)
                return;

            var names = new List<string>(Pending.Keys);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (!Pending.TryGetValue(name, out bool wantOn))
                    continue;
                ModEntry mod = FindEntry(name);
                if (mod == null || !CanToggle(mod))
                    continue;
                try
                {
                    Remember(mod);
                    if (wantOn == IsRunning(mod))
                        continue;
                    TogglePower(mod);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("Could not apply " + name + ": " + e.Message);
                }
            }
            Pending.Clear();
        }

        /// <summary>Flips the mod immediately. Off unloads it. On loads that same mod again.</summary>
        internal static void TogglePower(ModEntry mod)
        {
            if (!CanToggle(mod))
                return;
            Remember(mod);
            if (IsRunning(mod))
                Disable(mod);
            else
                Enable(mod);
        }

        static void Remember(ModEntry mod)
        {
            if (mod.Melon != null && mod.Melon.Registered)
                return;
            MelonBase melon = mod.Assembly != null ? MelonOf(mod.Assembly) : null;
            if (melon == null)
                melon = FindMelon(mod.Name);
            if (melon != null)
                mod.Melon = melon;
        }

        /// <summary>
        /// Unloads mods the player turned off, before their OnInitialize runs when possible.
        /// Pass dropMissing on the late pass so a mod that is no longer installed is forgotten.
        /// </summary>
        internal static void UnloadDisabled(bool dropMissing)
        {
            var names = new List<string>();
            Config.CollectDisabled(names);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (string.Equals(name, "PhxCore", StringComparison.OrdinalIgnoreCase) || string.Equals(name, Core.Name, StringComparison.OrdinalIgnoreCase))
                {
                    Config.SetDisabled(name, false);
                    continue;
                }

                MelonBase melon = FindMelon(name);
                if (melon == null)
                {
                    ModEntry existing = FindEntry(name);
                    if (existing != null && existing.Melon != null)
                        continue;
                    if (dropMissing)
                        Config.SetDisabled(name, false);
                    continue;
                }

                if (melon.MelonAssembly != null && melon.MelonAssembly.Assembly == typeof(Registry).Assembly)
                    continue;
                if (!(melon is MelonMod))
                    continue;

                ModEntry entry = FindEntry(name) ?? GetOrAdd(melon.Info != null ? melon.Info.Name : name);
                if (entry.Assembly == null && melon.MelonAssembly != null)
                    entry.Link(melon.MelonAssembly.Assembly);
                entry.Melon = melon;
                if (melon.Info != null && string.IsNullOrEmpty(entry.Version))
                    entry.Version = melon.Info.Version;

                if (!melon.Registered)
                    continue;

                try
                {
                    melon.Unregister("Disabled in PHX MODS.", true);
                    MelonLogger.Msg(entry.Name + " is off.");
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("Could not unload " + entry.Name + ": " + e.Message);
                }
            }
        }

        static void Disable(ModEntry mod)
        {
            Config.SetDisabled(mod.Name, true);
            MelonBase melon = mod.Melon;
            if ((melon == null || !melon.Registered) && mod.Assembly != null)
                melon = MelonOf(mod.Assembly);
            if (melon == null)
                melon = FindMelon(mod.Name);
            if (melon != null)
                mod.Melon = melon;

            if (melon == null || !melon.Registered)
                return;

            try
            {
                melon.Unregister("Disabled in PHX MODS.", true);
                MelonLogger.Msg(mod.Name + " is off.");
            }
            catch (Exception e)
            {
                MelonLogger.Warning("Could not unload " + mod.Name + ": " + e.Message);
            }
        }

        static void Enable(ModEntry mod)
        {
            MelonBase melon = mod.Melon;
            if (melon == null && mod.Assembly != null)
                melon = MelonOf(mod.Assembly);
            if (melon == null)
                melon = FindMelon(mod.Name);
            if (melon == null)
            {
                MelonLogger.Warning("Could not load " + mod.Name + ".");
                return;
            }

            mod.Melon = melon;
            if (!melon.Registered)
            {
                try
                {
                    if (!melon.Register())
                    {
                        MelonLogger.Warning("Could not load " + mod.Name + ".");
                        return;
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("Could not load " + mod.Name + ": " + e.Message);
                    return;
                }
            }

            Config.SetDisabled(mod.Name, false);
            MelonLogger.Msg(mod.Name + " is on.");
        }

        static ModEntry FindEntry(string name)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return All[i];
            }
            return null;
        }

        internal static MelonBase FindMelon(string name)
        {
            foreach (MelonBase melon in MelonBase.RegisteredMelons)
            {
                if (melon?.Info != null && string.Equals(melon.Info.Name, name, StringComparison.OrdinalIgnoreCase))
                    return melon;
            }
            return null;
        }

        internal static void Raise<T>(Action<T> handler, T value, string mod, string label)
        {
            if (handler == null)
                return;
            foreach (Action<T> one in handler.GetInvocationList())
            {
                try
                {
                    one(value);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("" + mod + " could not apply " + label + ": " + e.Message);
                }
            }
        }
    }
}
