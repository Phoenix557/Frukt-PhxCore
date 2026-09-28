using MelonLoader;
using UnityEngine.InputSystem;

namespace PhxCore
{
    /// <summary>
    /// PhxCore's built-in movement: noclip on a hotkey (N by default) and the "Default movement" setting that picks
    /// whether the player spawns walking or flying. Both rows live on the SETTINGS page in PHX MODS.
    /// Stands down if the old Walking.dll is still installed, so the two never fight over the player.
    /// </summary>
    internal static class Movement
    {
        internal static readonly string[] Modes = { "NOCLIP", "WALKING" };

        static Walker _walker;
        static Hotkey _key;
        static Choice _default;
        static Choice _ground;
        static Switch _hitboxes;

        internal static readonly string[] GroundModes = { "GAME + PHXCORE", "GAME ONLY" };

        internal static bool GenerateGround => _ground == null || _ground.Index == 0;

        internal static bool ShowHitboxes => _hitboxes != null && _hitboxes.On;

        internal static bool WalkByDefault => _default != null && _default.Index == 1;

        internal static bool IsWalking => _walker != null && _walker.Walking;

        internal static string Hint =>
            _walker == null
                ? "Built-in walking is off because Walking.dll is installed."
                : "Press " + (_key != null ? _key.Current.ToString() : "N") + " to switch between walking and noclip.";

        internal static void Init()
        {
            _key = Registry.Core.Key("Noclip key", Key.N);
            _default = Registry.Core.Choice("Default movement", Modes, 0);
            _ground = Registry.Core.Choice("Ground hitboxes", GroundModes, 0);
            _hitboxes = Registry.Core.Toggle("Show hitboxes", false);
            _ground.Changed += _ => { if (_walker != null) _walker.RequestRebuild(); };
            _hitboxes.Changed += on => { if (!on) Hitboxes.Hide(); };

            if (Registry.FindMelon("Walking") != null)
            {
                MelonLogger.Warning("Walking.dll is installed. PhxCore has walking built in; remove Walking.dll to use it. Built-in walking is off for now.");
                return;
            }

            _walker = new Walker();
            _walker.Init(_key);
            Player.OnRespawn(_walker.Respawn);
        }

        internal static void Request(bool walking)
        {
            if (_walker != null)
                _walker.Request(walking);
        }

        internal static bool Teleport(UnityEngine.Vector3 feet, UnityEngine.Quaternion? facing)
        {
            return _walker != null && _walker.TeleportFeet(feet, facing);
        }

        internal static void Shutdown()
        {
            Hitboxes.Hide();
            Ground.Clear();
            if (_walker == null)
                return;
            Player.ForgetRespawn(_walker.Respawn);
            _walker.Shutdown();
        }

        internal static void SceneLoaded()
        {
            Hitboxes.Reset();
            if (_walker != null)
                _walker.SceneLoaded();
        }

        internal static void Update()
        {
            if (_walker != null)
                _walker.Update();
            if (ShowHitboxes)
                Hitboxes.Tick();
        }

        internal static void FixedUpdate()
        {
            if (_walker != null)
                _walker.FixedUpdate();
        }

        internal static void LateUpdate()
        {
            if (_walker != null)
                _walker.LateUpdate();
        }
    }
}
