using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// What every in-mission panel shares (docs/es1/simulation/alert-panels.md): <c>alert.cpp</c>'s globals,
/// <c>AlertPanel_OpenCount</c> (<c>ES1DBSIM 00094d78</c>), <c>AlertPanel_CursorImage</c> (<c>00094d7c</c>) and the two
/// hooks <c>AlertPanel_EnterHook</c> and <c>AlertPanel_LeaveHook</c> (<c>00094d80</c>, <c>00094d84</c>); the resources
/// <c>Widget_LoadStylesAndFonts</c> loaded; and the simulator's state a panel takes and puts back as it goes up and
/// down: the display and its pages, the cockpit's mouse with its root and <c>Cursor_FollowsMouse</c>, the input build
/// with <c>PlayerInputBlock</c> and the button latches, and the mouse driver.
///
/// <para>In this engine the files a panel loads as it is built come through <see cref="Content"/>; none, as in a test,
/// loads nothing, and what would draw with them draws nothing.</para>
/// </summary>
public sealed class Es1AlertGlobals {
	/// <param name="display">The display the panels draw on, <c>000bdd28</c>.</param>
	/// <param name="mouse">The cockpit's mouse, whose active root a panel takes and whose cursor it changes.</param>
	/// <param name="input">The input build each pass of a panel's loop runs.</param>
	/// <param name="widgets">What <c>Widget_LoadStylesAndFonts</c> loaded; none draws no frame and no text.</param>
	/// <param name="content">The volumes the panels' own files are read from; none reads nothing.</param>
	public Es1AlertGlobals(Es1VideoMemory display, Es1CockpitMouse mouse, Es1InputBuild input, Es1WidgetResources? widgets,
			GameContent? content) {
		Display = display;
		Mouse = mouse;
		Input = input;
		Widgets = widgets ?? new Es1WidgetResources();
		Content = content;
	}

	public Es1VideoMemory Display { get; }

	public Es1CockpitMouse Mouse { get; }

	public Es1InputBuild Input { get; }

	public Es1WidgetResources Widgets { get; }

	public GameContent? Content { get; }

	/// <summary>
	/// The install the simulator runs in, whose <c>data\</c> a panel reads the shell's files from, as
	/// <c>Path_ApplyAltDrive</c> (<c>000592a4</c>) names them with the empty drive prefix the simulator runs with when
	/// <c>PREFER_ALT</c> is not set (docs/es1/simulation/main-loop.md#starting--sim_main-00059350, step 3); none reads
	/// nothing.
	/// </summary>
	public string? InstallRoot { get; init; }

	/// <summary>
	/// <c>Input_GetMouseDevice</c>'s answer: the mouse driver, or none. The host's mouse is the driver the cockpit's mouse
	/// registered with (<see cref="Es1CockpitMouse.Driver"/>).
	/// </summary>
	public Es1HostMouse? MouseDevice => Mouse.Driver;

	/// <summary>
	/// <c>AlertPanel_EnterHook</c> (<c>00094d80</c>), which <c>Sim_StartMission</c> sets to <c>SimInput_PushDisable</c>
	/// (docs/es1/simulation/main-loop.md#small-functions); none does nothing.
	/// </summary>
	public Action? EnterHook { get; set; }

	/// <summary><c>AlertPanel_LeaveHook</c> (<c>00094d84</c>), which <c>Sim_StartMission</c> sets to <c>SimInput_PopEnable</c>.</summary>
	public Action? LeaveHook { get; set; }

	/// <summary><c>AlertPanel_OpenCount</c> (<c>00094d78</c>, <c>int16</c>): the panels up.</summary>
	public short OpenCount { get; set; }

	/// <summary><c>AlertPanel_CursorImage</c> (<c>00094d7c</c>), <c>dci\alert</c>, once a panel has loaded it.</summary>
	public Es1CursorImage? CursorImage { get; private set; }

	/// <summary>
	/// <c>AlertPanel_Enter</c>'s step 7: while <see cref="CursorImage"/> is none, <c>dci\alert</c> loaded into it with
	/// <c>Cursor_LoadWrapper</c>.
	/// </summary>
	public Es1CursorImage? LoadCursorImage() {
		if (CursorImage == null && Content != null) {
			CursorImage = Es1CursorImage.Load(Content, "ALERT");
		}

		return CursorImage;
	}
}
