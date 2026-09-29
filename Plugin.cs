using MelonLoader;

[assembly: MelonInfo(typeof(PhxCore.PhxPlugin), "PhxCore", PhxCore.PhxPlugin.Version, "Phoenix557", "https://github.com/Phoenix557/Frukt-PhxCore")]
[assembly: MelonGame("tripledose", "FRUKT")]
[assembly: MelonPriority(-100)]

namespace PhxCore
{
    public class PhxPlugin : MelonMod
    {
        public const string Version = "2.1.0";

        public override void OnInitializeMelon()
        {
            Config.EnsureLoaded();
            Registry.UnloadDisabled(false);
            Movement.Init();
            Display.Register();
            HarmonyInstance.PatchAll(typeof(PhxBackPatch));
            Registry.Started = true;
            LoggerInstance.Msg("Loaded. Pause and open PHX MODS for mod settings. " + Movement.Hint);
        }

        public override void OnLateInitializeMelon()
        {
            Registry.UnloadDisabled(true);
            if (Registry.FindMelon("Phx Pause") != null)
                LoggerInstance.Warning("Phx.dll (Phx Pause) is also installed. PhxCore replaces it; remove Phx.dll from Mods to avoid two menu buttons.");
            if (Registry.FindMelon("PhxLib") != null)
                LoggerInstance.Warning("PhxLib.dll is also installed. PhxCore is the same framework renamed; remove PhxLib.dll from Mods.");
            Registry.Discover();
            Updates.Start();
        }

        public override void OnDeinitializeMelon()
        {
            Movement.Shutdown();
            Menu.Teardown();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Menu.Reset();
            Movement.SceneLoaded();
        }

        public override void OnUpdate()
        {
            Menu.Tick();
            Movement.Update();
            Updates.Tick();
        }

        public override void OnFixedUpdate()
        {
            Movement.FixedUpdate();
        }

        public override void OnLateUpdate()
        {
            Movement.LateUpdate();
            Updates.LateTick();
        }
    }
}
