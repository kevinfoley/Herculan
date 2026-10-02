# The HERCULAN Engine

The HERCULAN Engine is an effort to fully reverse-engineer the classic video game _[Earthsiege 2](https://en.wikipedia.org/wiki/Earthsiege_2)_ (developed by Dynamix and published by Sierra in 1996) and port it to modern operating systems. This engine is still in the very early stages of development; there is no specific development timetable.

## HercWorks CSharp

A fundemental feature of the HERCULAN Engine is HercWorks CSharp, a toolset for viewing (and sometimes editing) data files from _EarthSiege 2_. HercWorks CSharp is a rewrite and extension of [herc-works-mdk](https://github.com/Subject9x/herc-works-mdk), originally by Subject9x. HercWorks has been rewritten in C#/.NET. HercWorks CSharp builds upon the original Java library, fixing bugs, expanding the scope of the reverse-engineering work, and adding a graphical user interface built with Windows Forms. Unlike the original Java library, this repository currently does not support exporting data to JSON.

My great thanks to Subject9x (developer of the original repository) and any contributors (e.g. the Starsiege Community) for the initial reverse-engineering work that has made this possible. Also, I apologize for taking their work and continuing it in a completely different programming language!

## The engine

The HERCULAN Engine is intended to eventually be a playable cross-platform modern reimplementation of _Earthsiege 2_. Unlike many classic games which have been ported to modern operating systems, such as _Doom_, the source code for _Earthsiege 2_ has never been released. This means the HERCULAN Engine is not simply a matter of taking the original code and updating it for modern systems; instead, the game logic in its entirety has to be reconstructed through reverse-engineering.

### Using the engine

_Earthsiege 2_ is a copyrighted work. While a legal owner of the copyright has previously released the game for free, I do not have a license to distribute the original game files and they are not included in this repository. To use the HERCULAN Engine, you will need a copy of the original game files, which you can acquire from abandonware websites or by purchasing an original retail CD-ROM. I am not aware of any digital marketplace that sells _Earthsiege 2_.

#### Telling the engine where the game files are

HERCULAN Engine needs a folder containing an Earthsiege 2 installation in order to run. It tries to find the Earthsiege 2 directory using several methods. In order:

1. **A path on the command line**, e.g. `Herculan.Engine.Host "D:\Games\ES2"`. If that folder has no `VOL` folder in it, the engine stops with an error rather than looking anywhere else.
2. **The `ES2_GAME_PATH` environment variable.**
3. **The folder it used last time.** The engine remembers whichever folder it ran with in `install-path.txt`, in its settings folder (`%APPDATA%\Herculan\` on Windows). If that folder has since been moved or deleted, the engine skips it.
4. **A folder named `ES2`** beside the engine or in any folder above it. This is what finds the game files when running the engine from a checkout of this repository.
5. **Asking you.** If none of the above works, the engine opens a small window where you can browse for the folder with your system's folder picker, or type or paste its path. On Linux the picker needs `zenity` or `kdialog` to be installed; without either, you type the path.

To switch to a different copy of the game, pass its path on the command line once; the engine then remembers it. Deleting `install-path.txt` makes the engine search again.

Run `Herculan.Engine.Host --help` for the rest of the command line, or see [`Herculan/docs/engine/host-flags.md`](Herculan/docs/engine/host-flags.md).

#### Building a release that runs without .NET installed

Building the engine needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). A normal build also needs the .NET 8 runtime on any machine that runs it. To build a copy that runs on a Windows machine with no .NET installed, publish it with the `win-x64` profile, from the `Herculan` folder:

```
dotnet publish src/Herculan.Engine.Host -p:PublishProfile=win-x64
```

The result lands in `Herculan/src/Herculan.Engine.Host/bin/publish/win-x64/`. Copy that whole folder; it contains:

- `Herculan.Engine.Host.exe`, with the .NET runtime built into it
- `glfw3.dll`, `soft_oal.dll` and `cimgui.dll`, the window, audio and debug-UI libraries
- the `Localization` folder, whose `.lang` files hold the engine's menu text and can be edited or added to after install
- the `Assets` folder, with the font the engine's menus use

The `.pdb` files in the folder are debugging symbols; the engine runs without them. For another platform, publish with a different runtime instead of the profile, e.g. `dotnet publish src/Herculan.Engine.Host -c Release -r linux-x64 --self-contained`.

### What the engine is not

HERCULAN Engine is a reimplementation of _Earthsiege 2_, **not** a remake. There are currently no plans for any of the following:

- Enhanced graphics (other than basic quality of life changes like support for higher resolutions and widescreen displays)
- Gameplay changes or balance adjustments (other than bug fixes)
- New Hercs, weapons, or missions

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
    <p><em>An early version of the HERCULAN Engine, displaying static content and a basic debugging interface that may eventually turn into a mission editor (no promises!)</em></p>
  </figure>
</div>

<div align="center">
  <figure>
    <img src="Images/Outlaw%20Cockpit.png" alt="Outlaw Cockpit" width="400">
    <p><em>An early version of the HERCULAN Engine, displaying the Outlaw cockpit with (currently non-functional) HUD</em></p>
  </figure>
</div>

## Project Structure

- **`Herculan/`** — Main project directory
  - **`HercWorksMDK.sln`** — Visual Studio solution for HercWorks CSharp (WinForms tool)
  - **`HerculanEngine.sln`** — Visual Studio solution for the HERCULAN Engine
  - **`src/`** — Source code for HercWorks CSharp and the HERCULAN Engine
  - **`docs/`** — Technical documentation on file formats, reverse-engineering findings, and architecture
- **`tools/`** — Utility scripts and plugins (e.g., Ghidra scripts for binary analysis)
- **`Images/`** — Screenshots of the HercWorks UI and engine

For details on specific reverse-engineering work and file format documentation, see the files in `Herculan/docs/`.

## AI Disclosure
Development of HercWorks CSharp has been largely powered by Claude (with prompting, testing, and feedback from a human). AI-generated documentation and code may contain mistakes. See the accompanying LICENSE file for copyright information.

## License

See the accompanying LICENSE file.