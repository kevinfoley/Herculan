using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Mfd;

/// <summary>
/// The MFD, the cockpit's instrument <c>+0x1e9</c> (docs/es1/simulation/mfd-display.md): a screen of six pictures
/// switched by six F-key buttons, a title that names the one up and steps to the next when clicked, the four captioned
/// buttons and the two row arrows the pictures use, the click surface over the screen, and the switch to the missile
/// camera while a launcher of electro-optical missiles is armed. Its slots are <c>MfdDisplayVtable</c>
/// (<c>00093fb6</c>)'s: the child click <see cref="OnChildClick"/>, the paint <see cref="Repaint"/>, the per-frame
/// <see cref="Update"/> and the key slot <see cref="KeyDispatch"/>.
/// </summary>
public sealed class Es1MfdDisplay {
	/// <summary>The modes, a screen each, in the order of the F-key buttons.</summary>
	public const int Status = 0, FlashComm = 1, NavMap = 2, Scanner = 3, TargetStatus = 4, MissileCam = 5, ModeCount = 6;

	/// <summary>The buttons past the F-key ones and the title.</summary>
	public const int ModeButton = 7, RangeButton = 8, TargetButton = 9, XmitButton = 10, PreviousRowButton = 11,
		NextRowButton = 12;

	/// <summary><c>STRINGS0.STR</c>'s group of the titles (entries 0 to 5) and the captions (7 to 10), <c>000bd2e0</c>, and of <c>OFFLINE</c>, <c>000bd4ac</c>.</summary>
	public const int CaptionGroup = 5, OfflineGroup = 30;

	/// <summary>The fill of the title and of the <c>OFFLINE</c> label.</summary>
	public const byte LabelFill = 0x3f;

	/// <summary>The clock counts between two refreshes of modes 0, 2 and 4: half a second.</summary>
	public const uint RefreshCounts = 30;

	/// <summary>The scanner range's index the constructor sets, the last of <see cref="ScannerRanges"/>.</summary>
	public const int FirstScannerRange = 2;

	/// <summary>
	/// The keys <see cref="KeyDispatch"/> takes for buttons 7 to 12, the table <c>00094054</c>: <c>D</c>, [Alt]+R, <c>T</c>,
	/// <c>X</c>, <c>,</c> and <c>.</c>.
	/// </summary>
	public static readonly ushort[] ButtonKeys = [0x20, 0x213, 0x14, 0x2d, 0x33, 0x34];

	/// <summary>The keys FLASH COMM takes for its rows, the table <c>00094084</c>: <c>A</c>, <c>G</c>, <c>H</c>, <c>O</c>, <c>C</c>, <c>E</c> and <c>F</c>.</summary>
	public static readonly ushort[] RowKeys = [0x1e, 0x22, 0x23, 0x18, 0x2e, 0x12, 0x21];

	/// <summary>The row each of <see cref="RowKeys"/> selects.</summary>
	public static readonly int[] RowKeyRows = [0, 1, 2, 3, 4, 4, 5];

	/// <summary>
	/// <c>MfdScannerRanges</c> (<c>000bd4f4</c>), in world units, which the constructor stores; the last is the radar
	/// range the target selection reads (<see cref="Es1TargetSelection.RadarRange"/>).
	/// </summary>
	public static readonly int[] ScannerRanges = [30000, 80000, 120000];

	/// <summary>
	/// <c>MfdButtonVisibility</c> (<c>00093f64</c>): per mode, the buttons it uses, a byte each; 0 to 6 in every mode,
	/// 9 in modes 0, 2, 4 and 5, 9 to 12 in mode 1 and 7 to 9 in mode 3.
	/// </summary>
	private static readonly bool[][] ButtonVisibility = [
		Uses(9), Uses(9, 10, 11, 12), Uses(9), Uses(7, 8, 9), Uses(9), Uses(9),
	];

	/// <summary>The F-key buttons' first picture in <c>dba\pbuttons</c>, two each: button <i>i</i>'s are 10 + 2<i>i</i> and 11 + 2<i>i</i>.</summary>
	private const int FirstModeFrame = 10;

	/// <summary>The captioned buttons' first picture, three in all, and the previous and next row arrows'.</summary>
	private const int CaptionedFrame = 3, PreviousRowFrame = 29, NextRowFrame = 32;

	private readonly Es1MfdButton[] _buttons = new Es1MfdButton[Es1GauMfdPanelBlock.ButtonCount];
	private readonly Es1MfdScreen[] _screens = new Es1MfdScreen[ModeCount];
	private readonly bool[] _screenFlags = new bool[ModeCount];
	private readonly StringFile? _strings;

	/// <summary>
	/// <c>MfdDisplay_Ctor</c> (<c>ES1DBSIM 00039c64</c>)(0, <paramref name="cockpit"/>, <paramref name="block"/>)
	/// (docs/es1/simulation/mfd-display.md#building-it): the gauge base's rect, the block's first 16 bytes; with
	/// (<i>x</i>, <i>y</i>) its first pair, the 13 buttons each on its rect moved by them, given its pictures and its
	/// caption label in <c>cpred</c> centered over the rect with no plate, and registered on the cockpit's root; the title,
	/// a label in <c>cpred</c> centered over button 6 on the fill <see cref="LabelFill"/>; the screen's rect, the block's
	/// at 952 moved by (<i>x</i>, <i>y</i>), and the click surface over it, registered; the mode −1, the range's index
	/// <see cref="FirstScannerRange"/> and the shared block cleared but for its range; the six screens in mode order; the
	/// refresh deadline the clock + 30; and the <c>OFFLINE</c> label, centered over the screen's rect on
	/// <see cref="LabelFill"/>.
	/// </summary>
	/// <param name="fonts"><c>ColorSchemePanels</c>.</param>
	/// <param name="pushButtons"><c>CockpitPButtonsBank</c>, the frames of <c>dba\pbuttons</c>; no picture is blitted when it or a frame is missing.</param>
	/// <param name="strings"><c>STRINGS0.STR</c>; nothing is written when null.</param>
	/// <param name="flyers">The frames of <c>dba\flyers</c>, the status screens' flyer silhouette.</param>
	internal Es1MfdDisplay(Es1Cockpit cockpit, Es1GauMfdPanelBlock block, IReadOnlyList<HudFont?> fonts,
			IReadOnlyList<DynamixBitmap>? pushButtons, StringFile? strings, IReadOnlyList<DynamixBitmap>? flyers) {
		Cockpit = cockpit;
		_strings = strings;
		Rect = Es1Rect.From(block.Rect);
		int x = block.Rect.X0, y = block.Rect.Y0;
		var red = Es1CockpitViews.CpRed < fonts.Count ? fonts[Es1CockpitViews.CpRed] : null;
		for (int i = 0; i < Es1GauMfdPanelBlock.ButtonCount; i++) {
			var rect = Es1Rect.From(block.Buttons[i]).Offset(x, y);
			DynamixBitmap?[] pictures = i switch {
				< Es1MfdButton.TitleButton => [Frame(FirstModeFrame + 2 * i), Frame(FirstModeFrame + 2 * i + 1), null],
				Es1MfdButton.TitleButton => [null, null, null],
				<= Es1MfdButton.LastCaptioned => [Frame(CaptionedFrame), Frame(CaptionedFrame + 1), Frame(CaptionedFrame + 2)],
				_ => Frames(i == PreviousRowButton ? PreviousRowFrame : NextRowFrame),
			};
			Es1Label? caption = null;
			if (i is < Es1MfdButton.TitleButton or (>= Es1MfdButton.FirstCaptioned and <= Es1MfdButton.LastCaptioned)) {
				caption = new Es1Label(red);
				caption.SetRect(Es1Label.Centered, rect);
			} else if (i == Es1MfdButton.TitleButton) {
				// Retail builds the title after the loop and stores it as button 6's label; no later step tells the two apart.
				caption = new Es1Label(red) { FillColor = LabelFill };
				caption.SetRect(Es1Label.Centered, rect);
			}

			_buttons[i] = new Es1MfdButton(i, this, rect, pictures, caption, fonts);
			cockpit.Root.RegisterClickable(_buttons[i]);
		}

		ScreenRect = Es1Rect.From(block.Screen).Offset(x, y);
		ClickSurface = new Es1MfdClickSurface(this, ScreenRect);
		cockpit.Root.RegisterClickable(ClickSurface);
		RangeIndex = FirstScannerRange;
		Shared.ScannerRange = ScannerRanges[RangeIndex];

		_screens[Status] = new Es1MfdStatusScreen(cockpit, ScreenRect, Shared, fonts, strings, flyers);
		_screens[FlashComm] = new Es1MfdPlaceholderScreen(cockpit, ScreenRect, Shared);
		_screens[NavMap] = new Es1MfdPlaceholderScreen(cockpit, ScreenRect, Shared);
		_screens[Scanner] = new Es1MfdPlaceholderScreen(cockpit, ScreenRect, Shared);
		_screens[TargetStatus] = new Es1MfdStatusScreen(cockpit, ScreenRect, Shared, fonts, strings, flyers);
		_screens[MissileCam] = new Es1MfdPlaceholderScreen(cockpit, ScreenRect, Shared);

		RefreshDeadline = cockpit.Globals.Clock.Clock + RefreshCounts;
		OfflineLabel = new Es1Label(red) { FillColor = LabelFill };
		OfflineLabel.SetRect(Es1Label.Centered, ScreenRect);

		DynamixBitmap? Frame(int index) => pushButtons != null && index < pushButtons.Count ? pushButtons[index] : null;

		DynamixBitmap?[] Frames(int first) => [Frame(first), Frame(first + 1), Frame(first + 2)];
	}

	/// <summary><c>+0x10</c>, the cockpit, <c>CockpitViewInstance</c>, whose machine and selection the display reads.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary><c>+0</c>–<c>+0xc</c>, the gauge base's rect, which nothing of the display reads after the constructor.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x14</c>–<c>+0x44</c>, the 13 buttons in index order; <c>+0x64</c>, their count.</summary>
	public IReadOnlyList<Es1MfdButton> Buttons => _buttons;

	/// <summary><c>+0x68</c>–<c>+0x75</c>, the sensor dropout and the repaint byte <c>+0x75</c>.</summary>
	public Es1GaugeDropout Dropout { get; } = new();

	/// <summary><c>+0x7a</c>–<c>+0x89</c>, the block the screens share.</summary>
	public Es1MfdSharedBlock Shared { get; } = new();

	/// <summary><c>+0x8a</c>, the mode, −1 before the first <see cref="SetMode"/>.</summary>
	public int Mode { get; private set; } = -1;

	/// <summary><c>+0x8e</c>, the scanner range's index into <see cref="ScannerRanges"/>.</summary>
	public int RangeIndex { get; private set; }

	/// <summary><c>+0x96</c>–<c>+0xaa</c>, the six screens by mode.</summary>
	public IReadOnlyList<Es1MfdScreen> Screens => _screens;

	/// <summary><c>+0xae</c>–<c>+0xb3</c>, a byte a mode: while the current mode's is set, <see cref="Update"/> runs its screen's update.</summary>
	public IReadOnlyList<bool> ScreenFlags => _screenFlags;

	/// <summary><c>+0xb4</c>–<c>+0xc0</c>, the screen's rect.</summary>
	public Es1Rect ScreenRect { get; }

	/// <summary><c>+0xc4</c>, the click surface over the screen.</summary>
	public Es1MfdClickSurface ClickSurface { get; }

	/// <summary><c>+0xc9</c>, the clock count past which modes 0, 2 and 4 next refresh.</summary>
	public uint RefreshDeadline { get; private set; }

	/// <summary>
	/// <c>MfdDisplay_OfflineLabel</c> (<c>00093f60</c>), a global retail builds for the first display constructed; kept
	/// with the display here, of which the simulator builds one.
	/// </summary>
	public Es1Label OfflineLabel { get; }

	/// <summary>
	/// <c>g_MfdMissileCamMode</c> (<c>00093f5c</c>), a global <c>int16</c>, −1 in the image: 5 while the missile camera
	/// switch holds. Kept with the display here, as <see cref="OfflineLabel"/>.
	/// </summary>
	public short MissileCamMode { get; private set; } = -1;

	/// <summary><c>g_MfdMissileCamArmed</c> (<c>00093f5e</c>), a global byte, 1 in the image.</summary>
	public bool MissileCamArmed { get; private set; } = true;

	/// <summary><c>g_MfdMissileCamSavedMode</c> (<c>00093f5a</c>), a global <c>int16</c>, 0 in the image: the mode the switch gives back.</summary>
	public short MissileCamSavedMode { get; private set; }

	/// <summary><c>STRINGS0.STR</c> group 5's entry <paramref name="entry"/>: a mode's title for 0 to 5, a button's caption for 7 to 10.</summary>
	public string? Caption(int entry) => _strings?.Text(CaptionGroup, entry);

	/// <summary>
	/// <c>MfdDisplay_SetMode</c> (<c>0003a9ec</c>)(display, <paramref name="mode"/>)
	/// (docs/es1/simulation/mfd-display.md#modes): nothing for the mode already set. Otherwise the mode stored and, while
	/// the missile camera switch holds, the switch cleared and its armed flag with it; each of buttons 6 to 12 the mode
	/// uses put from <see cref="Es1Widget.Hidden"/> to up and each it does not from up to hidden, either change
	/// invalidating it; the repaint byte and the title's paint counter set to 2; and the subject stored for modes 0 and 4.
	/// </summary>
	public void SetMode(int mode) {
		if (Mode == mode) {
			return;
		}

		Mode = mode;
		if (MissileCamMode != -1) {
			MissileCamArmed = false;
			MissileCamMode = -1;
		}

		var uses = ButtonVisibility[mode];
		for (int i = Es1MfdButton.TitleButton; i < Es1GauMfdPanelBlock.ButtonCount; i++) {
			var button = _buttons[i];
			if (uses[i] && button.State == Es1Widget.Hidden) {
				button.State = Es1Widget.Up;
				button.Invalidate();
			} else if (!uses[i] && button.State == Es1Widget.Up) {
				button.State = Es1Widget.Hidden;
				button.Invalidate();
			}
		}

		Dropout.Repaint = Es1GaugeDropout.RepaintPasses;
		_buttons[Es1MfdButton.TitleButton].PaintCounter = 2;
		StoreSubject();
	}

	/// <summary><c>MfdDisplay_CycleMode</c> (<c>0003a9c4</c>)(display): <see cref="SetMode"/> of the next mode, 5 wrapping to 0.</summary>
	public void CycleMode() => SetMode(Mode + 1 == ModeCount ? 0 : Mode + 1);

	/// <summary><c>MfdDisplay_CycleScannerRange</c> (<c>0003ab1c</c>)(display): <see cref="SetScannerRange"/> of the next index, 2 wrapping to 0.</summary>
	public void CycleScannerRange() => SetScannerRange(RangeIndex + 1 == ScannerRanges.Length ? 0 : RangeIndex + 1);

	/// <summary>
	/// <c>MfdDisplay_SetScannerRange</c> (<c>0003ab44</c>)(display, <paramref name="index"/>): for an index other than the
	/// one set, the index stored and its range put in the shared block.
	/// </summary>
	public void SetScannerRange(int index) {
		if (RangeIndex == index) {
			return;
		}

		RangeIndex = index;
		Shared.ScannerRange = ScannerRanges[index];
	}

	/// <summary>
	/// <c>MfdDisplay_InScannerRange</c> (<c>0003ae00</c>)(display, <paramref name="target"/>): whether the object's
	/// distance to the cockpit's machine (<c>Math_DistanceBetweenPoints</c>) is less than the shared block's range. With
	/// no machine, where retail reads through a null pointer, false, an engine guard.
	/// </summary>
	public bool InScannerRange(IEs1SimObject target) =>
		Cockpit.Machine is { } machine && target.Position.ApproxDistanceTo(machine.Position) < Shared.ScannerRange;

	/// <summary>
	/// <c>MfdDisplay_SyncMissileCamMode</c> (<c>0003ad08</c>)(display) (docs/es1/simulation/mfd-display.md#the-missile-camera-switch):
	/// the condition is that the armed mount's row gauge (<see cref="Es1Cockpit.FindArmedMountGauge"/>) has a projectile
	/// record of type 0 and subtype 3. With it and the switch clear: while the armed flag is clear, the switch set to 5 and
	/// the flag set; while it is set, the mode saved, mode 5 selected and the switch set to 5. Without it and with the
	/// switch set, the saved mode selected and the flag set.
	/// </summary>
	public void SyncMissileCamMode() {
		bool condition = Cockpit.FindArmedMountGauge() is { } gauge && gauge.MountProjectileType() == 0
			&& gauge.MountProjectileSubtype() == 3;
		if (MissileCamMode == -1 && condition) {
			if (!MissileCamArmed) {
				MissileCamMode = MissileCam;
				MissileCamArmed = true;
			} else {
				MissileCamSavedMode = (short)Mode;
				SetMode(MissileCam);
				MissileCamMode = MissileCam;
			}
		} else if (MissileCamMode != -1 && !condition) {
			SetMode(MissileCamSavedMode);
			MissileCamArmed = true;
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MfdDisplay_Repaint</c> (<c>0003a1f0</c>), which <c>CockpitView_InvalidateWidgets</c> runs: when
	/// <c>CockpitView_RectCornerVisible</c> passes the screen's rect, each button's paint, then <see cref="PaintScreen"/>.
	/// </summary>
	public void Repaint() {
		if (!Cockpit.RectCornerVisible(ScreenRect)) {
			return;
		}

		foreach (var button in _buttons) {
			button.Paint();
		}

		PaintScreen();
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>MfdDisplay_Update</c> (<c>0003a2a8</c>), which <c>Cockpit_DrawFrame</c> runs once a frame
	/// (docs/es1/simulation/mfd-display.md#drawing-it): nothing in view 4. The condition <c>Player_DependentCondition</c>(4)
	/// stored and, when it is not 0, the dropout's tick. Then, when <c>CockpitView_RectCornerVisible</c> passes the
	/// screen's rect: each button's flush and <see cref="SyncMissileCamMode"/>; at a repaint byte of 2
	/// <see cref="PaintScreen"/>, at 1 the screen's rect copied from the page on the screen to the page drawn and the
	/// mode's byte set, each lowering it by 1; and with the mode's byte set, by mode, the screen's update: for 0, 2 and 4
	/// once the clock is past the refresh deadline, which moves on 30 counts, the subject stored again for 0 and 4; for 3
	/// and 5 every frame; for 1 once, the byte cleared.
	/// </summary>
	public void Update() {
		if (Cockpit.CurrentView == Es1CockpitViews.ExternalView) {
			return;
		}

		Dropout.Condition = Cockpit.DependentCondition(4);
		if (Dropout.Condition != 0) {
			Dropout.Tick(Cockpit.Globals.Clock.Clock, Cockpit.Globals.PresentationRandom);
		}

		if (!Cockpit.RectCornerVisible(ScreenRect)) {
			return;
		}

		foreach (var button in _buttons) {
			button.Tick();
		}

		SyncMissileCamMode();
		if (Dropout.Repaint == 2) {
			PaintScreen();
			Dropout.Repaint--;
		} else if (Dropout.Repaint == 1) {
			Cockpit.DrawToCockpit(ScreenRect, 0);
			Dropout.Repaint--;
			_screenFlags[Mode] = true;
		}

		if (!_screenFlags[Mode]) {
			return;
		}

		switch (Mode) {
			case Status or NavMap or TargetStatus:
				uint now = Cockpit.Globals.Clock.Clock;
				if (RefreshDeadline >= now) {
					return;
				}

				RefreshDeadline = now + RefreshCounts;
				StoreSubject();
				break;
			case Scanner or MissileCam:
				break;
			default:
				_screenFlags[Mode] = false;
				break;
		}

		_screens[Mode].Update();
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>MfdButton_OnClick</c> (<c>0003a46c</c>)(display, flags, <paramref name="child"/>), a button's
	/// click (docs/es1/simulation/mfd-display.md#clicks-and-keys), by its index: 0 to 5 <see cref="SetMode"/>; 6
	/// <see cref="CycleMode"/>; 7 in mode 3 <c>Mech_ToggleRadarMode</c> on the cockpit's machine; 8
	/// <see cref="CycleScannerRange"/> and 9 <c>Cockpit_CycleTarget</c> in any mode; 10 in mode 1, unless the release was
	/// a double click, the order transmit; 11 and 12 in mode 1 the previous and the next available row. Each of 8 to 12
	/// that acts then sets the mode's byte. A child not among the buttons does nothing.
	/// </summary>
	public void OnChildClick(Es1Widget child) {
		int index = Array.IndexOf(_buttons, child as Es1MfdButton);
		switch (index) {
			case >= Status and < ModeCount:
				SetMode(index);
				return;
			case Es1MfdButton.TitleButton:
				CycleMode();
				return;
			case ModeButton:
				if (Mode == Scanner) {
					Cockpit.Machine?.ToggleRadarMode();
				}

				return;
			case RangeButton:
				CycleScannerRange();
				break;
			case TargetButton:
				CycleTarget();
				break;
			case XmitButton:
				if (Mode != FlashComm || Cockpit.MouseDoubleClick) {
					return;
				}

				Transmit();
				break;
			case PreviousRowButton or NextRowButton:
				if (Mode != FlashComm) {
					return;
				}

				if (index == PreviousRowButton) {
					PreviousAvailableRow();
				} else {
					NextAvailableRow();
				}

				break;
			default:
				return;
		}

		_screenFlags[Mode] = true;
	}

	/// <summary>
	/// <c>Cockpit_CycleTarget</c> (<c>0003ab08</c>)(display): <c>TargetSelect_Cycle</c> on the cockpit
	/// (<see cref="Es1TargetSelection.Cycle"/>).
	/// </summary>
	public void CycleTarget() => Cockpit.Targets.Cycle();

	/// <summary>
	/// Slot <c>+0x10</c>, <c>MfdDisplay_KeyDispatch</c> (<c>0003a5e8</c>)(display, <paramref name="code"/>), the end of the
	/// cockpit's key chain: 0 while <c>CockpitView_RectCornerVisible</c> refuses the screen's rect. One of
	/// <see cref="ButtonKeys"/> presses its button, 7 to 12, when the mode uses it (<see cref="PressButton"/>), [Alt]+R
	/// stepping the range itself when the mode does not use <c>RANGE</c>, and answers 1 either way. In mode 1 one of
	/// <see cref="RowKeys"/> selects its row and answers 1. Any other code answers 0.
	/// </summary>
	public short KeyDispatch(ushort code) {
		if (!Cockpit.RectCornerVisible(ScreenRect)) {
			return 0;
		}

		int key = Array.IndexOf(ButtonKeys, code);
		if (key >= 0) {
			int button = ModeButton + key;
			if (ButtonVisibility[Mode][button]) {
				PressButton(button);
			} else if (button == RangeButton) {
				CycleScannerRange();
			}

			return 1;
		}

		int rowKey = Array.IndexOf(RowKeys, code);
		if (Mode == FlashComm && rowKey >= 0) {
			SelectRow(RowKeyRows[rowKey], ClickSurface);
			return 1;
		}

		return 0;
	}

	/// <summary>
	/// A press of button <paramref name="index"/>, as <c>CockpitWidgets_HandleCommand</c>'s F-keys and
	/// <see cref="KeyDispatch"/> make it (docs/es1/simulation/cockpit-panel.md#keys-and-the-view-device): unless its state
	/// is <see cref="Es1Widget.Hidden"/> or 3, its click with the left button.
	/// </summary>
	public void PressButton(int index) {
		var button = _buttons[index];
		if (button.State is not (Es1Widget.Hidden or 3)) {
			button.OnClick(1);
		}
	}

	/// <summary>
	/// <c>MfdFlashComm_HandleAltKey</c> (<c>0003a890</c>)(display, <paramref name="code"/>), which the cockpit asks first
	/// with every code its own cases do not take: [Alt] with <c>A</c>, <c>G</c>, <c>H</c>, <c>O</c>, <c>C</c>, <c>E</c>
	/// or <c>F</c> picks and transmits a FLASH COMM order, answering 1. PLACEHOLDER: FLASH COMM is plan step 6c5 part C;
	/// until then it takes no code and answers 0.
	/// </summary>
	public short HandleAltKey(ushort code) {
		_ = code;
		return 0;
	}

	/// <summary>
	/// <c>MfdFlashComm_HandleListClick</c> (<c>0003ac04</c>)(display, <paramref name="point"/>, <paramref name="surface"/>),
	/// the click surface's left click. PLACEHOLDER: FLASH COMM's row pick, plan step 6c5 part C; until then it does nothing.
	/// </summary>
	public void HandleListClick((int X, int Y) point, Es1MfdClickSurface surface) {
		_ = point;
		_ = surface;
	}

	/// <summary>
	/// <c>MfdFlashComm_Transmit</c> (<c>0003adc8</c>)(display), the order transmit
	/// (docs/es1/simulation/mfd-flash-comm.md#the-order-helpers). PLACEHOLDER: plan step 6c5 part C; until then it does
	/// nothing.
	/// </summary>
	public void Transmit() {
	}

	/// <summary>
	/// <c>MfdFlashComm_PrevAvailableRow</c> (<c>0003abac</c>)(display), button 11's. PLACEHOLDER: plan step 6c5 part C;
	/// until then it does nothing.
	/// </summary>
	public void PreviousAvailableRow() {
	}

	/// <summary>
	/// <c>MfdFlashComm_NextAvailableRow</c> (<c>0003ab70</c>)(display), button 12's. PLACEHOLDER: plan step 6c5 part C;
	/// until then it does nothing.
	/// </summary>
	public void NextAvailableRow() {
	}

	/// <summary>
	/// <c>MfdFlashComm_SelectRow</c> (<c>0003acd4</c>)(display, <paramref name="row"/>, <paramref name="surface"/>), a row
	/// key's pick (docs/es1/simulation/mfd-flash-comm.md#picking-a-row). PLACEHOLDER: plan step 6c5 part C; until then it
	/// does nothing.
	/// </summary>
	public void SelectRow(int row, Es1MfdClickSurface surface) {
		_ = row;
		_ = surface;
	}

	/// <summary>
	/// What <see cref="Repaint"/> paints after the buttons and <see cref="Update"/> at a repaint byte of 2: unless the
	/// dropout holds the display dark, the current screen's paint; while it does, <c>OFFLINE</c> written on
	/// <see cref="OfflineLabel"/> for a condition of 2 or more and an empty string below, so the fill alone.
	/// </summary>
	private void PaintScreen() {
		if (!Dropout.Dark) {
			_screens[Mode].Paint();
			return;
		}

		string? text = Dropout.Condition >= 2 ? _strings?.Text(OfflineGroup, 0) : "";
		OfflineLabel.SetText(Cockpit.PushCanvasContext(), text);
	}

	/// <summary>The subject, <see cref="Es1MfdSharedBlock.Subject"/>: the cockpit's machine for mode 0 and its selection for mode 4; another mode leaves it.</summary>
	private void StoreSubject() {
		if (Mode == Status) {
			Shared.Subject = Cockpit.Machine;
		} else if (Mode == TargetStatus) {
			Shared.Subject = Cockpit.Selection;
		}
	}

	private static bool[] Uses(params int[] buttons) {
		var row = new bool[Es1GauMfdPanelBlock.ButtonCount];
		for (int i = 0; i <= Es1MfdButton.TitleButton; i++) {
			row[i] = true;
		}

		foreach (int button in buttons) {
			row[button] = true;
		}

		return row;
	}
}
