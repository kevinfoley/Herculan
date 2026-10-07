![HERCULAN Engine logo](Branding/HERCULAN%20Engine%20Logo%20Solid%20600.png)

# The HERCULAN Engine

The HERCULAN Engine is a full reimplementation of _[Earthsiege 2](https://en.wikipedia.org/wiki/Earthsiege_2)_ (developed by Dynamix and published by Sierra in 1996), with support for modern operating systems, powered by .NET.

## HercWorks CSharp

A fundamental feature of the HERCULAN Engine is HercWorks CSharp, a toolset for viewing (and sometimes editing) data files from _Earthsiege 2_. HercWorks CSharp is a C#/.NET rewrite and extension of [herc-works-mdk](https://github.com/Subject9x/herc-works-mdk), originally by Subject9x. HercWorks CSharp builds upon the original Java library, fixing bugs and expanding the scope of the reverse-engineering work. Unlike the original Java library, HercWorks CSharp currently does not support exporting data to JSON. New to HercWorks CSharp is a graphical user interface, built with Windows Forms (and therefore only available on Windows devices for now).

Great thanks to Subject9x (developer of the original herc-works-mdk repository), Crow, and any other contributors for the initial reverse-engineering work that helped make this possible.

## The engine

The HERCULAN Engine is a modern reimplementation of _Earthsiege 2_. Because the source code for Earthsiege 2 has never been released, HERCULAN Engine was constructed entirely through reverse engineering.

The engine is currently in beta. Not all features have been thoroughly tested, and there may be bugs. **Back up your retail save data before switching to HERCULAN**.

### Features

- Play all of `Earthsiege 2`, at any resolution, with support for widescreen and ultra-widescreen monitors (menus render at 4:3).
- Hardware-accelerated rendering that almost perfectly matches retail's software renderer.
- Currently supports Windows, with planned support for Linux and potentially macOS in the near future.
- Full support for retail's file formats, including saved games.
- Compatible with real CDs and with ISOs (no mounting required).
- Built-in installer for copying the files from a retail CD or ISO to your drive, supporting Minimum, Medium, and Maximum installation methods.
- Support for the full retail 1.0 release, 1.0 Hi-Rez Studios release (which has no music) and the 1.10 GoldGames release.
- Support for version 1.10's French and German languages (note that many "French" and "German" voiceover files are duplicates of the original English files).
- Video files play with custom decoders implemented in-engine - no more dependency on outdated and unsafe codecs.
- Support for playing music from the original CD, and caching it to your computer.
- A Tweaks menu to optionally fix retail bugs and add minor QoL tweaks.
- Custom WinHelp (.HLP) parser; view the original "On-line manual" interactively in your browser.

### Using the engine

To use the HERCULAN Engine, you will need a copy of the original game files. _Earthsiege 2_ is a copyrighted work. While a legal owner of the copyright has previously released the game for free, I do not have a license to distribute the original game files and they are not included in this repository. The original download directly from the copyright holder is no longer available, but there are mirrors and other websites which still have the files available. Always consult local law before downloading the game from third-party websites. Use caution when downloading files from "abandonware" websites, as they could be infected with malware. Alternatively, you can purchase an original retail CD-ROM (no longer manufactured, but still available from some online markets). I am not aware of any digital marketplace that sells _Earthsiege 2_.

Download `HERCULAN-<version>-win-x64.zip` from the [Releases](https://github.com/kevinfoley/Herculan/releases) page, unzip it anywhere, and run `Herculan.exe`. You don't need to install .NET; the runtime is built into the executable. The same page has `HercWorksCSharp-<version>-win-x64.zip` for the HercWorks CSharp tools.

When you run the engine, it will ask you where _Earthsiege 2_ is installed; you'll also have the option of using HERCULAN to install the _Earthsiege 2_ files from a CD or ISO.

#### Settings and tweaks

HERCULAN Engine has a custom menu bar which is normally hidden. Press the Esc key to toggle visibility of the menu bar. It has two buttons, each which opens a new menu:

- Settings: Here you can adjust the install location or change the selected language.
- Tweaks: Here you can toggle tweaks which fix vanilla bugs or otherwise alter vanilla behavior.

The engine's own menu text lives in the `.lang` files in the `Localization` folder, which can be edited, or added to for a new language, after install. Each file names itself on its `language.name` line, and any line a translation lacks is read from `en.lang`.

### What the engine is not

HERCULAN Engine is a reimplementation of _Earthsiege 2_, **not** a remake. There are currently **no plans** for any of the following:

- Enhanced graphics (other than basic quality of life changes like support for higher resolutions and widescreen displays)
- Gameplay changes or balance adjustments (other than bug fixes)
- New Hercs, weapons, or missions
- Multiplayer

Higher tickrates/smoother motion are under consideration for a future update, but are not a priority right now.

## Screenshots

<div align="center">
  <figure>
    <img src="Images/VOL%20Browser.png" alt="VOL Browser" width="400">
    <p><em>HercWorks UI for browsing VOL archives</em></p>
  </figure>
</div>

<div align="center">
  <figure>
    <img src="Images/Campaign%20Resources%20Editor.png" alt="Campaign Resources Editor" width="400">
    <p><em>Campaign Resources Editor</em></p>
  </figure>
</div>

<div align="center">
  <figure>
    <img src="Images/DTS%20Viewer.png" alt="DTS Viewer" width="400">
    <p><em>DTS (3D model) Viewer</em></p>
  </figure>
</div>

<div align="center">
  <figure>
    <img src="Images/Herc%20Stats%20Editor.png" alt="Herc Stats Editor" width="400">
    <p><em>Herc Stats Editor</em></p>
  </figure>
</div>

<div align="center">
  <figure>
    <img src="Images/Texture%20Viewer.png" alt="Texture Viewer" width="400">
    <p><em>Texture Viewer</em></p>
  </figure>
</div>

<div align="center">
  <figure>
    <img src="Images/Mission%20Editor.png" alt="Mission Editor" width="400">
    <p><em>Editor mode, a basic debugging interface that may eventually turn into a mission editor (no promises!)</em></p>
  </figure>
</div>

<div align="center">
  <figure>
    <img src="Images/Samson%20Cockpit.png" alt="Samson Cockpit" width="400">
    <p><em>Gameplay, piloting the Samson.</em></p>
  </figure>
</div>

## Building from source

Building needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). From the `Herculan` folder:

```
dotnet build HerculanEngine.sln
dotnet build HercWorksMDK.sln
```

A normal build needs the .NET 8 runtime on any machine that runs it. To build a copy of the engine that runs on Windows with no .NET installed (the same build as a release), publish it with the `win-x64` profile:

```
dotnet publish src/Herculan.Engine.Host -p:PublishProfile=win-x64
```

The result lands in `Herculan/src/Herculan.Engine.Host/bin/publish/win-x64/`; copy that whole folder.

## Project Structure

- **`Herculan/`** — Main project directory
  - **`HercWorksMDK.sln`** — Visual Studio solution for HercWorks CSharp (WinForms tool)
  - **`HerculanEngine.sln`** — Visual Studio solution for the HERCULAN Engine
  - **`src/`** — Source code for HercWorks CSharp and the HERCULAN Engine
  - **`docs/`** — Technical documentation on file formats, reverse-engineering findings, and architecture
- **`tools/`** — Utility scripts and plugins (e.g., Ghidra scripts for binary analysis)
- **`Images/`** — Screenshots of the HercWorks UI and engine
- **`Branding/`** — The HERCULAN Engine logo and icon artwork (not MIT-licensed; see its README)

For details on specific reverse-engineering work and file format documentation, see the files in `Herculan/docs/`.

## AI Disclosure
Development of HercWorks CSharp and HERCULAN Engine has been largely powered by Claude (with extensive prompting, testing, and feedback from a human). AI-generated documentation and code may contain mistakes. See the accompanying LICENSE file for copyright information.

## License

See the accompanying LICENSE file. The HERCULAN Engine logo and icon artwork are not covered by that license; see [Branding/README.md](Branding/README.md) for how they may be used.