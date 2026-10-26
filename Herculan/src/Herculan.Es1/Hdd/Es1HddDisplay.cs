using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// The Heads-Down Display, the cockpit's instrument <c>+0x1f5</c> (docs/es1/simulation/heads-down-display.md): the
/// console below the dashboard, seen by looking down, with two pages, the command screen (<c>F7</c>) and the damage
/// screen (<c>F8</c>), fifteen buttons, a title with its indicator, and three squad comm boxes. Its slots are
/// <c>HDDGaugeVtable</c> (<c>0009451e</c>)'s: the child click <see cref="OnChildClick"/>, the paint
/// <see cref="Repaint"/>, the per-frame <see cref="Update"/> and the key slot <see cref="KeyDispatch"/>.
/// </summary>
public sealed class Es1HddDisplay {
	/// <summary>The pages, by <see cref="Page"/>.</summary>
	public const int CommandPage = 0, DamagePage = 1, PageCount = 2;

	/// <summary>The buttons by index (docs/es1/simulation/heads-down-display.md#the-buttons); 8 has no role.</summary>
	public const int CommandPageButton = 0, DamagePageButton = 1, UpButton = 2, DownButton = 3, LeftButton = 4,
		RightButton = 5, ZoomOutButton = 6, ZoomInButton = 7, TitleButton = 9, FirstCommBoxButton = 10, XmitButton = 13,
		CancelButton = 14;

	/// <summary>The subject slots: the player's machine, the three squadmates after it, and the target.</summary>
	public const int PlayerSubject = 0, TargetSubject = 4, SubjectCount = 5;

	/// <summary>
	/// <c>STRINGS0.STR</c>'s groups the display reads (docs/es1/formats/str-strings.md#the-groups): 8 <c>XMIT</c> and
	/// <c>CANCEL</c>, 10 page 0's title <c>MAP</c>, 11 page 1's titles, 14 <c>YOU</c>, 15 <c>TARGET</c>, 16
	/// <c>NO TARGET SELECTED</c> and <c>NO INFO AVAILABLE</c>.
	/// </summary>
	public const int CaptionGroup = 8, MapTitleGroup = 10, DamageTitleGroup = 11, YouGroup = 14, TargetGroup = 15,
		NoSubjectGroup = 16;

	/// <summary>The fill of the title and of <c>XMIT</c> and <c>CANCEL</c>, and of a comm box with no pilot.</summary>
	public const byte LabelFill = 0x3f;

	/// <summary>The title indicator's fill while its flag is set and while it is clear.</summary>
	public const byte IndicatorLit = 0x3b, IndicatorUnlit = 0x39;

	/// <summary>The clock counts between two refreshes of the damage page: half a second.</summary>
	public const uint RefreshCounts = 0x1e;

	/// <summary>The pilot channel's message id <see cref="SelectPilot"/> posts for the squadmate picked.</summary>
	public const byte PilotSelectedMessage = 0x22;

	/// <summary>The last message id the developer keys step <see cref="DebugMessageId"/> to.</summary>
	public const int LastDebugMessage = 0x29;

	/// <summary>The value of <see cref="PressedButton"/> with no press waiting.</summary>
	public const byte NoPress = 0xff;

	/// <summary>
	/// <c>HddDisplay_KeyCodes</c> (<c>00094574</c>), the 17 codes <see cref="KeyDispatch"/> takes: [Ctrl]+[Alt] with
	/// keypad 4, keypad 6 and <c>\</c>; keypad 8, 2, 4 and 6; <c>1</c> to <c>3</c>; <c>=</c>, keypad <c>+</c>,
	/// <c>-</c>, keypad <c>-</c>; <c>S</c>, <c>I</c> and <c>W</c>.
	/// </summary>
	public static readonly ushort[] KeyCodes =
		[0x64b, 0x64d, 0x62b, 0x48, 0x50, 0x4b, 0x4d, 0x02, 0x03, 0x04, 0x0d, 0x4e, 0x0c, 0x4a, 0x1f, 0x17, 0x11];

	/// <summary>
	/// <c>HddPageWidgetVisibility</c> (<c>000944dc</c>): per page, the buttons it shows, a byte each; page 0 0 to 7 and 10
	/// to 14, page 1 0 to 5 and 10 to 12.
	/// </summary>
	private static readonly bool[][] PageVisibility = [
		Shows(0, 1, 2, 3, 4, 5, 6, 7, 10, 11, 12, 13, 14), Shows(0, 1, 2, 3, 4, 5, 10, 11, 12),
	];

	/// <summary>The page buttons' first frame in <c>dba\pbuttons</c>, two each, and the magnifiers'.</summary>
	private const int PageButtonFrame = 22, ZoomButtonFrame = 51;

	/// <summary>The arrows' first frame for which the constructor gives them the diamond hit shape.</summary>
	private const int DiamondArrowFrame = 35;

	private readonly Es1HddButton[] _buttons = new Es1HddButton[Es1GauHddBlock.ButtonCount];
	private readonly Es1HddPage[] _pages = new Es1HddPage[PageCount];
	private readonly bool[] _fullRepaint = new bool[PageCount];
	private readonly bool[] _updateFlags = new bool[PageCount];
	private readonly Es1HddCommBox[] _commBoxes = new Es1HddCommBox[Es1GauHddBlock.CommBoxCount];
	private readonly IEs1SimObject?[] _subjects = new IEs1SimObject?[SubjectCount];
	private readonly string?[] _subjectNames = new string?[SubjectCount];
	private readonly StringFile? _strings;

	/// <summary>
	/// <c>HddDisplay_Ctor</c> (<c>ES1DBSIM 0003c258</c>)(0, <paramref name="cockpit"/>, <paramref name="block"/>)
	/// (docs/es1/simulation/heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258): the gauge base's rect, the
	/// block's first 16 bytes; the three comm boxes; with (<i>x</i>, <i>y</i>) its first pair, the screen rect, the damage
	/// column and the order column moved by it, and the page −1; the command screen and then the damage screen; the
	/// fifteen buttons, each on its rect moved by (<i>x</i>, <i>y</i>), given its frames, the arrows the diamond hit
	/// shape when their first frame is <see cref="DiamondArrowFrame"/>, and registered on the cockpit's root; the
	/// captions of <c>XMIT</c> and <c>CANCEL</c> and the title over button 9's rect, each in <c>cpred</c>, centered, on
	/// <see cref="LabelFill"/>; the title indicator and each box's marker rect moved by (<i>x</i>, <i>y</i>) and its name
	/// none; no pilot selected and page 0 set; the display stored as <see cref="Es1SimGlobals.HddDisplayInstance"/>, no
	/// press waiting, the highlight mode the block's, the dark byte kept and the damage page's refresh the clock + 30;
	/// and the five subjects, the player's machine named <c>YOU</c>, three empty and named with an empty string, and the
	/// target named <c>TARGET</c>, the selector 0 on the player, the indicator's flag set and the no-subject text
	/// <c>NO TARGET SELECTED</c>.
	/// </summary>
	/// <param name="fonts"><c>ColorSchemePanels</c>, whose <c>cpred</c> the labels take.</param>
	/// <param name="pushButtons"><c>CockpitPButtonsBank</c>, the frames of <c>dba\pbuttons</c>; no frame is blitted when it or a frame is missing.</param>
	/// <param name="strings"><c>STRINGS0.STR</c>; no text is written when null.</param>
	internal Es1HddDisplay(Es1Cockpit cockpit, Es1GauHddBlock block, IReadOnlyList<HudFont?> fonts,
			IReadOnlyList<DynamixBitmap>? pushButtons, StringFile? strings) {
		Cockpit = cockpit;
		_strings = strings;
		Rect = Es1Rect.From(block.Rect);
		int x = block.Rect.X0, y = block.Rect.Y0;
		for (int i = 0; i < _commBoxes.Length; i++) {
			_commBoxes[i] = new Es1HddCommBox();
		}

		// PLACEHOLDER: the comm boxes' clip context (GLViewport_Ctor(0) at CommBox_ClipContext, 000b15f8) and dba\static
		// into HddStaticBank (000944d0), which only the comm boxes' paints read: plan step 6c6's part C.
		ScreenRect = Es1Rect.From(block.Screen).Offset(x, y);
		DamageColumn = Es1Rect.From(block.DamageColumn).Offset(x, y);
		OrderColumn = Es1Rect.From(block.OrderColumn).Offset(x, y);
		CommandScreen = new Es1HddCommandScreen(this, OrderColumn, ScreenRect);
		DamageScreen = new Es1HddDamageScreen(this, DamageColumn, ScreenRect);
		_pages[CommandPage] = CommandScreen;
		_pages[DamagePage] = DamageScreen;

		int arrowFrame = block.ArrowFrame;
		for (int i = 0; i < Es1GauHddBlock.ButtonCount; i++) {
			var rect = Es1Rect.From(block.Buttons[i]).Offset(x, y);
			int first = i switch {
				<= DamagePageButton => PageButtonFrame + 2 * i,
				<= RightButton => arrowFrame + 2 * (i - UpButton),
				<= ZoomInButton => ZoomButtonFrame + 2 * (i - ZoomOutButton),
				_ => -1,
			};
			var button = new Es1HddButton(i, this, rect, Frame(first), Frame(first == -1 ? -1 : first + 1));
			if (i is >= UpButton and <= RightButton && arrowFrame == DiamondArrowFrame) {
				button.SetHitShape(1);
			}

			_buttons[i] = button;
			cockpit.Root.RegisterClickable(button);
		}

		var red = Es1CockpitViews.CpRed < fonts.Count ? fonts[Es1CockpitViews.CpRed] : null;
		foreach (int i in new[] { XmitButton, CancelButton }) {
			var caption = new Es1Label(red) { FillColor = LabelFill };
			caption.SetRect(Es1Label.Centered, _buttons[i].Rect);
			_buttons[i].Caption = caption;
		}

		TitleLabel = new Es1Label(red) { FillColor = LabelFill };
		TitleLabel.SetRect(Es1Label.Centered, _buttons[TitleButton].Rect);
		TitleIndicator = Es1Rect.From(block.TitleIndicator).Offset(x, y);
		for (int i = 0; i < _commBoxes.Length; i++) {
			_commBoxes[i].MarkerRect = Es1Rect.From(block.MarkerRects[i]).Offset(x, y);
			_commBoxes[i].Name = null;
		}

		PreviousSlot = -1;
		SelectedSlot = -1;
		SetPage(CommandPage);
		// PLACEHOLDER: CommBox_OnMessageBegin and CommBox_OnMessageEnd added to the pilot channel
		// (MessagePort_AddBeginCallback, MessagePort_AddEndCallback), plan step 6c6's part C.
		cockpit.Globals.HddDisplayInstance = this;
		PressedButton = NoPress;
		HighlightMode = (short)block.HighlightMode;
		LastDark = Dropout.Dark;
		RefreshDeadline = cockpit.Globals.Clock.Clock + RefreshCounts;
		// PLACEHOLDER: str\pilots into HddPilotNameTable (000944d4) the first time, which only HddGauge_LoadPilotFrames
		// reads: plan step 6c6's part C.
		_subjects[PlayerSubject] = cockpit.Machine;
		_subjectNames[PlayerSubject] = Text(YouGroup, 0);
		for (int i = PlayerSubject + 1; i < TargetSubject; i++) {
			_subjects[i] = null;
			_subjectNames[i] = string.Empty;
		}

		_subjectNames[TargetSubject] = Text(TargetGroup, 0);
		Selector = PlayerSubject;
		DamageSubject = _subjects[PlayerSubject];
		IndicatorFlag = true;
		NoSubjectText = Text(NoSubjectGroup, 0);

		DynamixBitmap? Frame(int index) =>
			pushButtons != null && index >= 0 && index < pushButtons.Count ? pushButtons[index] : null;
	}

	/// <summary><c>+0x10</c>, the cockpit, <c>CockpitViewInstance</c>, whose machine and selection the display reads.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary><c>+0</c>–<c>+0xc</c>, the gauge base's rect, which nothing of the display reads after the constructor.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x14</c>–<c>+0x50</c>, the fifteen buttons in index order; <c>+0x64</c>, their count.</summary>
	public IReadOnlyList<Es1HddButton> Buttons => _buttons;

	/// <summary><c>+0x68</c>–<c>+0x75</c>, the sensor dropout and the paint stage <c>+0x75</c>.</summary>
	public Es1GaugeDropout Dropout { get; } = new();

	/// <summary><c>+0x8a</c>, the screen rect, which the pages draw in and the visibility tests and copies take.</summary>
	public Es1Rect ScreenRect { get; }

	/// <summary><c>+0x9a</c>, the damage column, the damage screen's.</summary>
	public Es1Rect DamageColumn { get; }

	/// <summary><c>+0xaa</c>, the order column, the command screen's.</summary>
	public Es1Rect OrderColumn { get; }

	/// <summary><c>+0xba</c>, the title indicator's rect.</summary>
	public Es1Rect TitleIndicator { get; }

	/// <summary><c>+0xca</c>, page 0, the command screen.</summary>
	public Es1HddCommandScreen CommandScreen { get; }

	/// <summary><c>+0xce</c>, page 1, the damage screen.</summary>
	public Es1HddDamageScreen DamageScreen { get; }

	/// <summary><c>+0xd2</c>, the current page, −1 before the first <see cref="SetPage"/>.</summary>
	public int Page { get; private set; } = -1;

	/// <summary><c>+0xd6</c> + page, a byte a page: its full repaint waits.</summary>
	public IReadOnlyList<bool> FullRepaintFlags => _fullRepaint;

	/// <summary><c>+0xd8</c> + page, a byte a page: its update runs in the paint stages.</summary>
	public IReadOnlyList<bool> UpdateFlags => _updateFlags;

	/// <summary><c>+0xf6</c>, the three comm boxes' gauges by slot.</summary>
	public IReadOnlyList<Es1HddCommBox> CommBoxes => _commBoxes;

	/// <summary><c>+0x4d4</c>, the selected pilot's slot, −1 for none.</summary>
	public int SelectedSlot { get; private set; }

	/// <summary><c>+0x4e8</c>, the slot selected before it, −1 for none.</summary>
	public int PreviousSlot { get; private set; }

	/// <summary><c>+0x4d8</c>, the title label, over button 9's rect.</summary>
	public Es1Label TitleLabel { get; }

	/// <summary><c>+0x4dc</c>, the title indicator's flag, which the subject step sets from the subject's player byte.</summary>
	public bool IndicatorFlag { get; private set; }

	/// <summary><c>+0x4e1</c>, the damage page's subject machine.</summary>
	public IEs1SimObject? DamageSubject { get; private set; }

	/// <summary><c>+0x4e5</c>, the pressed button's index, <see cref="NoPress"/> for none.</summary>
	public byte PressedButton { get; private set; }

	/// <summary><c>+0x4e6</c>, the comm boxes' highlight mode, the block's <c>int32</c> <c>0x5e</c>.</summary>
	public short HighlightMode { get; }

	/// <summary><c>+0x4ec</c>, the dark byte <c>+0x74</c> as the last update saw it.</summary>
	public bool LastDark { get; private set; }

	/// <summary><c>+0x4ed</c>, the clock count after which the damage page next refreshes.</summary>
	public uint RefreshDeadline { get; private set; }

	/// <summary><c>+0x4f1</c>, the five subject machines: the player, three squadmates and the target.</summary>
	public IReadOnlyList<IEs1SimObject?> Subjects => _subjects;

	/// <summary><c>+0x505</c>, the five subjects' names.</summary>
	public IReadOnlyList<string?> SubjectNames => _subjectNames;

	/// <summary><c>+0x519</c>, the subject selector, an <c>int16</c> slot of <see cref="Subjects"/>.</summary>
	public short Selector { get; private set; }

	/// <summary><c>+0x51b</c>, the no-subject text: <c>NO TARGET SELECTED</c> or <c>NO INFO AVAILABLE</c>.</summary>
	public string? NoSubjectText { get; private set; }

	/// <summary>
	/// <c>HddDisplay_LastTarget</c> (<c>0009456c</c>), a global: the target slot as the last update stored it; kept
	/// with the display here, of which the simulator builds one.
	/// </summary>
	public IEs1SimObject? LastTarget { get; private set; }

	/// <summary><c>HddDebugMessageId</c> (<c>00094570</c>), a global, −1 in the image: the developer keys' message id; kept with the display, as <see cref="LastTarget"/>.</summary>
	public int DebugMessageId { get; private set; } = -1;

	/// <summary>The current page, or none before the first <see cref="SetPage"/>.</summary>
	private Es1HddPage? CurrentPage => Page is >= 0 and < PageCount ? _pages[Page] : null;

	/// <summary><c>STRINGS0.STR</c> group 8's entry <paramref name="entry"/>: <c>XMIT</c> for 0, <c>CANCEL</c> for 1.</summary>
	public string? ButtonCaption(int entry) => Text(CaptionGroup, entry);

	/// <summary>
	/// <c>HddDisplay_SetPage</c> (<c>0003d8b4</c>)(display, <paramref name="page"/>)
	/// (docs/es1/simulation/heads-down-display.md#pages-and-visibility): for a page other than the current one, the old
	/// page's hide unless there was none, the page stored, its show, and each button the page shows put from
	/// <see cref="Es1Widget.Hidden"/> to up and each it does not from up to hidden, either change invalidating it; then,
	/// either way, the page's full-repaint flag and the paint stage 2.
	/// </summary>
	public void SetPage(int page) {
		if (Page != page) {
			CurrentPage?.Hide();
			Page = page;
			_pages[page].Show();
			var shows = PageVisibility[page];
			for (int i = 0; i < _buttons.Length; i++) {
				var button = _buttons[i];
				if (shows[i] && button.State == Es1Widget.Hidden) {
					button.State = Es1Widget.Up;
					button.Invalidate();
				} else if (!shows[i] && button.State == Es1Widget.Up) {
					button.State = Es1Widget.Hidden;
					button.Invalidate();
				}
			}
		}

		_fullRepaint[Page] = true;
		Dropout.Repaint = Es1GaugeDropout.RepaintPasses;
	}

	/// <summary>
	/// <c>HddDisplay_SetTitle</c> (<c>0003d9b8</c>)(display): the title label written with <c>MAP</c> on page 0 and with
	/// the entry of group 11 the damage screen's <see cref="Es1HddDamageScreen.TitleIndex"/> picks on page 1; nothing on
	/// another page or for a missing entry.
	/// </summary>
	public void SetTitle() {
		string? text = Page switch {
			CommandPage => Text(MapTitleGroup, 0),
			DamagePage => Text(DamageTitleGroup, DamageScreen.TitleIndex),
			_ => null,
		};
		if (text != null) {
			TitleLabel.SetText(Cockpit.PushCanvasContext(), text);
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>HddDisplay_Repaint</c> (<c>0003cb90</c>), which <c>CockpitView_InvalidateWidgets</c> runs
	/// (docs/es1/simulation/heads-down-display.md#drawing-it): when <c>CockpitView_RectCornerVisible</c> passes the screen
	/// rect, each button's paint; the current page's show and repaint; the title indicator filled
	/// <see cref="IndicatorLit"/> or <see cref="IndicatorUnlit"/> by its flag; <see cref="SetTitle"/>; and each comm
	/// box's video rect set to its button's rect inset by a pixel on every side, then filled <see cref="LabelFill"/> while
	/// the box has no name.
	/// </summary>
	public void Repaint() {
		if (!Cockpit.RectCornerVisible(ScreenRect)) {
			return;
		}

		foreach (var button in _buttons) {
			button.Paint();
		}

		if (CurrentPage is { } page) {
			page.Show();
			page.Repaint();
		}

		var canvas = Cockpit.PushCanvasContext();
		FillIndicator(canvas);
		SetTitle();
		for (int i = 0; i < _commBoxes.Length; i++) {
			var box = _commBoxes[i];
			var rect = _buttons[FirstCommBoxButton + i].Rect;
			box.VideoRect = new Es1Rect(rect.X0 + 1, rect.Y0 + 1, rect.X1 - 1, rect.Y1 - 1);
			if (box.Name == null) {
				canvas.FillRect(box.VideoRect.X0, box.VideoRect.Y0, box.VideoRect.X1, box.VideoRect.Y1, LabelFill);
			}

			// PLACEHOLDER: a box with a name is painted by HddGauge_PaintIdle(display, i), plan step 6c6's part C.
		}
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>HddDisplay_Update</c> (<c>0003cd28</c>), which <c>Cockpit_DrawFrame</c> runs once a frame,
	/// with the widgets off as well (docs/es1/simulation/heads-down-display.md#drawing-it): the target step; the press
	/// waiting (<see cref="HandleWidgetPress"/>); the comms-out latches; the comm boxes' state machine; then outside view
	/// 4 the sensor dropout on <c>Player_DependentCondition</c>(4), the current page's full repaint when the dark byte
	/// changed, and, while <c>CockpitView_RectCornerVisible</c> passes the screen rect, each button's flush, the damage
	/// page's half-second refresh and the paint stages (<see cref="PaintStages"/>).
	/// </summary>
	public void Update() {
		var target = Cockpit.Selection;
		if (target == null) {
			NoSubjectText = Text(NoSubjectGroup, 0);
		} else if (target.TargetClass != 0) {
			NoSubjectText = Text(NoSubjectGroup, 1);
			target = null;
		}

		_subjects[TargetSubject] = target;
		if (!ReferenceEquals(target, LastTarget)) {
			if (Selector == TargetSubject) {
				DamageSubject = target;
			}

			LastTarget = target;
			_updateFlags[DamagePage] = true;
		}

		HandleWidgetPress();
		bool outsideStaged = Cockpit.ViewChangeStaged && Cockpit.CurrentView == Es1CockpitViews.ExternalView;
		for (int i = 0; i < _commBoxes.Length; i++) {
			if (Cockpit.SquadmateMachines[i] is not { } machine) {
				continue;
			}

			var box = _commBoxes[i];
			if (outsideStaged && machine.Destroyed) {
				box.CommsOutLatch = true;
			}

			if (machine.Destroyed && box.CommsOutLatch) {
				box.State = 1;
			}
		}

		// PLACEHOLDER: HddDisplay_ServiceCommBoxes (0003e574)(display), the comm boxes' state machine, plan step 6c6's
		// part C; until then every box has no name and the state machine would pass over each.
		if (Cockpit.CurrentView == Es1CockpitViews.ExternalView) {
			return;
		}

		Dropout.Condition = Cockpit.DependentCondition(4);
		if (Dropout.Condition != 0) {
			Dropout.Tick(Cockpit.Globals.Clock.Clock, Cockpit.Globals.PresentationRandom);
		}

		if (Dropout.Dark != LastDark) {
			_fullRepaint[Page] = true;
		}

		LastDark = Dropout.Dark;
		if (!Cockpit.RectCornerVisible(ScreenRect)) {
			return;
		}

		foreach (var button in _buttons) {
			button.Tick();
		}

		uint now = Cockpit.Globals.Clock.Clock;
		if (Page == DamagePage && RefreshDeadline < now) {
			Dropout.Repaint = Es1GaugeDropout.RepaintPasses;
			_updateFlags[Page] = true;
			RefreshDeadline = now + RefreshCounts;
		}

		PaintStages();
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>HddDisplay_OnChildClick</c> (<c>0003d430</c>)(display, value, <paramref name="child"/>): the
	/// child's index among the buttons stored as <see cref="PressedButton"/>, for <see cref="HandleWidgetPress"/> to act
	/// on; a child not among them leaves it.
	/// </summary>
	public void OnChildClick(Es1Widget child) {
		int index = Array.IndexOf(_buttons, child as Es1HddButton);
		if (index >= 0) {
			PressedButton = (byte)index;
		}
	}

	/// <summary>
	/// <c>HddDisplay_HandleWidgetPress</c> (<c>0003d48c</c>)(display), from the update
	/// (docs/es1/simulation/heads-down-display.md#presses): nothing with no press waiting, nor while the display is dark
	/// for a button above 1, whose press then waits. Otherwise, by the button: 0 and 1 <see cref="SetPage"/>; 2 to 7 on
	/// page 0 the command screen's pans and zooms, each setting page 0's update flag and the paint stage 2; 2, 3 and 9
	/// on page 1 the damage screen's view steps, each setting page 1's full-repaint flag and the stage 2; 4 and 5 on page
	/// 1 <see cref="PreviousSubject"/> and <see cref="NextSubject"/> into the damage page's subject, then
	/// <see cref="StepIndicator"/>; 10 to 12, page 1 first switching to 0, <see cref="SelectPilot"/> of the box's slot
	/// when it is not the selected one and its squadmate is set and not destroyed, then the map-pick cursor in the other
	/// views' slot while the command screen <see cref="Es1HddCommandScreen.WantsPick"/>; 13, <c>XMIT</c>, on page 0, and
	/// 14, <c>CANCEL</c>, as below. The press is then cleared.
	/// </summary>
	public void HandleWidgetPress() {
		if (PressedButton == NoPress || (Dropout.Dark && PressedButton > DamagePageButton)) {
			return;
		}

		switch (PressedButton) {
			case CommandPageButton or DamagePageButton:
				SetPage(PressedButton);
				break;
			case UpButton or DownButton:
				if (Page == CommandPage) {
					// PLACEHOLDER: 0003fd2c or 0003fd58 (command screen), ES2's HddMap_PanUp's and _PanDown's places;
					// the command screen is unread (Es1HddCommandScreen).
					ScheduleUpdate(CommandPage);
				} else if (Page == DamagePage) {
					// PLACEHOLDER: 0004246c or 00042494 (damage screen), ES2's HddDamageScreen_NextView's and _PrevView's
					// places; the damage screen is unread (Es1HddDamageScreen).
					ScheduleFullRepaint(DamagePage);
				}

				break;
			case LeftButton or RightButton:
				if (Page == CommandPage) {
					// PLACEHOLDER: 0003fcd4 or 0003fd00 (command screen), ES2's HddMap_PanLeft's and _PanRight's places.
					ScheduleUpdate(CommandPage);
				} else if (Page == DamagePage) {
					DamageSubject = PressedButton == LeftButton ? PreviousSubject() : NextSubject();
					StepIndicator();
				}

				break;
			case ZoomOutButton or ZoomInButton:
				if (Page == CommandPage) {
					// PLACEHOLDER: 0003fc70 or 0003fca4 (command screen), ES2's HddMap_ZoomOut's and _ZoomIn's places.
					ScheduleUpdate(CommandPage);
				}

				break;
			case TitleButton:
				if (Page == DamagePage) {
					// PLACEHOLDER: 0004246c (damage screen), as button 2.
					ScheduleFullRepaint(DamagePage);
				}

				break;
			case >= FirstCommBoxButton and < XmitButton:
				if (Page == DamagePage) {
					SetPage(CommandPage);
				} else if (Page != CommandPage) {
					break;
				}

				int slot = PressedButton - FirstCommBoxButton;
				if (slot != SelectedSlot && Cockpit.SquadmateMachines[slot] is { Destroyed: false }) {
					SelectPilot(slot);
					if (CommandScreen.WantsPick) {
						Cockpit.Cursors.SetOffForwardCursor(Es1CockpitCursors.MapPick);
					}
				}

				break;
			case XmitButton:
				if (Page == CommandPage) {
					Transmit();
				}

				break;
			case CancelButton:
				// PLACEHOLDER: 000407d4 (command screen), ES2's HddCommandScreen_CancelTransmission's place.
				Cockpit.Cursors.SetOffForwardCursor(Es1CockpitCursors.Arrow);
				SelectPilot(-1);
				break;
		}

		PressedButton = NoPress;
	}

	/// <summary>
	/// <c>HddDisplay_SelectPilot</c> (<c>0003da08</c>)(display, <paramref name="slot"/>): the selected box's lamp put out
	/// and the box invalidated when it was lit; the selection kept as <see cref="PreviousSlot"/> and
	/// <paramref name="slot"/> stored; for a slot other than −1, its box's lamp lit and the box invalidated when it was
	/// out, and message <see cref="PilotSelectedMessage"/> posted to the pilot channel about the slot's squadmate.
	/// </summary>
	public void SelectPilot(int slot) {
		if (SelectedSlot != -1) {
			var selected = _buttons[FirstCommBoxButton + SelectedSlot];
			if (selected.Lamp) {
				selected.Lamp = false;
				selected.Invalidate();
			}
		}

		PreviousSlot = SelectedSlot;
		SelectedSlot = slot;
		if (slot != -1) {
			var box = _buttons[FirstCommBoxButton + slot];
			if (!box.Lamp) {
				box.Lamp = true;
				box.Invalidate();
			}

			Cockpit.PilotChannel.Post(PilotSelectedMessage, Cockpit.SquadmateMachines[slot]);
		}

		// PLACEHOLDER: 00040664 (command screen, the selected slot), in the command screen's unread file.
	}

	/// <summary>
	/// <c>HddDisplay_NextSubject</c> (<c>0003e8d4</c>)(display) (docs/es1/simulation/heads-down-display.md#subject-stepping):
	/// the selector one on, 5 wrapping to 0, past each empty slot but the target's; answers the slot's machine.
	/// </summary>
	public IEs1SimObject? NextSubject() {
		Selector++;
		if (Selector == SubjectCount) {
			Selector = PlayerSubject;
		} else if (_subjects[Selector] == null && Selector != TargetSubject) {
			return NextSubject();
		}

		return _subjects[Selector];
	}

	/// <summary>
	/// <c>HddDisplay_PrevSubject</c> (<c>0003e950</c>)(display): the selector one back, −1 wrapping to the target's slot,
	/// past each empty slot; answers the slot's machine.
	/// </summary>
	public IEs1SimObject? PreviousSubject() {
		Selector--;
		if (Selector < 0) {
			Selector = TargetSubject;
		} else if (_subjects[Selector] == null) {
			return PreviousSubject();
		}

		return _subjects[Selector];
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>HddDisplay_KeyDispatch</c> (<c>0003d12c</c>)(display, <paramref name="code"/>)
	/// (docs/es1/simulation/heads-down-display.md#keys): one of <see cref="KeyCodes"/> acts and answers 1: the arrows
	/// press buttons 2 to 5, <c>1</c> to <c>3</c> buttons 10 to 12, <c>-</c> and keypad <c>-</c> button 6, <c>=</c> and
	/// keypad <c>+</c> button 7 (<see cref="PressButton"/>); <c>S</c>, <c>I</c> and <c>W</c> <see cref="SetPage"/> 1 and
	/// the damage screen's view; [Ctrl]+[Alt] with keypad 4, keypad 6 and <c>\</c> <see cref="PostDebugMessage"/>. Any
	/// other code goes to the current page's key slot, whose answer it returns.
	/// </summary>
	public short KeyDispatch(ushort code) {
		switch (code) {
			case 0x64b:
				PostDebugMessage(-1);
				break;
			case 0x64d:
				PostDebugMessage(1);
				break;
			case 0x62b:
				PostDebugMessage(0);
				break;
			case 0x48:
				PressButton(UpButton);
				break;
			case 0x50:
				PressButton(DownButton);
				break;
			case 0x4b:
				PressButton(LeftButton);
				break;
			case 0x4d:
				PressButton(RightButton);
				break;
			case >= 0x02 and <= 0x04:
				PressButton(code + 8);
				break;
			case 0x0d or 0x4e:
				PressButton(ZoomInButton);
				break;
			case 0x0c or 0x4a:
				PressButton(ZoomOutButton);
				break;
			case 0x1f or 0x17 or 0x11:
				SetPage(DamagePage);
				// PLACEHOLDER: 000423d8(damage screen, 0, 1 or 2 for S, I and W), ES2's HddDamageScreen_SetView's place;
				// the damage screen is unread (Es1HddDamageScreen).
				break;
			default:
				return CurrentPage?.KeyDispatch(code) ?? 0;
		}

		return 1;
	}

	/// <summary>
	/// A press of button <paramref name="index"/>, as <see cref="KeyDispatch"/> and the cockpit's <c>F7</c> and <c>F8</c>
	/// make it (docs/es1/simulation/cockpit-panel.md#keys-and-the-view-device): unless its state is
	/// <see cref="Es1Widget.Hidden"/> or 3, its click with the left button.
	/// </summary>
	public void PressButton(int index) {
		var button = _buttons[index];
		if (button.State is not (Es1Widget.Hidden or 3)) {
			button.OnClick(1);
		}
	}

	/// <summary>
	/// The developer keys' case: <see cref="DebugMessageId"/> made 1 when negative, then less 1 while above 0 for a
	/// <paramref name="step"/> of −1 or plus 1 while below <see cref="LastDebugMessage"/> for 1, and {that id, the first
	/// squadmate} posted to the pilot channel, the post reading the id's low byte.
	/// </summary>
	private void PostDebugMessage(int step) {
		if (DebugMessageId < 0) {
			DebugMessageId = 1;
		}

		if (step < 0 && DebugMessageId > 0) {
			DebugMessageId--;
		} else if (step > 0 && DebugMessageId < LastDebugMessage) {
			DebugMessageId++;
		}

		Cockpit.PilotChannel.Post((byte)DebugMessageId, Cockpit.SquadmateMachines[0]);
	}

	/// <summary>
	/// <c>XMIT</c>'s press on page 0: with the command screen's <see cref="Es1HddCommandScreen.Byte104"/> clear the
	/// screen's message row; otherwise, with a pilot selected, the screen's <see cref="Es1HddCommandScreen.Word5D"/> as
	/// the order record's verb, the order sent to the selected slot (<see cref="Es1Cockpit.SendOrderToSlot"/>), the arrow
	/// in the other views' cursor slot and the selection cleared. The order record is <c>HddOrderRecord</c>
	/// (<c>000b0ddc</c>), the global the MFD's transmit fills, <see cref="Mfd.Es1MfdDisplay.Order"/>.
	/// </summary>
	private void Transmit() {
		if (!CommandScreen.Byte104) {
			// PLACEHOLDER: 00040834(command screen, 1, 0), ES2's HddCommandScreen_SetMessageRow's place.
			return;
		}

		// PLACEHOLDER: 0004075c(command screen), ES2's HddCommandScreen_CommitPick's place.
		if (SelectedSlot == -1) {
			return;
		}

		var order = Cockpit.Mfd.Order;
		order.Verb = CommandScreen.Word5D;
		// PLACEHOLDER: 0004070c(command screen, the record), ES2's HddCommandScreen_FillOrderRecord's place.
		Cockpit.SendOrderToSlot(SelectedSlot, order);
		Cockpit.Cursors.SetOffForwardCursor(Es1CockpitCursors.Arrow);
		// PLACEHOLDER: 000407d4(command screen), ES2's HddCommandScreen_CancelTransmission's place.
		SelectPilot(-1);
	}

	/// <summary>
	/// The subject step's end (docs/es1/simulation/heads-down-display.md#subject-stepping): the indicator's flag follows
	/// the subject's player byte (<see cref="IEs1SimObject.IsPlayer"/>, none for no subject), and when it changes the
	/// indicator is filled <see cref="IndicatorLit"/> or <see cref="IndicatorUnlit"/> and copied to the page on the
	/// screen one column wider (<see cref="Es1Cockpit.DrawToCockpit"/> with 1); either way page 1's update flag and the
	/// paint stage 2.
	/// </summary>
	private void StepIndicator() {
		bool player = DamageSubject is { IsPlayer: true };
		if (IndicatorFlag != player) {
			IndicatorFlag = player;
			FillIndicator(Cockpit.PushCanvasContext());
			var rect = TitleIndicator;
			Cockpit.DrawToCockpit(rect with { X1 = rect.X1 + 1 }, 1);
		}

		_updateFlags[DamagePage] = true;
		Dropout.Repaint = Es1GaugeDropout.RepaintPasses;
	}

	/// <summary>
	/// The paint stages on the stage <c>+0x75</c> (docs/es1/simulation/heads-down-display.md#drawing-it). At 2: with the
	/// page's full-repaint flag clear and its update flag set, the page's update with the stage stored; with the flag set,
	/// the page's repaint and <see cref="SetTitle"/>; either lowering the stage. At 1: the same update with the full-repaint
	/// flag clear; with it set, the screen rect copied from the page on the screen to the page drawn
	/// (<see cref="Es1Cockpit.DrawToCockpit"/> with 0), <see cref="SetTitle"/>, the stage lowered and the flag cleared;
	/// then for page 0 or 1 the update flag set and the stage 2 again. While the pilot channel has a line up the copy
	/// makes the page on the screen the draw page first and lifts the line off around it
	/// (<see cref="Messages.Es1PilotChannel.RestoreBackground"/> before, <see cref="Messages.Es1PilotChannel.Paint"/>
	/// after), and leaves that page the draw page, as retail's code does.
	/// </summary>
	private void PaintStages() {
		var page = _pages[Page];
		if (Dropout.Repaint == 2) {
			if (!_fullRepaint[Page]) {
				if (_updateFlags[Page]) {
					page.Stage = Dropout.Repaint;
					page.Update();
					Dropout.Repaint--;
				}
			} else {
				page.Repaint();
				SetTitle();
				Dropout.Repaint--;
			}
		} else if (Dropout.Repaint == 1) {
			if (!_fullRepaint[Page]) {
				if (_updateFlags[Page]) {
					page.Stage = Dropout.Repaint;
					page.Update();
					Dropout.Repaint--;
				}
			} else {
				var channel = Cockpit.PilotChannel;
				if (!channel.LineUp) {
					Cockpit.DrawToCockpit(ScreenRect, 0);
				} else {
					Cockpit.Display.DrawPage = Cockpit.Display.PairVisible;
					channel.RestoreBackground();
					Cockpit.DrawToCockpit(ScreenRect, 0);
					channel.Paint();
				}

				SetTitle();
				Dropout.Repaint--;
				_fullRepaint[Page] = false;
			}

			if (Page is CommandPage or DamagePage) {
				_updateFlags[Page] = true;
				Dropout.Repaint = Es1GaugeDropout.RepaintPasses;
			} else {
				_updateFlags[Page] = false;
			}
		}
	}

	/// <summary>The title indicator filled by its flag, as the repaint and the subject step fill it.</summary>
	private void FillIndicator(Es1PanelCanvas canvas) =>
		canvas.FillRect(TitleIndicator.X0, TitleIndicator.Y0, TitleIndicator.X1, TitleIndicator.Y1,
			IndicatorFlag ? IndicatorLit : IndicatorUnlit);

	/// <summary>A command-screen press's flags: page 0's update flag and the paint stage 2.</summary>
	private void ScheduleUpdate(int page) {
		_updateFlags[page] = true;
		Dropout.Repaint = Es1GaugeDropout.RepaintPasses;
	}

	/// <summary>A damage-screen view step's flags: page 1's full-repaint flag and the paint stage 2.</summary>
	private void ScheduleFullRepaint(int page) {
		_fullRepaint[page] = true;
		Dropout.Repaint = Es1GaugeDropout.RepaintPasses;
	}

	private string? Text(int group, int entry) => _strings?.Text(group, entry);

	private static bool[] Shows(params int[] buttons) {
		var row = new bool[Es1GauHddBlock.ButtonCount];
		foreach (int button in buttons) {
			row[button] = true;
		}

		return row;
	}
}
