using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using HercWorks.Core.Data.File.Cfg;

namespace Herculan.Engine.Shell;

/// <summary>
/// The preferences screen's thirteen clickable widgets: the eleven checkboxes box by box, top to bottom,
/// then the two buttons. Each checkbox's handler is named against it.
/// </summary>
public enum ShellPreferencesWidget {
	/// <summary><c>Music</c>, <c>PreferencesScreen_OnMusic</c> (<c>00436cc1</c>): option 0.</summary>
	Music = 0,

	/// <summary><c>Sound Effects</c>, <c>PreferencesScreen_OnSoundEffects</c> (<c>00436d22</c>): option 1.</summary>
	SoundEffects = 1,

	/// <summary><c>AutoRepair All Hercs</c>, <c>PreferencesScreen_OnAutoRepairAll</c> (<c>00436d83</c>): option 44 to 0.</summary>
	AutoRepairAll = 2,

	/// <summary><c>Manually Repair My Herc</c>, <c>PreferencesScreen_OnManualRepairMine</c> (<c>00436de4</c>): option 44 to 1.</summary>
	ManualRepairMine = 3,

	/// <summary><c>Manually Repair All Hercs</c>, <c>PreferencesScreen_OnManualRepairAll</c> (<c>00436e45</c>): option 44 to 2.</summary>
	ManualRepairAll = 4,

	/// <summary><c>AutoBuild Weapons</c>, <c>PreferencesScreen_OnAutoBuildWeapons</c> (<c>00436ea6</c>): option 45 to 0.</summary>
	AutoBuildWeapons = 5,

	/// <summary><c>Manually Build Weapons</c>, <c>PreferencesScreen_OnManualBuild</c> (<c>00437006</c>): option 45 to 1.</summary>
	ManualBuildWeapons = 6,

	/// <summary><c>High Res (640x480)</c>, <c>PreferencesScreen_OnHighRes</c> (<c>004370c8</c>): option 4 to 0.</summary>
	HighRes = 7,

	/// <summary><c>Low Res (320x240)</c>, <c>PreferencesScreen_OnLowRes</c> (<c>00437067</c>): option 4 to 1.</summary>
	LowRes = 8,

	/// <summary><c>Window</c>, <c>PreferencesScreen_OnWindow</c> (<c>00436f07</c>): option 6 to 0.</summary>
	Window = 9,

	/// <summary><c>Full Screen</c>, <c>PreferencesScreen_OnFullScreen</c> (<c>00436f78</c>): puts up the <c>Alert!</c> dialog.</summary>
	FullScreen = 10,

	/// <summary><c>Cancel</c>, <c>PreferencesScreen_OnCancel</c> (<c>00436b90</c>).</summary>
	Cancel = 11,

	/// <summary><c>Accept</c>, <c>PreferencesScreen_OnAccept</c> (<c>00436c51</c>).</summary>
	Accept = 12,

	/// <summary>The <c>Alert!</c> dialog's <c>ACCEPT</c>, <c>FullScreenAlert_OnAccept</c> (<c>00436fe8</c>).</summary>
	AlertAccept = 13,
}

/// <summary>
/// The screen the main menu's <c>PREFERENCES</c> opens — five boxes of checkboxes over six
/// <c>prefs.cfg</c> options, and <c>Cancel</c> and <c>Accept</c>. Built once at startup by
/// <c>PreferencesScreen_Build</c> (<c>00434f08</c>), put up by <c>PreferencesScreen_Enter</c>
/// (<c>004366b5</c>) and taken down by <c>PreferencesScreen_Hide</c> (<c>00436717</c>). See
/// docs/retail/shell/main-menu.md#the-preferences-screen.
///
/// <para>Every rect is a literal in the executable, kept parent-relative as the builder writes it: the
/// content panel in the canvas, the five boxes and the two buttons in the panel, and the rest in whichever
/// box holds them. The alert's panel is in a window the size of the display, so its rect is a canvas
/// rect.</para>
///
/// <para>HERCULAN treats the alert as modal, a divergence from retail.</para>
/// </summary>
public sealed class ShellPreferencesScreen {
	/// <summary>The bank the checkboxes draw: frame 0 ticked, frame 1 empty.</summary>
	public const string CheckBoxBank = "CHK_BOX";

	/// <summary>The content panel, in the canvas.</summary>
	public static readonly ShellRect PanelRect = new(0x72, 0x98, 0x21c, 0x183);

	/// <summary>The five boxes, in the content panel, in the order the builder constructs them.</summary>
	private enum Box {
		Audio,
		Repair,
		Weapons,
		Resolution,
		Display,
	}

	private static readonly ShellRect[] BoxRects = {
		new(0xc, 0x1a, 0xd0, 0x60),
		new(0xda, 0x1a, 0x19e, 0x76),
		new(0xda, 0x7e, 0x19e, 0xc4),
		new(0xc, 0x68, 0xd1, 0xae),
		new(0xc, 0xb6, 0xd1, 0xe4),
	};

	/// <summary>
	/// Each box's heading, in the box: its <c>estext.bin</c> entry, its rect and its alignment. Only
	/// <c>Audio/Speech Options:</c> is centred.
	/// </summary>
	private static readonly (int Text, ShellRect Rect, ShellTextAlign Align)[] Headings = {
		(0x106, new(0x15, 6, 0xad, 0x12), ShellTextAlign.Center),
		(0x107, new(0x36, 6, 0xa0, 0x12), ShellTextAlign.Left),
		(0x108, new(0x2c, 6, 0xaa, 0x12), ShellTextAlign.Left),
		(0x120, new(0x2c, 6, 0xaa, 0x12), ShellTextAlign.Left),
		(0x123, new(0x35, 6, 0xad, 0x12), ShellTextAlign.Left),
	};

	/// <summary>
	/// Each checkbox: the box that holds it, its rect there, and the label beside it, left-aligned in
	/// <c>0x27</c>. Indexed by <see cref="ShellPreferencesWidget"/>.
	/// </summary>
	private static readonly (Box Box, ShellRect Rect, int Label, ShellRect LabelRect)[] CheckBoxes = {
		(Box.Audio, new(0xaf, 0x1c, 0xc1, 0x2c), 0x116, new(5, 0x1c, 0xaa, 0x2a)),
		(Box.Audio, new(0xaf, 0x32, 0xc1, 0x42), 0x117, new(5, 0x33, 0xaa, 0x41)),
		(Box.Repair, new(0xaf, 0x1c, 0xc1, 0x2c), 0x113, new(5, 0x1c, 0xaa, 0x2a)),
		(Box.Repair, new(0xaf, 0x32, 0xc1, 0x42), 0x114, new(5, 0x33, 0xaa, 0x41)),
		(Box.Repair, new(0xaf, 0x48, 0xc1, 0x58), 0x115, new(5, 0x48, 0xaa, 0x56)),
		(Box.Weapons, new(0xaf, 0x1c, 0xc1, 0x2c), 0x111, new(5, 0x1c, 0xaa, 0x2a)),
		(Box.Weapons, new(0xaf, 0x32, 0xc1, 0x42), 0x112, new(5, 0x33, 0xaa, 0x41)),
		(Box.Resolution, new(0xaf, 0x1c, 0xc1, 0x2c), 0x121, new(5, 0x1c, 0xaa, 0x2a)),
		(Box.Resolution, new(0xaf, 0x32, 0xc1, 0x42), 0x122, new(5, 0x32, 0xaa, 0x3e)),
		(Box.Display, new(0x39, 0x1a, 0x4b, 0x2a), 0x124, new(10, 0x1c, 0x37, 0x2a)),
		(Box.Display, new(0xa9, 0x1a, 0xbb, 0x2a), 0x125, new(0x5a, 0x1c, 0xa5, 0x2a)),
	};

	/// <summary><c>Cancel</c> and <c>Accept</c>, in the content panel.</summary>
	private static readonly ShellRect CancelRect = new(0xda, 0xce, 0x139, 0xdd);
	private static readonly ShellRect AcceptRect = new(0x13e, 0xce, 0x19e, 0xdd);

	/// <summary>The <c>Alert!</c> panel, an <c>ESAlert</c>, in the canvas.</summary>
	public static readonly ShellRect AlertRect = new(0x68, 199, 0x226, 0x117);

	/// <summary>
	/// The alert's line, in the panel: <c>{5, 0x1c, W - 4, 0x2a}</c>, where <c>W</c> is the panel's own
	/// width, <c>+0x2d - +0x25</c>.
	/// </summary>
	private static readonly ShellRect AlertLineRect = new(5, 0x1c, AlertRect.X1 - AlertRect.X0 - 4, 0x2a);

	/// <summary>The alert's <c>ACCEPT</c>, in the panel.</summary>
	private static readonly ShellRect AlertAcceptRect = new(0xa8, 0x38, 0x107, 0x47);

	/// <summary>The <c>prefs.cfg</c> options the checkboxes show: the two sound bytes, then the four radio groups.</summary>
	private const int MusicOption = Prefs.MusicOption;
	private const int SoundsOption = Prefs.SoundsOption;
	private const int ResolutionOption = Prefs.VideoModeOption;
	private const int DisplayModeOption = 6;
	private const int RepairOption = 0x2c;
	private const int WeaponsBuildingOption = 0x2d;

	private readonly SimulatorPreferences _options;
	private readonly DynamixBitmap[]? _checkBox;
	private readonly Func<bool> _isFullScreen;
	private readonly Action _toggleFullScreen;

	/// <summary>Builds the screen over the shell's option array and the <see cref="CheckBoxBank"/> frames.</summary>
	/// <param name="isFullScreen"><c>Display_FullScreen</c> (<c>00481e68</c>), the shell's full-screen flag.</param>
	/// <param name="toggleFullScreen"><c>Display_ToggleFullScreen</c> (<c>00407085</c>), which takes the shell into full screen or out of it.</param>
	public ShellPreferencesScreen(SimulatorPreferences options, DynamixBitmap[]? checkBoxFrames,
			Func<bool> isFullScreen, Action toggleFullScreen) {
		_options = options;
		_checkBox = checkBoxFrames;
		_isFullScreen = isFullScreen;
		_toggleFullScreen = toggleFullScreen;
	}

	/// <summary>Whether the <c>Alert!</c> dialog is up — its window shown by <c>Full Screen</c>.</summary>
	public bool AlertOpen { get; private set; }

	/// <summary>
	/// Runs a widget's handler. Returns true for <c>Cancel</c> and <c>Accept</c>, whose handlers then take
	/// the screen down (<c>PreferencesScreen_Hide</c> (<c>00436717</c>)) and put the main menu up, which is the caller's. The fades go
	/// to <paramref name="sound"/>, and do nothing without one, as the original's do with no sound
	/// manager.
	/// </summary>
	public bool Click(ShellPreferencesWidget widget, ShellSound? sound) {
		switch (widget) {
			// PreferencesScreen_ToggleAudioOption (00436841)(0): MUSIC on then the fade in, or the fade out then MUSIC off.
			case ShellPreferencesWidget.Music when _options[MusicOption] == 0:
				_options.Toggle(MusicOption);
				sound?.FadeIn();
				break;
			case ShellPreferencesWidget.Music:
				sound?.FadeOut();
				_options.Toggle(MusicOption);
				break;

			// PreferencesScreen_ToggleAudioOption (00436841)(1).
			case ShellPreferencesWidget.SoundEffects:
				_options.Toggle(SoundsOption);
				break;

			// The group setters, PreferencesScreen_SetRepairMode (00436a9c), PreferencesScreen_SetWeaponsBuildMode (00436b50) and PreferencesScreen_SetGameResolution (00436abc), each through
			// ShellOptions_SetOption with apply. Each also stores the value in a word of its own
			// (00474cc4-00474cca) whose reader is not known, and which is not kept here.
			case ShellPreferencesWidget.AutoRepairAll or ShellPreferencesWidget.ManualRepairMine
					or ShellPreferencesWidget.ManualRepairAll:
				_options.Set(RepairOption, (byte)(widget - ShellPreferencesWidget.AutoRepairAll));
				break;
			case ShellPreferencesWidget.AutoBuildWeapons or ShellPreferencesWidget.ManualBuildWeapons:
				_options.Set(WeaponsBuildingOption, (byte)(widget - ShellPreferencesWidget.AutoBuildWeapons));
				break;
			case ShellPreferencesWidget.HighRes or ShellPreferencesWidget.LowRes:
				_options.Set(ResolutionOption, (byte)(widget - ShellPreferencesWidget.HighRes));
				break;

			// PreferencesScreen_OnWindow (00436f07): out of full screen if it is in it, then PreferencesScreen_SetDisplayMode(0) (00436b70).
			case ShellPreferencesWidget.Window:
				if (_isFullScreen()) {
					_toggleFullScreen();
				}

				_options.Set(DisplayModeOption, 0);
				break;

			// PreferencesScreen_OnFullScreen (00436f78): the alert's window, only while windowed.
			case ShellPreferencesWidget.FullScreen:
				if (!_isFullScreen()) {
					AlertOpen = true;
				}

				break;

			// FullScreenAlert_OnAccept (00436fe8): the window hidden, into full screen, then PreferencesScreen_SetDisplayMode(1) (00436b70).
			case ShellPreferencesWidget.AlertAccept:
				AlertOpen = false;
				_toggleFullScreen();
				_options.Set(DisplayModeOption, 1);
				break;

			// PreferencesScreen_OnCancel (00436b90): every option back to the shadow with no handler run, the window put where option
			// 6 says, and the fade MUSIC calls for — a fade out under MUSIC turned on for its length.
			case ShellPreferencesWidget.Cancel:
				_options.Revert(apply: false);
				if ((_options[DisplayModeOption] != 0) != _isFullScreen()) {
					_toggleFullScreen();
				}

				if (_options[MusicOption] == 0) {
					_options.Toggle(MusicOption);
					sound?.FadeOut();
					_options.Toggle(MusicOption);
				} else {
					sound?.FadeIn();
				}

				return true;

			// PreferencesScreen_OnAccept (00436c51): ShellOptions_Commit(0), then ShellOptions_SaveAll.
			case ShellPreferencesWidget.Accept:
				_options.Commit(apply: false);
				_options.Save(Enumerable.Range(0, Prefs.Length).ToArray());
				return true;
		}

		return false;
	}

	/// <summary>
	/// Whether a checkbox is ticked — its <c>+0x69</c>, which <c>PreferencesScreen_Enter</c> seeds from the
	/// options. <c>Music</c> and <c>Sound Effects</c> take their byte as it is (<c>PreferencesScreen_SyncSoundChecks</c>, <c>00436790</c>). A radio
	/// group's relight ticks the one whose value the option holds and clears the others
	/// (<c>PreferencesScreen_SyncRepairModeRadios</c> (<c>0043692e</c>), <c>PreferencesScreen_SyncBuildModeRadios</c> (<c>00436adc</c>), <c>PreferencesScreen_SyncResolutionRadios</c> (<c>00436a28</c>), <c>PreferencesScreen_SyncDisplayModeRadios</c> (<c>004367cd</c>)); a value no
	/// checkbox in the group names writes none, and on the first entry that leaves all of them at the
	/// constructor's 0.
	/// </summary>
	public bool IsChecked(ShellPreferencesWidget widget) => widget switch {
		ShellPreferencesWidget.Music => _options[MusicOption] != 0,
		ShellPreferencesWidget.SoundEffects => _options[SoundsOption] != 0,
		ShellPreferencesWidget.AutoRepairAll => _options[RepairOption] == 0,
		ShellPreferencesWidget.ManualRepairMine => _options[RepairOption] == 1,
		ShellPreferencesWidget.ManualRepairAll => _options[RepairOption] == 2,
		ShellPreferencesWidget.AutoBuildWeapons => _options[WeaponsBuildingOption] == 0,
		ShellPreferencesWidget.ManualBuildWeapons => _options[WeaponsBuildingOption] == 1,
		ShellPreferencesWidget.HighRes => _options[ResolutionOption] == 0,
		ShellPreferencesWidget.LowRes => _options[ResolutionOption] == 1,
		ShellPreferencesWidget.Window => _options[DisplayModeOption] == 0,
		ShellPreferencesWidget.FullScreen => _options[DisplayModeOption] == 1,
		_ => false,
	};

	/// <summary>Whether a widget is one of the eleven checkboxes rather than one of the two buttons.</summary>
	public static bool IsCheckBox(ShellPreferencesWidget widget) => widget < ShellPreferencesWidget.Cancel;

	/// <summary>One widget's rect, in the canvas.</summary>
	public static ShellRect WidgetRect(ShellPreferencesWidget widget) => widget switch {
		ShellPreferencesWidget.Cancel => Inside(PanelRect, CancelRect),
		ShellPreferencesWidget.Accept => Inside(PanelRect, AcceptRect),
		ShellPreferencesWidget.AlertAccept => Inside(AlertRect, AlertAcceptRect),
		_ => Inside(BoxRect(CheckBoxes[(int)widget].Box), CheckBoxes[(int)widget].Rect),
	};

	private static ShellRect BoxRect(Box box) => Inside(PanelRect, BoxRects[(int)box]);

	/// <summary>
	/// What the pointer hits: a checkbox or a button. None of them overlaps another, and the panel and the
	/// boxes have no handler, so a click anywhere else is swallowed. A checkbox's caption is an empty
	/// <c>Text</c> covering the whole of it, so the hit never changes within one. While the alert is up,
	/// only its <c>ACCEPT</c> answers.
	/// </summary>
	public ShellHit? HitAt(float canvasX, float canvasY) {
		if (AlertOpen) {
			var accept = WidgetRect(ShellPreferencesWidget.AlertAccept);
			return accept.Contains(canvasX, canvasY)
				? ShellHit.Button(new ShellWidget(ShellWidgetKind.PreferencesWidget, (int)ShellPreferencesWidget.AlertAccept),
					accept, canvasX, canvasY)
				: null;
		}

		for (var widget = ShellPreferencesWidget.Music; widget <= ShellPreferencesWidget.Accept; widget++) {
			var rect = WidgetRect(widget);
			if (!rect.Contains(canvasX, canvasY)) {
				continue;
			}

			var id = new ShellWidget(ShellWidgetKind.PreferencesWidget, (int)widget);
			return IsCheckBox(widget)
				? new ShellHit(id, ShellHandler.CheckBox)
				: ShellHit.Button(id, rect, canvasX, canvasY);
		}

		return null;
	}

	/// <summary>
	/// Draws the screen into <paramref name="surface"/>. The caller clears it first; the content panel's
	/// dithered body leaves every other pixel to the backdrop, as the main menu's does. <paramref name="lit"/> is
	/// the widget a press has lit.
	/// </summary>
	public void Paint(ShellSurface surface, ShellText? text, HudSpriteSheet? sprites, ShellWidget? lit) {
		var font = sprites?.Font(ShellArt.ScreenFont);

		ShellChrome.PaintTitledPanel(surface, PanelRect, PanelBorder, PanelFace, ShellChrome.InteriorColor, TitleHeight,
			headerChrome: true, TitlePlateFirst, TitlePlateLast, fill: false);
		ShellChrome.PaintText(surface, new ShellRect(PanelRect.X0, PanelRect.Y0, PanelRect.X1, PanelRect.Y0 + TitleHeight),
			font, text?.Text(TitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);

		foreach (var box in Enum.GetValues<Box>()) {
			var rect = BoxRect(box);
			var (heading, headingRect, align) = Headings[(int)box];
			ShellChrome.PaintFramedPanel(surface, rect, BoxBorder, BoxFace, fill: true);
			ShellChrome.PaintText(surface, Inside(rect, headingRect), font, text?.Text(heading), align,
				ShellChrome.FontInkColor);
		}

		for (var widget = ShellPreferencesWidget.Music; widget < ShellPreferencesWidget.Cancel; widget++) {
			var (box, _, label, labelRect) = CheckBoxes[(int)widget];
			ShellChrome.PaintText(surface, Inside(BoxRect(box), labelRect), font, text?.Text(label), ShellTextAlign.Left,
				LabelColor);
			PaintCheckBox(surface, widget);
		}

		PaintButton(surface, font, text, ShellPreferencesWidget.Cancel, CancelText, lit);
		PaintButton(surface, font, text, ShellPreferencesWidget.Accept, AcceptText, lit);

		if (AlertOpen) {
			PaintAlert(surface, font, text, lit);
		}
	}

	/// <summary>
	/// The <c>Alert!</c> dialog: an <c>ESAlert</c> with a filled body, its one line and its <c>ACCEPT</c>.
	/// </summary>
	private static void PaintAlert(ShellSurface surface, HudFont? font, ShellText? text, ShellWidget? lit) {
		ShellChrome.PaintTitledPanel(surface, AlertRect, PanelBorder, PanelFace, ShellChrome.InteriorColor,
			AlertTitleHeight, headerChrome: true, AlertPlateFirst, AlertPlateLast, fill: true);
		ShellChrome.PaintText(surface, new ShellRect(AlertRect.X0, AlertRect.Y0, AlertRect.X1, AlertRect.Y0 + AlertTitleHeight),
			font, text?.Text(AlertTitleText), ShellTextAlign.Center, ShellChrome.FontInkColor);
		ShellChrome.PaintText(surface, Inside(AlertRect, AlertLineRect), font, text?.Text(AlertLineText),
			ShellTextAlign.Left, LabelColor);
		PaintButton(surface, font, text, ShellPreferencesWidget.AlertAccept, AlertAcceptText, lit);
	}

	/// <summary>
	/// A checkbox — <c>ESRadioButton_Paint</c> (<c>0040a26d</c>), the paint of the <c>ButtonIcon</c> subclass <c>ESRadioButton_Ctor</c> (<c>0040a100</c>)
	/// builds: the face its <c>+0x69</c> picks, blitted at the widget's corner and clipped to it. The
	/// builder hands the constructor <c>chk_box</c> frame 1 as the <c>+0x51</c> face and frame 0 as the
	/// <c>+0x55</c> one. The frames are 24 wide and the rect 19, and the five columns clipped off are
	/// index 0. The caption is the empty string at <c>00474cb1</c>, so the paint's text step draws nothing.
	/// </summary>
	private void PaintCheckBox(ShellSurface surface, ShellPreferencesWidget widget) {
		int frame = IsChecked(widget) ? CheckedFrame : UncheckedFrame;
		if (_checkBox is not { } frames || frame >= frames.Length) {
			return;
		}

		var rect = WidgetRect(widget);
		var clip = surface.PushClip(rect);
		surface.Blit(frames[frame], rect.X0, rect.Y0);
		surface.PopClip(clip);
	}

	/// <summary>A button, none of which is ever greyed.</summary>
	private static void PaintButton(ShellSurface surface, HudFont? font, ShellText? text, ShellPreferencesWidget widget,
			int captionText, ShellWidget? lit) =>
		ShellChrome.PaintButton(surface, WidgetRect(widget), ButtonBorder, font, text?.Text(captionText),
			ShellChrome.FontInkColor, pressed: lit == new ShellWidget(ShellWidgetKind.PreferencesWidget, (int)widget));

	private static ShellRect Inside(ShellRect parent, ShellRect child) =>
		new(parent.X0 + child.X0, parent.Y0 + child.Y0, parent.X0 + child.X1, parent.Y0 + child.Y1);

	/// <summary>The content panel: border from the constructor, header face, plate and dithered body from the builder.</summary>
	private const byte PanelBorder = 0x27;
	private const byte PanelFace = 0x25;
	private const int TitleHeight = 0x13;
	private const int TitlePlateFirst = 0x8c;
	private const int TitlePlateLast = 0x11b;

	/// <summary>The alert: the content panel's border and face, header 20 tall, its own plate.</summary>
	private const int AlertTitleHeight = 0x14;
	private const int AlertPlateFirst = 0xbe;
	private const int AlertPlateLast = 0xfa;

	/// <summary>The five boxes: <c>ESRegionFill_Ctor</c>'s border, and the <c>0x0f</c> checkerboard the builder writes over the class's.</summary>
	private const byte BoxBorder = 0x15;
	private const byte BoxFace = 0x0f;

	private const byte LabelColor = 0x27;
	private const byte ButtonBorder = 0x22;

	/// <summary>The <see cref="CheckBoxBank"/> frame for each face.</summary>
	private const int CheckedFrame = 0;
	private const int UncheckedFrame = 1;

	/// <summary><c>estext.bin</c> indices the screen prints beyond the tables above.</summary>
	private const int TitleText = 0x103;
	private const int CancelText = 0x10b;
	private const int AcceptText = 0x10c;
	private const int AlertTitleText = 0x126;
	private const int AlertLineText = 0x127;
	private const int AlertAcceptText = 0x10;
}
