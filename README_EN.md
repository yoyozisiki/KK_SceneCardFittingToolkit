[简体中文](README.md) | [English](README_EN.md)

# Scene Card Fitting Toolkit

A BepInEx plugin for Koikatsu Sunshine (KKS) CharaStudio.

Automatically batch-load scene cards, replace characters and outfits, apply post-processing effects, and record scenes into MP4 videos. Supports Timeline-based duration control, fade-in/fade-out effects, and automatic video merging.

---

## Features

| Feature | Description |
|---|---|
| **Batch Video Export** | Select any number of scene cards and automatically export them as MP4 videos. Optionally merge all clips into a single video. |
| **Character Replacement** | Replace all female characters in a scene with a specified character card. Body shape, breast size, skeleton, and height can independently inherit settings from either the original scene or the replacement character card. |
| **Outfit Customization** | Apply outfit presets before exporting. Each clothing component can be configured as **Worn / Partially Removed / Removed**. |
| **Timeline-based Duration** | Automatically use the Timeline duration when available. If no Timeline exists or its duration is too short, the configured minimum duration is used, with playback looping as necessary. |
| **Fade In / Fade Out** | Record additional footage and overlay black-screen transitions at the beginning and end of each clip. |
| **Mosaic / Auxiliary Object Removal** | Automatically remove mosaic materials and hide auxiliary lighting and shadow spheres. |
| **Real-time ReShade Effects** | Apply bloom and vignette effects in real time on the GPU through ReShade. |
| **Scene Card Browser** | Browse scenes directly from the main panel. Supports next/previous scene, random scene selection, and random character replacement. |

---

## Installation

1. Make sure you have the 64-bit version of Koikatsu Sunshine installed.
2. Copy `KK_SceneCardFittingToolkit.dll` into `BepInEx/plugins/`.
3. Launch **CharaStudio.exe**, not the main game executable.

### Dependencies

| Component | Required | Purpose |
|---|---|---|
| BepInEx 5 (x64) | **Required** | Plugin loading and configuration |
| VideoExport | **Required** | Actual MP4 recording and encoding. Export functionality is unavailable without it. |
| KKAPI (KKSAPI) | **Required** | CharaStudio integration, scene loading events, and toolbar integration |
| ReShade + `reshade/KK_BloomVignette.fx` | Optional | Real-time bloom and vignette effects |
| Timeline | Optional | Reads scene duration. Without it, the configured minimum recording duration is used. |

---

## Quick Start

1. Launch CharaStudio and click the **dark-red film icon** in the bottom-left toolbar to open the plugin panel.
2. Browse your scene card directories in the left panel and select the scenes you want to export. You can also select entire folders.
3. Configure the replacement character card, recording duration, frame rate, and other export settings in the right panel.
4. Set the output directory at the bottom of the panel.
5. Click **Start Export** to begin batch rendering.

---

## Frequently Asked Questions

**Q: Why does the hair fly upward or the skirt explode at the beginning of a recording?**

A: Dynamic bones need time to stabilize after character replacement. Increase the **Wait After Character Replacement** setting, for example, from 3 to 5 seconds.

**Q: Why is the export button disabled, or why does the plugin report that VideoExport cannot be found?**

A: VideoExport may not be installed or may not have finished initializing. Make sure `VideoExport.dll` exists in `BepInEx/plugins/` and that VideoExport has initialized successfully.

**Q: Why are other plugins' interfaces visible in the exported video?**

A: The exporter automatically hides UI-only components, including Canvas and pure IMGUI elements. However, plugins that combine their interface with game logic cannot always be hidden automatically. Close those plugin panels manually before exporting.

**Q: Can the plugin record multiple Timeline sequences from a single scene card?**

A: No. The plugin maintains only one Timeline state at a time. Each scene card produces one video clip.

**Q: How do I change the bloom / vignette strength?**

A: Press `Home` to open the ReShade overlay and drag the sliders; `[OVERLAY] AutoSavePreset=1` writes them back to the preset automatically.
This plugin is read-only towards ReShade: exporting never overwrites your values and never toggles the effect for you. The export log prints the current values and whether the technique is enabled.

**Q: Where can I edit the configuration file?**

A: The configuration file is located at:

`BepInEx/config/com.kkpeheightlock.cfg`

Each configuration option includes a description. Settings available in the plugin interface should preferably be configured through the interface.

---

## How It Works

- Scene cards are loaded automatically in sequence.
- Female characters can be replaced with a specified character card.
- Outfit presets and individual clothing states are applied before recording.
- Timeline playback determines the recording duration when available.
- VideoExport handles actual video capture and MP4 encoding.
- ReShade applies bloom and vignette effects directly during rendering.

**No FFmpeg post-processing is required for ReShade effects.** The recorded MP4 already contains the final real-time post-processing results.

---

## Dependencies and Credits

This project builds upon the work of several third-party projects. All external dependencies are listed below.

**This repository does not include or redistribute any third-party binary files.** Please obtain and install each dependency from its respective official source.

All third-party components remain the property of their respective authors and are distributed under their own licenses.

The [LICENSE](LICENSE) file in this repository applies only to the original code and documentation of this project.

### Runtime Dependencies

| Project | Author / Source | Purpose |
|---|---|---|
| **BepInEx 5** | BepInEx Team · https://github.com/BepInEx/BepInEx | Plugin loading framework, configuration system, and logging |
| **HarmonyX** (`0Harmony`) | BepInEx Team · https://github.com/BepInEx/HarmonyX | Runtime method patching, including height locking and body shape preservation during character replacement |
| **IllusionModdingAPI / KKSAPI** | IllusionMods · https://github.com/IllusionMods/IllusionModdingAPI | CharaStudio character interfaces, `SceneLoad` events, toolbar buttons, and character card operations |
| **VideoExport** | Community plugin (DeathWeasel / KK_Plugins series) | Actual video recording, MP4 encoding, and frame-based recording limits |
| **Timeline** | Community plugin | Timeline playback, pausing, seeking, and duration retrieval |
| **KKSPE / HSPE** | Community plugin (`HSPE.AMModules` namespace) | Height bone (`cf_n_height`) and skeleton editor type references |
| **ReShade** | crosire · https://reshade.me | Real-time post-processing host for GPU-based bloom and vignette effects |

### Host Application

Koikatsu Sunshine / CharaStudio © Illusion.

This project is not affiliated with, endorsed by, or officially associated with Illusion or its developers and publishers.

This repository does not contain any game assets or proprietary game resources.

---

## License

**STRICTLY NON-COMMERCIAL.**

See [LICENSE](LICENSE) for the complete terms.

### Permitted

- Free downloading and free use.
- Free redistribution of the original, unmodified archive or links to the original repository.
- Personal modifications for private use only.

### Prohibited

- Selling this plugin in any form.
- Paid downloads, paywalls, membership-exclusive distribution, or bundling it with commercial products.
- Uploading the plugin to file-sharing services or websites that require payment, advertisements, or points/credits to download.
- Using this plugin as part of paid commissions, commercial services, or other revenue-generating activities.
- Any other form of commercial exploitation.

**If you obtained this plugin through a paid distribution channel, you have likely been scammed. Request a refund and report the seller.**

---

## Language Support

The plugin interface supports the following languages:

- Simplified Chinese
- English

The default UI language follows your system locale. You can also manually select Chinese or English in the plugin settings.