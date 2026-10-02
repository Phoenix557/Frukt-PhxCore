# PhxCore

The shared framework for FRUKT mods by Phoenix557.
https://github.com/Phoenix557/Frukt-PhxCore

Pause the game and open **PHX MODS** to see every Phoenix557 mod you have installed and change its options. The menu is built from the game's own pause and settings screens, so it looks and animates like the rest of FRUKT.

PhxCore replaces **Phx Pause** (`Phx.dll`), **Walking** (`Walking.dll`), and **PhxLib** (`PhxLib.dll`). Walking is now built in.

## What's built in

- **Noclip key** switches between walking and noclip. Default: `N`. Change it in PHX MODS > SETTINGS > NOCLIP KEY
- **Default movement** is how you start each level. Default: `NOCLIP`. PHX MODS > SETTINGS > DEFAULT MOVEMENT
- **Ground hitboxes** adds floors so walking has something to stand on. Default: `GAME + PHXCORE` (or `GAME ONLY`). PHX MODS > SETTINGS > GROUND HITBOXES
- **Show hitboxes** outlines solid colliders near you. Default: off. PHX MODS > SETTINGS > SHOW HITBOXES
- **Display mode** is fullscreen, borderless, or windowed. PHX MODS > SETTINGS > DISPLAY MODE
- **On / off** is the plate to the left of each mod name, with a gap between them. It stages the change. **APPLY CHANGES**, to the right of the kills counter, loads or unloads those mods, then reloads the map. PhxCore cannot be turned off
- **Respawn** button inside PHX MODS
- Update check for every Phoenix557 mod you have installed

**NOCLIP** (default): you spawn flying like normal. Press **N** to drop out and walk.
**WALKING**: you spawn on your feet. Press **N** to start flying.

Walking: **WASD** or the arrow keys to move, **Shift** to sprint, **Space** to jump.

The default applies the next time a level loads, or when you press **RESPAWN**.

Show hitboxes colors: **green** is the game's own, **orange** is added by PhxCore, **blue** is a PhxCore mesh hitbox.

## Install

1. Install MelonLoader on FRUKT. Pick `FRUKT.exe` in the installer.
https://melonwiki.xyz/#/
2. Close FRUKT.
3. Put `PhxCore.dll` in `C:\Program Files (x86)\Steam\steamapps\common\FRUKT\Mods`
4. Remove `Phx.dll`, `Walking.dll`, and `PhxLib.dll` from `Mods` if they are there. PhxCore warns in the console and turns its own walking off while `Walking.dll` is present.
5. Start FRUKT, pause, and open **PHX MODS**.

Settings are saved to `UserData/PhxCore.cfg`. On first run, PhxCore copies your old values from `PhxLib.cfg` or `Phx.cfg`.

## Hooking a mod into PhxCore

Every Phoenix557 mod shows up in PHX MODS automatically. PhxCore lists any installed mod whose MelonInfo author or download link contains `Phoenix557`. To give it a settings page, register it:

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
            _toggle = mod.Key("Toggle", Key.X);
            _glow   = mod.Toggle("Glow", true);
            _color  = mod.Choice("Color", new[] { "RED", "GREEN", "BLUE" }, 0);

            _glow.Changed += on => LoggerInstance.Msg("Glow " + (on ? "on" : "off"));
        }

        public override void OnUpdate()
        {
            if (_toggle.Pressed())
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

## API

- `PhxCore.Mods.Register(name)` adds or returns the mod's page
- `mod.Key(label, Key)` returns `Hotkey` with `.Current`, `.Pressed()`, and `.Changed`
- `mod.Toggle(label, bool)` returns `Switch` with `.On` and `.Changed`
- `mod.Choice(label, string[], int)` returns `Choice` with `.Index`, `.Value`, and `.Changed`
- `PhxCore.Player.Freeze(this)` / `Unfreeze(this)` stops built-in walking and all hotkeys while held
- `PhxCore.Player.Protect(this)` / `Unprotect(this)` and `.Invulnerable` are the shared "don't hurt the player" flag
- `PhxCore.Player.Walking` / `SetWalking(bool)` reads or changes walking vs noclip
- `PhxCore.Player.Teleport(feet)` moves a walking player so their feet land at `feet`. Call it after the game's own teleport. Returns false in noclip
- `PhxCore.Player.OnRespawn(action)` runs when RESPAWN is pressed

## Build

Run `dotnet build` in this folder. References come from the FRUKT install if it exists, otherwise from `..\..\ModRefs`. When FRUKT is installed, the DLL is copied into `Mods` after each build. Close the game first.

https://buymeacoffee.com/phoenixlovesyou
