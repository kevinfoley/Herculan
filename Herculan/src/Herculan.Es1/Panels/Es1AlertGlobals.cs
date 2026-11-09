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
///
/// <para>The shell links its own copy of <c>alert.cpp</c> (docs/es1/shell/options.md#the-panel-base--alertpanel_ctorbase-0000222d),
/// the same statements on its own globals: its globals are another instance, with the shell's input
/// (<see cref="Input"/>), <see cref="PageOnScreen"/>, <see cref="CountsOpen"/> and <see cref="Capture"/> set for it.</para>
/// </summary>
public sealed class Es1AlertGlobals {
	/// <param name="display">The display the panels draw on, <c>000bdd28</c>.</param>
	/// <param name="mouse">The cockpit's mouse, whose active root a panel takes and whose cursor it changes.</param>
	/// <param name="input">The input build each pass of a panel's loop runs.</param>
	/// <param name="widgets">What <c>Widget_LoadStylesAndFonts</c> loaded; none draws no frame and no text.</param>
	/// <param name="content">The volumes the panels' own files are read from; none reads nothing.</param>
	public Es1AlertGlobals(Es1VideoMemory display, Es1CockpitMouse mouse, Es1InputBuild input, Es1WidgetResources? widgets,
			GameContent? content)
		: this(display, mouse, new Es1SimAlertInput(input), widgets, content) {
	}

	/// <param name="input">The binary's input and options.</param>
	/// <inheritdoc cref="Es1AlertGlobals(Es1VideoMemory, Es1CockpitMouse, Es1InputBuild, Es1WidgetResources, GameContent)"/>
	public Es1AlertGlobals(Es1VideoMemory display, Es1CockpitMouse mouse, Es1AlertInput input, Es1WidgetResources? widgets,
			GameContent? content) {
		Display = display;
		Mouse = mouse;
		Input = input;
		Widgets = widgets ?? new Es1WidgetResources();
		Content = content;
		PageOnScreen = () => display.PairVisible;
	}

	public Es1VideoMemory Display { get; }

	public Es1CockpitMouse Mouse { get; }

	public Es1AlertInput Input { get; }

	/// <summary>
	/// The page <c>AlertPanel_Enter</c> makes the display's source and draw page, the one on the screen: the simulator's
	/// <c>+0x88</c> (<see cref="Es1VideoMemory.PairVisible"/>), the shell's <c>+0x84</c> after its case's swap.
	/// </summary>
	public Func<int> PageOnScreen { get; init; }

	/// <summary>Whether <c>AlertPanel_Enter</c> and <c>AlertPanel_Leave</c> count <see cref="OpenCount"/>: the simulator's do, the shell's keep no count.</summary>
	public bool CountsOpen { get; init; } = true;

	/// <summary>
	/// The case of <c>AlertPanel_HandleEvent</c>'s sixth code, <c>0x42e</c> [Ctrl+C], which the shell's table
	/// <c>0007d9f0</c> holds and the simulator's five do not; none for no such code.
	/// </summary>
	public Action? Capture { get; init; }

	public Es1WidgetResources Widgets { get; }

	public GameContent? Content { get; }

	/// <summary>
	/// The simulator's sound file, which a panel's buttons click through (<c>Widget_ClickSound</c>) and whose sound manager
	/// the preferences panel tests; none for the shell's copy, which clicks through <see cref="ClickSound"/>, and for a
	/// test, which then counts the manager as present.
	/// </summary>
	public Sound.Es1Sound? Sound { get; init; }

	/// <summary>
	/// The shell's click sound, slot <c>+0xc</c> of its buttons' mixin table <c>ES1GO 0007dac2</c>,
	/// <c>Widget_ClickSoundAlertCopy</c> (<c>00008954</c>); none for the simulator's, whose slot is
	/// <c>Widget_ClickSound</c> (<see cref="PlayClickSound"/>).
	/// </summary>
	public Action? ClickSound { get; init; }

	/// <summary>
	/// A button's mixin click sound: <see cref="ClickSound"/> when set, else the simulator's <c>Widget_ClickSound</c>
	/// (<c>000314a0</c>), <c>Sound_Play</c>(<see cref="Sound.Es1Sound.ClickRow"/>) on <see cref="Sound"/>.
	/// </summary>
	public void PlayClickSound() {
		if (ClickSound != null) {
			ClickSound();
		} else {
			Sound?.Play(Herculan.Es1.Sound.Es1Sound.ClickRow);
		}
	}

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

	private readonly List<Es1Rect> _openRects = [];

	/// <summary>
	/// <b>This engine's own:</b> the rects of the panels up, in the order they went up. <see cref="Es1AlertPanel.Enter"/>
	/// adds one and <see cref="Es1AlertPanel.Leave"/> takes it off, whether or not <see cref="CountsOpen"/> is set. The
	/// host reads them to draw the panels at the window's center while the 320 × 200 screen lies off it
	/// (<see cref="View.Es1Monitor.Present"/>).
	/// </summary>
	public IReadOnlyList<Es1Rect> OpenRects => _openRects;

	/// <summary>A panel's rect added to <see cref="OpenRects"/> as it goes up.</summary>
	internal void AddOpenRect(Es1Rect rect) => _openRects.Add(rect);

	/// <summary>The last of <see cref="OpenRects"/> equal to <paramref name="rect"/> taken off as its panel comes down.</summary>
	internal void RemoveOpenRect(Es1Rect rect) {
		int index = _openRects.LastIndexOf(rect);
		if (index >= 0) {
			_openRects.RemoveAt(index);
		}
	}

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
