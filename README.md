# Gcotu Stationeers CAD Style

**Stationeers drawn like a CAD model: black outlines, flat shading, solid-color ground. Light stops leaking through walls, and machines light the rooms.**

<img alt="Inside a Stationeers base with CAD Style: black outlines, flat-shaded machines, colored device lights" src="./docs/images/hero.jpg" width="100%" />

Install it, start the game, and the look is there. The defaults are tuned, so there is nothing you have to set. Press **F9** at any time to compare with the original game.

**[⬇ Download GcotuStationeersCADStyle.zip](https://github.com/God-creator-of-the-universe/Gcotus-Stationeers-CAD-Style/releases/latest/download/GcotuStationeersCADStyle.zip)**  ·  [Quick install](#quick-install)  ·  [Step-by-step install](#step-by-step-install)  ·  [Keys and settings](#keys-and-settings)  ·  [Problems?](#troubleshooting)

## What it looks like

<img alt="Animated comparison: a divider sweeps across tanks and pipes, from the original game to CAD Style and back" src="./docs/images/wipe-machines.webp" width="100%" />

Every pair below is the same moment from the same spot, with **F9** pressed in between.

**Light stays in its room.** In the original game, lamp light and sunlight leak through walls into dark rooms. With CAD Style this corridor stays dark, and the computer screen lights it with a soft, moody glow.

| Original game | With CAD Style |
| --- | --- |
| <img alt="Original game: a dark corridor lit by lamp light leaking through the walls" src="./docs/images/light-leak-original.jpg" /> | <img alt="CAD Style: the same corridor stays dark, lit only by the screen inside it" src="./docs/images/light-leak-cad.jpg" /> |

**Sunlight stops at walls too.** In the original game, sunlight leaks through the wall into its shadow.

| Original game | With CAD Style |
| --- | --- |
| <img alt="Original game: a wall casting a shadow, with sunlight leaking through it onto the ground" src="./docs/images/landscape-original.jpg" /> | <img alt="CAD Style: the same wall in flat colors, its shadow clean" src="./docs/images/landscape-cad.jpg" /> |

**Machines in clean lines.**

| Original game | With CAD Style |
| --- | --- |
| <img alt="Original game: tanks and pipes beside an astronaut" src="./docs/images/machines-original.jpg" /> | <img alt="CAD Style: the same tanks and pipes with black outlines and flat shading" src="./docs/images/machines-cad.jpg" /> |

**Doors and devices light the room.**

<img alt="Animated comparison: a divider sweeps across a dark machine room, from the original game to CAD Style, where the devices light the room green and blue" src="./docs/images/wipe-devices.webp" width="100%" />

| Original game | With CAD Style |
| --- | --- |
| <img alt="Original game: a large door with a red status light" src="./docs/images/door-original.jpg" /> | <img alt="CAD Style: the same door, its red light glowing onto the floor" src="./docs/images/door-cad.jpg" /> |

**Fire lights the world.**

| Original game | With CAD Style |
| --- | --- |
| <img alt="Original game: a cloud of burning sparks over dark ground" src="./docs/images/fire-original.jpg" /> | <img alt="CAD Style: the same fire lighting the ground beneath it" src="./docs/images/fire-cad.jpg" /> |

**Solid-color ground.**

| Original game | With CAD Style |
| --- | --- |
| <img alt="Original game: a base on a gray, textured plain under a giant gas planet" src="./docs/images/ground-original.jpg" /> | <img alt="CAD Style: the same base on flat, clean ground with outlined hills" src="./docs/images/ground-cad.jpg" /> |

**Your flashlight while mining.**

| Original game | With CAD Style |
| --- | --- |
| <img alt="Original game: a dark mining tunnel, barely visible in the flashlight" src="./docs/images/flashlight-original.jpg" /> | <img alt="CAD Style: the same tunnel, its walls and the mining drill outlined in the flashlight" src="./docs/images/flashlight-cad.jpg" /> |

<img alt="The CAD Style settings window beside an astronaut on the planet surface" src="./docs/images/window.jpg" width="60%" />

## Features

### The look

- **Black outlines** on every edge and crease, at the width you choose.
- **Flat facets.** Surfaces are shaded by their angle, with no shine, like a technical drawing.
- **Light bands.** Light falls off in clear steps instead of smooth gradients.
- **Solid-color terrain.** Ground and rock in flat colors.
- **Glow** around lights and screens, in their own color.
- **Eye adaptation.** A bright screen glares from across the room and becomes readable as you walk up to it.
- **Blue-noise dithering** dissolves the color banding the game shows in dark gradients. It also works with the look turned off, so you can use it on its own.

### The light

- **No more light through walls.** Lamp and sun light stop at walls instead of leaking into the next room.
- **Machines light their surroundings:** screens, fabricators, batteries, furnaces, heaters, doors and more.
- **Fire and engines:** burning gas, burning items and firing rocket engines light the world around them.
- **Your flashlight** lights the scene in the new look.

<details>
<summary>Every light source the mod adds</summary>

Computers and other devices with a screen, fabricators, consoles, vending machines, station batteries (in the color of their charge), arc furnaces (green when on, orange while smelting), furnaces, power controllers, active and powered vents, ice crushers, wall heaters, pipe heaters, transformers, LED displays, doors and airlocks, the trader waypoint, rocket engines, burning gas, burning items, and your flashlight.

</details>

### Controls and settings

- **F9 switches the whole look on and off** in an instant. Off means fully off, apart from blue noise, which stays unless you switch it off in the **Look** tab.
- **Settings window (F11)** with five tabs. Every value is a slider with tick marks at round numbers.
- **Four preset slots** for your own looks: hold one second to save, click to load. **Defaults** and **Undo** are one click away.
- **Status tab:** at every start the mod checks the game features it relies on. After a game update you see at once whether something stopped working.
- **Measure the cost:** an 18-second test shows how much frame time the look takes on your machine.
- **The game pauses** while the settings window is open (single player).
- **Rebind keys** in `keys.txt`.
- **Updates keep your settings, presets and keys.**

## Requirements

- Stationeers on Windows, running with **DirectX 11 only** (the game's default). Vulkan, DirectX 12 and OpenGL are not supported.
- [BepInEx 5](https://github.com/BepInEx/BepInEx) and [StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad)

## Compatibility

- **Lighting mods:** CAD Style does its own lighting. Turn off other mods that change the game's lighting (SEGI-based mods, for example) while you use it.
- **Volumetric light:** the look switches the game's volumetric light off by default. You can turn it back on in the **Advanced** tab.
- **Tested with** Stationeers 0.2.6429.27802 (October 2026) at the game settings below. Other settings should work, but haven't been tried.

<details>
<summary>Game settings used for testing</summary>

| Video | | Advanced | |
| --- | --- | --- | --- |
| Resolution | 3840 × 2160 | Terrain Tessellation | Medium |
| Refresh Rate | 60 Hz | Render Distance | Extreme |
| Screen Mode | Full Screen | Terrain Detail | Medium |
| V-Sync | On | Minable Render Distance | Medium |
| Brightness | 100 | Shadows | Extreme |
| Field Of View | 100 | Distant Shadows | On |
| Overall Quality | Fantastic | Antialiasing Method | TAA |
| | | Pixel Light Count | 15 |
| | | Texture Quality | Very High |
| | | Volumetric Lighting | High |
| | | Target Framerate | Off |
| | | Weather Quality | High |
| | | Lens Flares | On |

</details>

## Quick install

For those who have modded Stationeers before:

1. Install BepInEx 5.4 (x64) and [StationeersLaunchPad](https://github.com/StationeersLaunchPad/StationeersLaunchPad) (not together with StationeersMods).
2. Extract [`GcotuStationeersCADStyle.zip`](https://github.com/God-creator-of-the-universe/Gcotus-Stationeers-CAD-Style/releases/latest/download/GcotuStationeersCADStyle.zip) into `Documents\My Games\Stationeers\mods`. Result: `mods\GcotuStationeersCADStyle\GcotuStationeersCADStyle.dll`.
3. Start the game. **F9** look on/off, **F11** settings.

## Step-by-step install

For your first mod. It takes about ten minutes. Steps 1 and 2 are needed only once; for every later mod you only repeat step 3.

**Before you start:**

- **How to extract a zip to the right place:** right-click the zip → **Extract All…** → **Browse…**, pick the folder named in the step, and check the destination box. Windows likes to add the zip's name at the end; delete that part, so the box ends with the folder you picked. Each step below has a check, so you will know if it went wrong.
- Windows usually hides file extensions, so `rocketstation.exe` appears as just `rocketstation`. That's fine.

### 1. Install BepInEx (the mod loader)

1. Download [`BepInEx_win_x64_5.4.23.5.zip`](https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip). It has to be version 5 for Windows x64, not version 6.
2. In Steam, right-click **Stationeers** → **Manage** → **Browse local files**. This opens the game folder, the one with `rocketstation.exe` in it.
3. Extract the zip into this game folder.
   **Check:** next to `rocketstation` you now see a `BepInEx` folder and the files `doorstop_config` and `winhttp`.
4. Start the game once and quit at the main menu. BepInEx uses this first start to create its folders.
   **Check:** a `plugins` folder now exists inside `BepInEx`. If not, the files are probably one folder too deep; repeat item 3.

### 2. Install StationeersLaunchPad (loads Stationeers mods)

If you used StationeersMods before, remove its folder from `BepInEx\plugins` first. The two can't be installed together.

1. Open the [StationeersLaunchPad releases](https://github.com/StationeersLaunchPad/StationeersLaunchPad/releases/latest). Under **Assets**, download the **client** zip (`StationeersLaunchPad-client-…zip`), not "Source code" and not the server zip.
2. Extract it into the `BepInEx\plugins` folder inside the game folder.
3. Start the game.
   **Check:** a LaunchPad box appears at the bottom of the loading screen. That means it's working.

### 3. Install CAD Style

1. Download [`GcotuStationeersCADStyle.zip`](https://github.com/God-creator-of-the-universe/Gcotus-Stationeers-CAD-Style/releases/latest/download/GcotuStationeersCADStyle.zip).
2. Find your Stationeers mods folder. In File Explorer, click **Documents** in the left pane (it may sit under OneDrive, or carry a name in your language), then open `My Games\Stationeers`. Your `saves` folder is there too. Open the `mods` folder; if there is none yet, create it.
3. Extract the zip into the `mods` folder.
   **Check:** this file exists:
   `Documents\My Games\Stationeers\mods\GcotuStationeersCADStyle\GcotuStationeersCADStyle.dll`
   If there is one folder too many in between, move the `GcotuStationeersCADStyle` folder up into `mods`.
4. Start the game. At the main menu the settings window opens, and the game already has the new look.
   If it doesn't, click the LaunchPad box at the bottom of the loading screen and make sure the mod is enabled.

### Updating

Close the game. Extract the new zip the same way and choose **Replace the files in the destination**. Your settings, presets and keys stay, because the zip doesn't contain them. Don't delete the old folder first: your settings live in it.

### Uninstalling

Delete the `GcotuStationeersCADStyle` folder from `mods`.

## Keys and settings

| Key | Action |
| --- | --- |
| **F9** | Look on / off |
| **F11** | Open / close the settings window |
| **Esc** | Close the settings window |

To change a key, click **Open mod folder** on the **General** tab and edit `keys.txt`. Key names are [Unity's](https://docs.unity3d.com/ScriptReference/KeyCode.html), such as `F10`, `Home` or `Keypad5`; number keys are `Alpha1` to `Alpha0`. Mouse buttons can't be used. Changes take effect at the next game start. Delete `keys.txt` to get the defaults back.

### The settings window

- **General:** keys, when the window opens by itself, presets.
- **Look:** lines, facets, light bands, glow, terrain.
- **Lights:** strength and reach of each kind of light the mod adds.
- **Advanced:** fine control over lines, shading direction, glow color, eye adaptation, the game's own effects, and the cost measurement.
- **Status:** which features work in your game version.

The title bar has a look on/off switch and a **Pause** switch.

**Presets** hold your look and light values. Hold a slot for one second to save into it; click it to load. To empty a slot, click **Delete**, then hold the slot for three seconds. **Defaults** brings back the mod's tuned values. **Undo** reverses the last load, save, delete or **Defaults** (a load or **Defaults** only until you change a value). When your values differ from the loaded preset, the bottom line of the window says so.

**At the main menu** the window opens by itself. On the **General** tab you can have it open at every start (the default), only when something on the **Status** tab has changed, or never. **Not again today** skips it for the rest of the day.

**Measuring the cost:** on the **Advanced** tab, click **Measure** and stand still. For 18 seconds the look switches on and off while the mod times each frame.

## Troubleshooting

**No LaunchPad box on the loading screen.** BepInEx or StationeersLaunchPad isn't installed correctly. Go through the checks in steps 1 and 2 again.

**The LaunchPad box is there, but F9 and F11 do nothing.** The mod isn't loaded. Check the path from step 3. Then click the LaunchPad box while the game loads and make sure the mod is enabled.

**The settings window opens, but the look doesn't appear.** Open the **Status** tab; it says what is missing and why.
- *Shader files not found:* the `Content` folder must sit next to `GcotuStationeersCADStyle.dll`. Extract the whole zip again.
- *Made for DirectX 11:* the game runs with another graphics mode, usually because of a launch option such as `-force-vulkan`, `-force-d3d12` or `-force-glcore`. Remove it in Steam (right-click Stationeers → **Properties** → **Launch options**).

**Something stopped working after a game update.** Check the **Status** tab, then [open an issue](https://github.com/God-creator-of-the-universe/Gcotus-Stationeers-CAD-Style/issues).

**A key does nothing.** If its name in `keys.txt` is misspelled, the **General** tab shows that in red. Otherwise another mod may use the same key; pick a different one.

**Reporting a problem:** [open an issue](https://github.com/God-creator-of-the-universe/Gcotus-Stationeers-CAD-Style/issues) and attach `cad_style_log.txt` from the mod folder. Please mention your game version and what you did when it happened.

## Building from source

For developers. The source is in [`mod/`](mod/). No Unity Editor is involved: the shaders are plain HLSL, compiled with `d3dcompiler_47` and packed into asset bundles by `build_look.py` (Python with UnityPy and lz4). The plugin is compiled with Roslyn against the game's and BepInEx's DLLs (`build_plugin.ps1`). `build_and_deploy.ps1` runs both and copies the result into the mods folder (`-NoDeploy` only builds).

You need Python 3 with `UnityPy` and `lz4`, the .NET SDK, and the game with BepInEx installed. The scripts find the game through Steam and the compiler in your .NET SDK. For other locations, pass `-GameDir`, `-Dotnet` or `-Csc`, set `STATIONEERS_DIR`, `DOTNET_EXE` or `CSC_DLL`, or put them in a `build_paths.local.ps1` next to the scripts (git ignores that file); see `build_paths.ps1`.

## Changelog

- **1.0.1:** when the game runs with Vulkan or OpenGL instead of DirectX 11, the look now switches itself off cleanly and the **Status** tab says why.
- **1.0.0:** first public release.

Every version with its download: [Releases](https://github.com/God-creator-of-the-universe/Gcotus-Stationeers-CAD-Style/releases).

## How it started

For a [video on my channel](https://www.youtube.com/watch?v=Cff-GotUdDg), I rendered Stationeers machines in a CAD style. Under that video, YouTube viewer **maxros182** wrote: *"why do objects with that outline effect look so dam good, i wish it just had that all the time"*. Now it does.

<a href="https://www.youtube.com/watch?v=Cff-GotUdDg"><img alt="Watch the video on YouTube: Stationeers News, engine swap, with the CAD-style render" src="./docs/images/video-brutal-cad-render.jpg" width="60%" /></a>

<img alt="The YouTube comment by maxros182 that started this mod, other commenters blacked out" src="./docs/images/comment-that-started-it.jpg" width="80%" />

## Credits

- **maxros182** (YouTube): the comment that started it all.
- **Christoph Peters:** the [blue-noise texture](https://momentsingraphics.de/BlueNoise.html) used for dithering (CC0).
- **BepInEx** and **StationeersLaunchPad:** the tools that make Stationeers mods possible.
- **Thanks** to TheRealBeef, Sonic Ether, Erdroy and Vinus. Working on my fork of their [SEGI Plus](https://github.com/God-creator-of-the-universe/Gcotus-Beef-Stationeers-SEGI-Plus) taught me much of what went into this mod. CAD Style is new code and contains none of theirs.

## License

[MIT](LICENSE). The blue-noise texture is CC0.

---

Made by Gcotu · [YouTube: God, creator of the universe](https://www.youtube.com/@OwnerOfTheCosmos)
