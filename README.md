# PhxCore

The shared framework for FRUKT mods by [Phoenix557](https://github.com/Phoenix557). Pause the game and open **PHX MODS** to see every Phoenix557 mod you have installed and change its options. The menu is built from the game's own pause and settings screens, so it looks and animates like the rest of FRUKT.

PhxCore replaces **Phx Pause** (`Phx.dll`), **Walking** (`Walking.dll`) and **PhxLib** (`PhxLib.dll`, its old name). Walking is now built in.

## What's built in

| Feature | Default | Where to change it |
| --- | --- | --- |
| **Noclip key** – switches between walking and noclip | `N` | PHX MODS › SETTINGS › NOCLIP KEY |
| **Default movement** – how you start each level | `NOCLIP` | PHX MODS › SETTINGS › DEFAULT MOVEMENT |
| **Display mode** | – | PHX MODS › SETTINGS › DISPLAY MODE |
| **Respawn** button in the pause menu | – | – |
| Update check for all Phoenix557 mods | – | – |

- With **Default movement = NOCLIP** you spawn flying like normal. Press **N** to drop out of noclip and walk.
- With **Default movement = WALKING** you spawn on your feet. Press **N** to start flying.
- Walking: **WASD** to move, **Shift** to sprint, **Space** to jump.
- The default applies the next time a level loads (or when you press RESPAWN).

## Install

1. Install [MelonLoader](https://melonwiki.xyz/#/) on FRUKT (pick `FRUKT.exe` in the installer).
2. Close FRUKT.
3. Put `PhxCore.dll` in `C:\Program Files (x86)\Steam\steamapps\common\FRUKT\Mods`.
4. **Remove `Phx.dll`, `Walking.dll` and `PhxLib.dll`** from `Mods` if they are there. PhxCore warns in the console and turns its own walking off while `Walking.dll` is present.
5. Start FRUKT, pause, and open **PHX MODS**.

Settings are saved to `UserData/PhxCore.cfg`. On first run PhxCore copies your old values from `PhxLib.cfg` or `Phx.cfg`.

## Hooking a mod into PhxCore

Every Phoenix557 mod shows up in PHX MODS automatically (PhxCore lists any installed mod whose MelonInfo author or download link contains `Phoenix557`). To give it a settings page, register it:

```csharp
using MelonLoader;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(XRay.XRayMod), "X-Ray", "1.1.0", "Phoenix557", "https://github.com/Phoenix557/Frukt-Xray-Organs")]
[assembly: MelonGame("tripledose", "FRUKT")]
[assembly: MelonAdditionalDependencies("PhxCore")]

namespace XRay
{
    public class XRayMod : MelonMod
    {
        PhxCore.Hotkey _toggle;
        PhxCore.Switch _glow;
        PhxCore.Choice _color;

        public override void OnInitializeMelon()
        {
            var mod = PhxCore.Mods.Register("X-Ray");
            _toggle = mod.Key("Toggle", Key.X);                              // rebindable key
            _glow   = mod.Toggle("Glow", true);                              // ON / OFF
            _color  = mod.Choice("Color", new[] { "RED", "GREEN", "BLUE" }, 0); // cycles on click

            _glow.Changed += on => LoggerInstance.Msg("Glow " + (on ? "on" : "off"));
        }

        public override void OnUpdate()
        {
            if (_toggle.Pressed())   // ignores presses while the PHX menu is open
                LoggerInstance.Msg("X-Ray toggled, color " + _color.Value);
        }
    }
}
```

In the mod's `.csproj`, reference PhxCore:

```xml
<Reference Include="PhxCore">
  <HintPath>..\FruktFableMods\PhxCore\bin\Debug\PhxCore.dll</HintPath>
  <Private>false</Private>
</Reference>
```

### API

| Call | What it does |
| --- | --- |
| `PhxCore.Mods.Register(name)` | Adds (or returns) the mod's page. |
| `mod.Key(label, Key)` → `Hotkey` | `.Current`, `.Pressed()`, `.Changed` |
| `mod.Toggle(label, bool)` → `Switch` | `.On`, `.Changed` |
| `mod.Choice(label, string[], int)` → `Choice` | `.Index`, `.Value`, `.Changed` |
| `PhxCore.Player.Freeze(this)` / `Unfreeze(this)` | Stops built-in walking and all hotkeys while held. |
| `PhxCore.Player.Protect(this)` / `Unprotect(this)` / `.Invulnerable` | Shared "don't hurt the player" flag. |
| `PhxCore.Player.Walking` / `SetWalking(bool)` | Read or change walking vs. noclip. |
| `PhxCore.Player.OnRespawn(action)` | Runs when RESPAWN is pressed. |

## Build

`dotnet build` in this folder. References come from the FRUKT install if it exists, otherwise from `..\..\ModRefs`. When FRUKT is installed the DLL is copied into `Mods` after each build (close the game first).
