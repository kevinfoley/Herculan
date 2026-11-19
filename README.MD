![HERCULAN Engine logo](Branding/HERCULAN%20Engine%20Logo%20Solid%20600.png)

# The HERCULAN Project

The HERCULAN Project is an unofficial engine and modding toolkit for _[Metaltech: Earthsiege](https://en.wikipedia.org/wiki/Metaltech:_Earthsiege)_ (ES1) and _[Earthsiege 2](https://en.wikipedia.org/wiki/Earthsiege_2)_ (ES2), the classic mech simulator games developed by Dynamix and published by Sierra in 1994 and 1996. The source code for these games has never been released, so everything in HERCULAN was constructed through reverse engineering.

The project has two major components:

- **The HERCULAN Engine**, for playing the games on modern computers.
- **HercWorks CSharp**, a modding toolkit for viewing, and in some cases editing, the games' data files.

## The HERCULAN Engine

The HERCULAN Engine is a cross-platform reimplementation of the engine used in _Earthsiege_ and _Earthsiege 2_. It can be used with the original game files to play on current operating systems with modern joysticks and controllers.

### Features

- Play _Earthsiege_ (base game only) and _Earthsiege 2_, at any resolution, with support for widescreen/ultra-widescreen (menus render at 4:3).
- Supports modern joysticks and gamepads, with customizable bindings (the original keyboard bindings are not currently customizable).
- Hardware-accelerated rendering that almost perfectly matches the original software renderers.
- Runs on Windows and Linux, with potential macOS support in the future.
- Full support for the original file formats, including saved games.
- Compatible with real CDs and with disc images (`.iso`, `.bin`, `.cue`; no mounting required).
- Built-in installer for copying the game files from a CD or disc image to your drive.
- Video files play with custom decoders implemented in-engine - no more dependency on outdated and unsafe codecs.
- A Tweaks menu to optionally fix vanilla bugs and add minor QoL tweaks.
- Support for English, French, and German in the HERCULAN UI. In-game support requires a compatible game version; see below.
- ES2-only features:
    - Support for playing music from the original CD or a compatible disc image, and caching it to your computer.
    - Custom WinHelp (.HLP) parser; view the original "On-line manual" interactively in your browser.
    - A Mission Editor (currently in beta).

### Screenshots

<div align="center">
  <figure>
    <img src="Images/ES1%20Gameplay.png" alt="ES1 Gameplay" width="400">
    <p><em>ES1 Gameplay, piloting the Colossus.</em></p>
  </figure>
</div>

<div align="center">
  <figure>
    <img src="Images/Samson%20Cockpit.png" alt="ES2 Gameplay" width="400">
    <p><em>ES2 Gameplay, piloting the Samson.</em></p>
  </figure>
</div>

<div align="center">
  <figure>
    <img src="Images/Mission%20Editor.png" alt="Mission Editor" width="400">
    <p><em>The Mission Editor</em></p>
  </figure>
</div>

### Getting started

#### System requirements

- A 64-bit (x86-64) PC with a graphics driver that supports OpenGL 3.3.
- **Windows:** Windows 10 or later.
- **Linux:** a distribution with glibc 2.34 or newer. That covers Ubuntu 22.04, Debian 12, Fedora 35, RHEL 9 and Linux Mint 21 or later, as well as rolling distributions such as Arch and SteamOS 3 (Steam Deck). Older releases such as Ubuntu 20.04 and Debian 11 are not supported; the engine fails at startup with ``version `GLIBC_2.34' not found``. Distributions built on musl instead of glibc, such as Alpine, are not supported.

On Linux, the engine also requires the following components, included with most desktop environments:

- An OpenGL 3.3 driver: Mesa or an NVIDIA proprietary driver.
- An X11 or Wayland desktop session.
- ICU, a Unicode library (package `libicu` or `icu`, depending on the distribution).
- A sound system: PipeWire, PulseAudio or ALSA.

#### 1. Get the original game

To use the HERCULAN Engine, you will need a copy of the original game files. _Metaltech: Earthsiege_ and _Earthsiege 2_ are copyrighted works. While the legal owner of the copyrights (Hi-Rez Studios) has previously released the games for free, I do not have a license to distribute the original game files and they are not included in this repository. The original download directly from Hi-Rez Studios is no longer available, but there are mirrors and other websites which still have the files available. Always consult local law before downloading the game from third-party websites, and use caution when downloading files from "abandonware" websites, as they could be infected with malware. Alternatively, you can purchase an original retail CD-ROM (no longer manufactured, but still available from some online markets). I am not aware of any digital marketplace that sells either game.

HERCULAN supports the following versions of the original games:

- _Metaltech: Earthsiege_ version 1.0 (tested with the 1996 CD re-release; the original 1994 release should work as well). The expansion is _not_ currently supported.
- _Earthsiege 2_ version 1.0 (either the 1996 CD or the Hi-Rez ISO) and version 1.10 (the 1997 "Gold Games 2" release).

HERCULAN can use an existing install already on your computer, an original CD, or a compatible disc image (`.iso`, `.bin`, `.cue`).

**Abandonware releases which do not use the original file structure are not supported**.

#### 2. Download HERCULAN

Download the engine for your platform from the [Releases](https://github.com/kevinfoley/Herculan/releases) page. You don't need to install .NET; the runtime is built into the executable.

- **Windows:** unzip `HERCULAN-<version>-win-x64.zip` anywhere and run `Herculan.exe`. The same page has `HercWorksCSharp-<version>-win-x64.zip` for the HercWorks CSharp tools, which are Windows-only.
- **Linux:** extract `HERCULAN-<version>-linux-x64.tar.gz` anywhere (`tar xzf HERCULAN-<version>-linux-x64.tar.gz`) and run `HERCULAN/Herculan`.

#### 3. First launch

After you select which game you want to play, HERCULAN will look for an existing install. If an existing install isn't found, HERCULAN will give you the option of manually locating an existing install, or creating a new install from a CD or disc image.

An install from a disc leaves some files on the disc, such as the movies and _Earthsiege 2_'s music, so keep the CD or disc image where it is after installing.

### Playing

#### The menu bar

HERCULAN Engine has a custom menu bar which is normally hidden. To open the menu bar, do any of the following:
- While in the shell (menus), move the cursor to the top-left of the window.
- Press Shift+Esc.
- On a gamepad, press the button formerly known as Select (modern gamepads may label this "Back", "View", "-", or a variety of other names).

From the menu bar, you can access Settings, Controller, Tweaks, Accessibility, and more (some functions are disabled during gameplay).

#### Languages

HERCULAN's own UI comes with support for English, French, and German. The text lives in the `.lang` files in the `Localization` folder; custom languages can be added to this folder after installation.

Version 1.10 of _Earthsiege 2_ supports French and German in-game (though some voiceover audio is always in English); by default, HERCULAN will synchronize your language selection when possible.

### What the engine is not

HERCULAN Engine is a reimplementation, **not** a remake/remaster. There are currently **no plans** for any of the following:

- Enhanced graphics (other than basic quality of life changes like support for higher resolutions and widescreen displays)
- Gameplay changes or balance adjustments (other than bug fixes)
- New HERCs, weapons, or campaigns
- Multiplayer

Higher tickrates/smoother motion are under consideration for a future update, but are not a priority right now.

## HercWorks CSharp

HercWorks CSharp is a toolset for reading and writing data files from _Earthsiege_ and _Earthsiege 2_. It is a C#/.NET rewrite and extension of [herc-works-mdk](https://github.com/Subject9x/herc-works-mdk), originally by Subject9x. HercWorks CSharp builds upon the original Java library, fixing bugs and expanding the scope of the reverse-engineering work. Unlike the original Java library, HercWorks CSharp currently does not support exporting data to JSON.

HercWorks CSharp is used as a library by HERCULAN Engine for reading the original data files. Additionally, HercWorks CSharp is offered as a standalone application with a graphical user interface built with Windows Forms (and therefore only available on Windows devices for now).

### Screenshots

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

## Documentation

This repository includes extensive documentation on both games, most of it technical: file formats, how the original programs behave, and how HERCULAN is built. _Earthsiege_ is covered in [`docs/es1`](Herculan/docs/es1) and _Earthsiege 2_ is covered in [`docs/retail`](Herculan/docs/retail).

Some less technical docs that may interest players and fans of the series:

### Earthsiege 2

- [Key bindings](Herculan/docs/retail/key-bindings.md): every key in the original game, most of which HERCULAN implements as well.
- [Campaign continuity](Herculan/docs/retail/campaign-continuity.md): how a campaign mission's result carries forward to later missions.
- [Cut content](Herculan/docs/retail/cut-content.md): unfinished features left in the game's code and data.
- [Launch options](Herculan/docs/retail/launch-options.md): the original game's command-line options, including the all-important `-eggplant` flag.
- [Retail builds](Herculan/docs/retail/retail-builds.md): the differences between v1.0 and v1.10 (more technical).
- [Known issues](Herculan/KNOWN_ISSUES_ES2.md): bugs and other mistakes in the original game (of which there are _many_), and where HERCULAN differs.

### Metaltech: Earthsiege

- [Cut content](Herculan/docs/es1/cut-content.md): unfinished features left in the game's code and data.
- [Known issues](Herculan/KNOWN_ISSUES_ES1.md): bugs in the original game, and where HERCULAN differs.

## Building from source

Building needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). From the `Herculan` folder:

```
dotnet build HerculanEngine.sln
dotnet build HercWorksMDK.sln
```

A normal build needs the .NET 8 runtime on any machine that runs it. To build a copy of the engine that runs with no .NET installed (the same build as a release), publish it with the `win-x64` or `linux-x64` profile, from the command line or from Visual Studio's Publish page:

```
dotnet publish src/Herculan.Engine.Host -p:PublishProfile=win-x64
dotnet publish src/Herculan.Engine.Host -p:PublishProfile=linux-x64
```

The result lands in `Herculan/src/Herculan.Engine.Host/bin/publish/<profile>/`; copy that whole folder. Either profile can be published from Windows or Linux. Windows does not record Linux's execute permission, so a Linux build published on Windows needs `chmod +x Herculan` before it runs, and has to be packaged on Linux (a zip made on Windows loses the permission again).

## Project structure

- **`Herculan/`** — Main project directory
  - **`HercWorksMDK.sln`** — Visual Studio solution for HercWorks CSharp (WinForms tool)
  - **`HerculanEngine.sln`** — Visual Studio solution for the HERCULAN Engine
  - **`src/`** — Source code for HercWorks CSharp and the HERCULAN Engine
  - **`docs/`** — Technical documentation on file formats, reverse-engineering findings, and architecture
- **`tools/`** — Utility scripts and plugins (e.g., Ghidra scripts for binary analysis)
- **`Images/`** — Screenshots of the HercWorks UI and engine
- **`Branding/`** — The HERCULAN Engine logo and icon artwork (not MIT-licensed; see its README)

## Acknowledgements

Great thanks to Subject9x (developer of the original herc-works-mdk repository), Crow, and any other contributors for the initial reverse-engineering work that helped make this possible.

## AI disclosure

Development of HercWorks CSharp and HERCULAN Engine has been largely powered by Claude (with extensive prompting, testing, and feedback from a human). AI-generated documentation and code may contain mistakes. See the accompanying LICENSE file for copyright information.

Custom missions included in HERCULAN were created entirely by human hand.

## License

See the accompanying LICENSE file. The HERCULAN Engine logo and icon artwork are not covered by that license; see [Branding/README.md](Branding/README.md) for how they may be used.

HERCULAN is not affiliated with or endorsed by Hi-Rez Studios, Dynamix, or Sierra. _Metaltech_, _Earthsiege_ and related names are trademarks of their respective owners.
