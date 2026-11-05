using System.Runtime.CompilerServices;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Sim;

/// <summary>
/// <c>Sim_DeathFlash</c> (<c>ES1DBSIM 00058414</c>), which the tick runs for status 2
/// (docs/es1/simulation/main-loop.md#the-death-flash--sim_deathflash-00058414): the view shaken, the screen flashed
/// through <c>dpl\death1</c> and <c>dpl\death2</c> and faded to black on the clock <c>000981c4</c> with frames drawn and
/// no tick, the pages blacked out, <c>dpl\world0</c> installed and the status-2 panel run. Every frame it draws is the
/// frame loop's (<see cref="IEs1Frame"/>), and each pass of its two fades ends with the next host frame, as a pass of a
/// panel's loop does, so the PC timer runs on between passes (plan decision 11).
/// </summary>
internal static class Es1DeathFlash {
	/// <summary>How long the shake lasts, in clock counts, half a second.</summary>
	public const uint ShakeLength = 0x1e;

	/// <summary>The band <c>CockpitView_SetShakeBand</c> is given, and the spread of the shake's steps.</summary>
	public const int ShakeBand = 10, ShakeSpread = 10;

	/// <summary>Each fade's length, in clock counts, two seconds.</summary>
	public const int FadeLength = 0x78;

	/// <summary>The counts left at which the first fade stops.</summary>
	public const uint FirstFadeEnd = 0x14;

	/// <summary>The colors <c>Palette_InstallRange</c> installs from entry 0 of <c>death1</c> and <c>world0</c>.</summary>
	public const int InstalledColors = 0xff;

	/// <summary>The row of <c>1explo</c>, the sound the shake starts with.</summary>
	private const int DeathSound = 0x26;

	/// <summary>
	/// <c>Sim_DeathFlash</c> on <paramref name="simulation"/>, in its order:
	/// <list type="number">
	/// <item>the three palettes loaded (<c>Palette_LoadDpl</c>, <c>00063584</c>);</item>
	/// <item>the digital sounds stopped (<see cref="Sound.Es1Sound.StopDigitalSounds"/>);</item>
	/// <item>the shake's end at the clock + <see cref="ShakeLength"/>, <c>CockpitView_SetShakeBand</c>(<see cref="ShakeBand"/>)
	/// (<see cref="Es1ViewManager.SetShakeBand"/>) and <see cref="DeathSound"/>;</item>
	/// <item><c>death1</c>'s first <see cref="InstalledColors"/> colors installed from entry 0 and a fade to <c>death2</c>
	/// over <see cref="FadeLength"/> (<see cref="Es1PaletteFade"/>), each pass while the fade answers more than
	/// <see cref="FirstFadeEnd"/>: <c>Sim_EndFrame</c>, while the clock is below the shake's end
	/// <c>CockpitView_StepShake</c>(<c>Math_RandomBelow</c>(<c>000b5c48</c>, <see cref="ShakeSpread"/>)), and
	/// <c>Sim_RenderFrame</c>;</item>
	/// <item>a fade from <c>death2</c> to black over <see cref="FadeLength"/>, each pass while it answers non-zero:
	/// <c>Sim_EndFrame</c> and <c>Sim_RenderFrame</c>;</item>
	/// <item>the display's <c>+0x88</c> and <c>+0x8c</c> pages cleared to color 0 (<see cref="Es1VideoMemory.ClearPage"/>),
	/// <c>world0</c>'s first <see cref="InstalledColors"/> colors installed and <c>Sim_EndFrame</c>;</item>
	/// <item>the status panel (0, 0, 0, 2), which keeps no copy of the screen, run with its answer unread and destroyed
	/// (<c>GeneralAlert_Dtor</c>(panel, 3)).</item>
	/// </list>
	/// With no frame loop, view manager or panels, which retail always has, nothing is drawn or raised, an engine guard;
	/// the tick still ends the mission. A palette whose file is missing, which retail's load asserts on, is none here: it
	/// is not installed, and a fade from or to it fades from or to black.
	/// <para>The cockpit and the 3D pass's faces and ground flash and fade with the live palette
	/// (<see cref="Render.Es1WorldPalette"/>). PLACEHOLDER: the effects' billboards and the sky's bands, skyline and
	/// clouds keep the colors they were built in, where retail's whole screen flashes; the plan's hit flash covers them
	/// (docs/herculan/plan-es1-first-mission.md#what-the-slice-leaves-out).</para>
	/// </summary>
	public static IEnumerable<Es1Wait> Run(Es1Simulation simulation) {
		if (simulation.Frame is not { } frame || simulation.ViewChain.Manager is not { } views || simulation.Panels is not { } panels) {
			yield break;
		}

		var globals = simulation.Globals;
		var clock = globals.Clock;
		var fade = globals.PaletteFade;
		var death1 = LoadDpl(panels.Content, "DEATH1");
		var death2 = LoadDpl(panels.Content, "DEATH2");
		var world0 = LoadDpl(panels.Content, "WORLD0");

		// 2.
		globals.Sound.StopDigitalSounds();

		// 3.
		uint shakeEnd = clock.Clock + ShakeLength;
		views.SetShakeBand(ShakeBand);
		globals.Sound.Play(DeathSound);

		// 4.
		Install(frame, death1);
		fade.Begin(FadeLength, death2, death1);
		while (fade.Step(clock.Clock, frame.InstallPalette) > FirstFadeEnd) {
			foreach (var wait in EndFrame(simulation, frame)) {
				yield return wait;
			}

			if (clock.Clock < shakeEnd) {
				views.StepShake(globals.PresentationRandom.NextBelow(ShakeSpread));
			}

			frame.RenderFrame();
			yield return Es1Wait.NextHostFrame;
		}

		// 5.
		fade.Begin(FadeLength, null, death2);
		while (fade.Step(clock.Clock, frame.InstallPalette) != 0) {
			foreach (var wait in EndFrame(simulation, frame)) {
				yield return wait;
			}

			frame.RenderFrame();
			yield return Es1Wait.NextHostFrame;
		}

		// 6.
		var display = views.Display;
		display.ClearPage(display.PairVisible, 0);
		display.ClearPage(display.PairDrawn, 0);
		Install(frame, world0);
		foreach (var wait in EndFrame(simulation, frame)) {
			yield return wait;
		}

		// 7.
		var panel = new Es1StatusAlertPanel(panels, Es1MissionStatus.Destroyed, keepsScreen: false);
		foreach (var wait in panel.Run(new StrongBox<int>())) {
			yield return wait;
		}

		panel.Destroy();
	}

	/// <summary>
	/// <c>Palette_LoadDpl</c> of <c>dpl\</c><paramref name="name"/>, through <c>ResourcePath_BuildFolderName</c> with the
	/// folder name <c>000980d0</c> points at, as its entries (<see cref="Es1PaletteFade.Entries"/>); none when the file
	/// is missing or does not read.
	/// </summary>
	private static uint[]? LoadDpl(GameContent? content, string name) =>
		content?.Read("dpl", name + ".DPL") is { } bytes && new DynamixPaletteTransformer().Parse(bytes) is { } palette
			? Es1PaletteFade.Entries(palette)
			: null;

	/// <summary><c>Palette_InstallRange</c>(the palette's colors, <see cref="InstalledColors"/>, 0); nothing for none.</summary>
	private static void Install(IEs1Frame frame, uint[]? palette) {
		if (palette != null) {
			frame.InstallPalette(0, palette.Take(InstalledColors).ToArray());
		}
	}

	/// <summary><c>Sim_EndFrame</c> under the simulation's <c>Sim_Frozen</c>, each of its waits for the monitor yielded.</summary>
	private static IEnumerable<Es1Wait> EndFrame(Es1Simulation simulation, IEs1Frame frame) {
		foreach (var retrace in frame.EndFrame(simulation.Frozen)) {
			yield return Es1Wait.For(retrace);
		}
	}
}
