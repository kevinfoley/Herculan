using System.Globalization;
using System.Runtime.CompilerServices;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Shell.Hangar;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// BUILD's screen object, class <c>FabricationMenu</c> (vtable <c>00080ece</c>, <c>0x26</c> bytes)
/// (docs/es1/shell/repair-and-build.md#build--screen-2000): screen 2000, a window down the right of the hangar's picture
/// with one chassis' picture, type, mass, speed and weapon capacity, the salvage it needs and the salvage available, the
/// arrows that step through the chassis the career has made available, and BUILD, which starts that HERC in the marked
/// slot or, while one is being built, reads SCRAP and gives its salvage back.
/// </summary>
public sealed class Es1FabricationMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu2000.dlg</c>.</summary>
	public const short Screen = 2000;

	/// <summary>The information panel's hotspot, drawn by <see cref="PanelHandler"/>.</summary>
	public const short Panel = 2005;

	/// <summary>The four ids of <c>FabricationMenu_CaseTable</c> (<c>00080ef4</c>): HERCS' two arrows, BUILD and DONE.</summary>
	private const short PrevChassis = 2003, NextChassis = 2004, BuildButton = 2001, Done = 1710;

	/// <summary>The lines of <c>eng\watext.bin</c> BUILD reads: SCRAP with a HERC being built, BUILD without.</summary>
	private const short ScrapLine = 0xcd, BuildLine = 0xce;

	/// <summary><c>Chassis_MassTable</c> (<c>000808a8</c>): each chassis' mass in tons.</summary>
	private static readonly short[] MassTable = [18, 27, 36, 45, 50, 54, 63, 77];

	/// <summary><c>Chassis_SpeedTable</c> (<c>000808c8</c>): each chassis' speed in KPH.</summary>
	private static readonly short[] SpeedTable = [84, 80, 76, 71, 64, 56, 52, 46];

	/// <summary>
	/// <c>FabricationMenu_Ctor</c> (<c>000307c9</c>)(0): the screen object, and <c>res32\blueherx.dba</c> loaded with
	/// <c>Resource_LoadBitmapArray</c> into <c>Fabrication_ChassisArt</c> (<see cref="Es1MenuBanks.ChassisArt"/>).
	/// </summary>
	public Es1FabricationMenu(Es1MenuContext context) : base(context) {
		context.Banks.ChassisArt = context.Banks.Load("BLUEHERX.DBA");
	}

	private Es1HangarBay? Bay => Context.Screens.Bay;

	/// <summary>
	/// <c>Menu_Open</c>'s change for 2000, before the focus (docs/es1/shell/repair-and-build.md#build--screen-2000): with no
	/// marked HERC (<see cref="Es1Hangar.HasMarkedHerc"/>), BUILD made inactive when <paramref name="salvage"/>
	/// (<c>Career_Salvage</c>) is below the price × 1000 of <paramref name="chassis"/> (<c>Fabrication_Chassis</c>),
	/// compared unsigned; with one, BUILD given line <c>0xcd</c>, SCRAP (slot <c>+0x3c</c>,
	/// <see cref="Es1DUIControl.SetTextFromTable"/>), and both arrows made inactive. BUILD and the arrows are found in any
	/// state (<c>DUIWindow_FindControlById</c>). The chassis is not tested against what the career has made available.
	/// With no hangar there is no marked HERC here, an engine guard.
	/// </summary>
	public static void SetOpeningControls(Es1DUIWindow window, Es1Hangar? hangar, int salvage, short chassis,
			Action<Es1DUIWindow, short> makeInactive) {
		if (hangar?.HasMarkedHerc() != true) {
			if ((uint)salvage < (uint)(Price(chassis) * 1000)) {
				makeInactive(window, window.FindControlById(BuildButton));
			}

			return;
		}

		if (window.FindControlById(BuildButton) is var build and >= 0) {
			window.Controls[build].SetTextFromTable(ScrapLine);
		}

		makeInactive(window, window.FindControlById(PrevChassis));
		makeInactive(window, window.FindControlById(NextChassis));
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>FabricationMenu_OnMessage</c> (<c>000309d7</c>) (docs/es1/shell/repair-and-build.md#build--screen-2000):
	/// the target looked up among the four ids of <c>FabricationMenu_CaseTable</c> (<c>00080ef4</c>): 2003
	/// (<c>00030a08</c>) and 2004 (<c>00030b55</c>) <see cref="StepChassis"/>, BUILD given
	/// <see cref="AffordableState"/> and the panel marked; 2001 (<c>00030c86</c>) <see cref="BuildOrScrap"/> and the panel
	/// marked; 1710 (<c>00030e77</c>), DONE, <c>CareerRecord_SaveCurrent</c> (<see cref="Es1ShellMission.SaveCurrentRecord"/>),
	/// the target then passing to <see cref="Es1ShellScreens.HandleMessage"/> as a screen. 2001's case, which waits, is
	/// <see cref="OnMessageWaiting"/>'s.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		switch (message.Target) {
			case PrevChassis:
			case NextChassis:
				StepChassis(message.Target == NextChassis);
				SetState(BuildButton, AffordableState());
				MarkPanel();
				break;
			case Done:
				Context.Mission.SaveCurrentRecord();
				break;
		}
	}

	/// <summary>2001's case (<c>00030c86</c>): <see cref="BuildOrScrap"/>, which waits, and the panel marked; any other <see cref="OnMessage"/>.</summary>
	public override IEnumerable<Es1ShellWait> OnMessageWaiting(StrongBox<Es1MenuMessage> message) =>
		message.Value.Target == BuildButton ? BuildAndMark() : base.OnMessageWaiting(message);

	/// <summary><see cref="BuildOrScrap"/>, then the panel marked.</summary>
	private IEnumerable<Es1ShellWait> BuildAndMark() {
		foreach (var wait in BuildOrScrap()) {
			yield return wait;
		}

		MarkPanel();
	}

	/// <summary>Slot <c>+4</c>, <c>FabricationMenu_Dtor</c> (<c>00030910</c>): <see cref="Es1MenuBanks.ChassisArt"/> destroyed (<c>GLBitmapArray_Dtor</c>(array, 3)), the base vtables stored and the menu freed.</summary>
	public override void Dtor() {
		Context.Banks.ChassisArt = null;
	}

	/// <summary>
	/// 2003's and 2004's walk: <c>Fabrication_Chassis</c> (<see cref="Es1MenuGlobals.FabricationChassis"/>) less 1, from 0
	/// to 7, or plus 1 modulo 8 for <paramref name="forward"/>, until <c>Tech_IsAvailableMenuHandlersCopy</c>
	/// (<c>00030e83</c>, a copy of <c>Tech_IsAvailable</c>, <see cref="Es1EquipmentState.IsAvailable"/>)(the
	/// <c>CareerRecord_Current</c> record's equipment <c>+0xa8</c>, chassis, 0) answers non-zero. Retail walks for ever when
	/// no chassis is available; here the walk stops after eight steps, and with no record none is available, engine guards.
	/// </summary>
	private void StepChassis(bool forward) {
		var globals = Context.Globals;
		var equipment = Context.Mission.CurrentRecord?.Equipment;
		for (int step = 0; step < Es1HercArt.ChassisCount; step++) {
			if (forward) {
				globals.FabricationChassis = (short)((globals.FabricationChassis + 1) % Es1HercArt.ChassisCount);
			} else {
				globals.FabricationChassis--;
				if (globals.FabricationChassis < 0) {
					globals.FabricationChassis = Es1HercArt.ChassisCount - 1;
				}
			}

			if (equipment != null && (byte)equipment.IsAvailable(globals.FabricationChassis, 0) != 0) {
				return;
			}
		}
	}

	/// <summary>
	/// BUILD's state after an arrow: up when the shown chassis' price × 1000 is at most <c>Career_Salvage</c>, compared
	/// unsigned, and inactive otherwise.
	/// </summary>
	private short AffordableState() =>
		(uint)(Price(Context.Globals.FabricationChassis) * 1000) <= (uint)Context.Mission.Roster.Salvage ? Es1DUIControl.Up : Es1DUIControl.Inactive;

	/// <summary>
	/// 2001's case (<c>00030c86</c>): with no marked HERC (<see cref="Es1Hangar.HasMarkedHerc"/>), the shown chassis built
	/// (<see cref="Es1HangarBay.BuildIntoSelected"/>), BUILD given line <c>0xcd</c>, SCRAP, and both arrows made inactive;
	/// with one, the selected HERC, the marked one, scrapped (<see cref="Es1HangarBay.ScrapSelectedHerc"/>), the draw page
	/// wiped onto the visible one (<see cref="Es1ScreenBlinds.Copy"/>(8, the visible page, the draw page)), BUILD given
	/// line <c>0xce</c>, BUILD, and both arrows made up. The waits are the scrap's and the wipe's. With no bay nothing is
	/// built, scrapped or wiped, an engine guard.
	/// </summary>
	private IEnumerable<Es1ShellWait> BuildOrScrap() {
		var bay = Bay;
		if (Context.Mission.Hangar?.HasMarkedHerc() != true) {
			bay?.BuildIntoSelected(Context.Globals.FabricationChassis);
			SetText(BuildButton, ScrapLine);
			SetState(PrevChassis, Es1DUIControl.Inactive);
			SetState(NextChassis, Es1DUIControl.Inactive);
			yield break;
		}

		if (bay != null) {
			foreach (var wait in bay.ScrapSelectedHerc()) {
				yield return wait;
			}

			var display = bay.Surface.Display;
			foreach (var wait in bay.Blinds.Copy(8, display.VisiblePage, display.DrawPage)) {
				yield return wait;
			}
		}

		SetText(BuildButton, BuildLine);
		SetState(PrevChassis, Es1DUIControl.Up);
		SetState(NextChassis, Es1DUIControl.Up);
	}

	/// <summary>The control <c>DUIWindow_FindControlById</c> finds for <paramref name="id"/> given state <paramref name="state"/> and <i>changed</i>; a missing control is passed over, an engine guard.</summary>
	private void SetState(short id, short state) {
		if (Window is { } window && window.FindControlById(id) is var index and >= 0) {
			window.Controls[index].State = state;
			window.Controls[index].Changed = true;
		}
	}

	/// <summary>The control's slot <c>+0x3c</c>, <c>MTButton_SetTextFromTable</c>, with <paramref name="line"/>; a missing control is passed over, an engine guard.</summary>
	private void SetText(short id, short line) {
		if (Window is { } window && window.FindControlById(id) is var index and >= 0) {
			window.Controls[index].SetTextFromTable(line);
		}
	}

	/// <summary>
	/// The pressed byte of the hotspot <c>DUIWindow_FindHotspotById</c> finds for 2005 set, so the next update draws the
	/// panel again. Retail writes at index −1 for a window with no such hotspot; here nothing is set, an engine guard.
	/// </summary>
	private void MarkPanel() {
		if (Window is { } window && window.FindHotspotById(Panel) is var index and >= 0) {
			window.Hotspots[index].Pressed = true;
		}
	}

	/// <summary>The chassis' word of <c>Chassis_PriceTable</c> (<c>000808b8</c>) in tons; retail reads outside the eight for any other chassis, 0 here, an engine guard.</summary>
	private static int Price(short chassis) => Word(Es1RepairCosts.ChassisPrices, chassis);

	/// <summary>The chassis' word of <paramref name="table"/>; 0 for a chassis outside the eight, which retail reads outside the table, an engine guard.</summary>
	private static short Word(short[] table, short chassis) => chassis >= 0 && chassis < table.Length ? table[chassis] : (short)0;

	/// <summary>The font slots of the panel: <c>nm_green</c> (<c>000aacda</c>), <c>nm_red1</c> (<c>000aacde</c>) and <c>nm_white</c> (<c>000aacee</c>).</summary>
	private const int GreenFont = 5, RedFont = 6, WhiteFont = 10;

	/// <summary>The panel's fill and frame color, <c>Raster_FillFrameRectColours</c>(<c>0x1f</c>, <c>0x1f</c>, rect).</summary>
	private const byte PanelColor = 0x1f;

	/// <summary>The progress box's fill and frame color, <c>Raster_FillFrameRectColours</c>(4, 4, box).</summary>
	private const byte ProgressColor = 4;

	/// <summary>
	/// The panel's lines of <c>eng\watext.bin</c>: TYPE:, MASS:, SPEED:, CAPACITY:, % COMPLETE:, SALVAGE REQUIRED:,
	/// SALVAGE AVAILABLE:, SALVAGE ALLOCATED:, UNDER CONSTRUCTION, and the first chassis name.
	/// </summary>
	private const short TypeLabel = 0xc4, MassLabel = 0xc5, SpeedLabel = 0xc6, CapacityLabel = 0xc7, CompleteLabel = 0xc8,
		RequiredLabel = 0xc9, AvailableLabel = 0xca, AllocatedLabel = 0xcb, UnderConstructionLine = 0xcc, ChassisLines = 0x1b;

	/// <summary>The formats the values are built with: <c>%d TONS</c> (<c>000808e8</c>), <c>%d KPH</c> (<c>000808f0</c>), <c>%d WEAPONS</c> (<c>000808f7</c>), <c>%d</c> (<c>00080902</c>) and <c>%3d TONS</c> (<c>00080905</c>, <c>0008090e</c>, <c>00080917</c>).</summary>
	private const string MassFormat = "{0} TONS", SpeedFormat = "{0} KPH", CapacityFormat = "{0} WEAPONS", ProgressFormat = "{0}",
		SalvageFormat = "{0,3} TONS";

	/// <summary>The progress box's corners and the field UNDER CONSTRUCTION is centered in.</summary>
	private const int ProgressLeft = 16, ProgressTop = 111, ProgressRight = 118, ProgressBottom = 117, ProgressFieldWidth = 0x66;

	/// <summary>The handler <c>Menu_BuildFromDlg</c> gives hotspot 2005, <c>FabricationMenu_DrawPanel</c> (<c>0002845c</c>), over <paramref name="context"/>.</summary>
	public static Es1HotspotHandler PanelHandler(Es1MenuContext context) => (render, rect, id) => DrawPanel(context, render, rect);

	/// <summary>
	/// <c>FabricationMenu_DrawPanel</c> (<c>0002845c</c>)(<paramref name="rect"/>, id)
	/// (docs/es1/shell/repair-and-build.md#build--screen-2000), in the window's render context, the alignment 0: the rect
	/// filled and framed in <see cref="PanelColor"/>; with a marked HERC (<see cref="Es1Hangar.HasMarkedHerc"/>),
	/// <c>Fabrication_Chassis</c> (<see cref="Es1MenuGlobals.FabricationChassis"/>) set to the chassis of the HERC in
	/// <c>Hangar_SelectedSlot</c>, −1 for none; frame <i>chassis</i> of <see cref="Es1MenuBanks.ChassisArt"/> at the
	/// rect's corner + (1, 1); the labels in <c>nm_green</c> and the values in <c>nm_white</c>: TYPE: at (75, 34) and line
	/// <c>0x1b</c> + chassis at (79, 43), MASS: at (75, 52) and <see cref="MassTable"/> at (79, 61), SPEED: at (75, 70) and
	/// <see cref="SpeedTable"/> at (79, 79), CAPACITY: at (75, 88) and <see cref="Es1HercRecord.MountCounts"/> at (79, 97);
	/// with a marked HERC, the box (16, 111)–(118, 117) filled and framed in <see cref="ProgressColor"/>, UNDER
	/// CONSTRUCTION in <c>nm_red1</c> at (17, 117) centered (alignment 2) in a field 102 wide, the alignment 0 again, %
	/// COMPLETE: at (17, 126) and (<i>c</i> − <see cref="Es1Hangar.MarkedSlotWord"/>) × 100 / <i>c</i> as a word at
	/// (67, 126), <i>c</i> the chassis' word of <c>Chassis_BuildMissions</c> (<c>000808d8</c>); SALVAGE ALLOCATED: with a
	/// marked HERC and SALVAGE REQUIRED: without at (17, 135), SALVAGE AVAILABLE: at (17, 144), then at (87, 135)
	/// <see cref="Es1Hangar.Dword"/> (<c>Hangar_MarkedValue</c>) or the chassis' price in tons, and at (87, 144)
	/// <c>Career_Salvage</c> / 1000, unsigned. The field width stays 102. Retail reads the art and the tables at a chassis
	/// outside the eight unchecked; such a chassis draws no picture and reads 0 from each table here, engine guards.
	/// </summary>
	private static void DrawPanel(Es1MenuContext context, Es1ShellRenderContext render, Es1Rect rect) {
		var globals = context.Globals;
		var data = context.Data;
		var hangar = context.Mission.Hangar;
		render.Alignment = 0;
		var canvas = render.Canvas;
		canvas.FillRect(rect.X0, rect.Y0, rect.X1, rect.Y1, PanelColor);
		canvas.OutlineRect(rect.X0, rect.Y0, rect.X1, rect.Y1, PanelColor);
		bool marked = hangar?.HasMarkedHerc() == true;
		if (marked) {
			globals.FabricationChassis = hangar!.HercIn(hangar.SelectedSlot)?.Chassis ?? -1;
		}

		short chassis = globals.FabricationChassis;
		render.Blit(Frame(context.Banks.ChassisArt, chassis), rect.X0 + 1, rect.Y0 + 1);

		var green = data.Fonts[GreenFont];
		var white = data.Fonts[WhiteFont];
		render.DrawString(green, Line(data, TypeLabel), 75, 34);
		render.DrawString(white, Line(data, (short)(ChassisLines + chassis)), 79, 43);
		render.DrawString(green, Line(data, MassLabel), 75, 52);
		render.DrawString(white, Format(MassFormat, Word(MassTable, chassis)), 79, 61);
		render.DrawString(green, Line(data, SpeedLabel), 75, 70);
		render.DrawString(white, Format(SpeedFormat, Word(SpeedTable, chassis)), 79, 79);
		render.DrawString(green, Line(data, CapacityLabel), 75, 88);
		render.DrawString(white, Format(CapacityFormat, Word(Es1HercRecord.MountCounts, chassis)), 79, 97);

		if (marked) {
			canvas.FillRect(ProgressLeft, ProgressTop, ProgressRight, ProgressBottom, ProgressColor);
			canvas.OutlineRect(ProgressLeft, ProgressTop, ProgressRight, ProgressBottom, ProgressColor);
			render.Alignment = 2;
			render.FieldWidth = ProgressFieldWidth;
			render.DrawString(data.Fonts[RedFont], Line(data, UnderConstructionLine), 17, 117);
			render.Alignment = 0;
			render.DrawString(green, Line(data, CompleteLabel), 17, 126);
			int missions = Word(Es1CareerAfterMission.MarkedHercMissions, chassis);
			short progress = missions == 0 ? (short)0 : (short)((missions - hangar!.MarkedSlotWord) * 100 / missions);
			render.DrawString(white, Format(ProgressFormat, progress), 67, 126);
		}

		render.DrawString(green, Line(data, marked ? AllocatedLabel : RequiredLabel), 17, 135);
		render.DrawString(green, Line(data, AvailableLabel), 17, 144);
		render.DrawString(white, Format(SalvageFormat, marked ? hangar!.Dword : Price(chassis)), 87, 135);
		render.DrawString(white, Format(SalvageFormat, (uint)context.Mission.Roster.Salvage / 1000), 87, 144);
	}

	/// <summary>A value formatted as <c>sprintf</c> formats it, with the invariant culture.</summary>
	private static string Format(string format, object value) => string.Format(CultureInfo.InvariantCulture, format, value);

	/// <summary>Frame <paramref name="index"/> of <paramref name="bank"/>; none outside it or without it.</summary>
	private static DynamixBitmap? Frame(IReadOnlyList<DynamixBitmap>? bank, int index) =>
		bank != null && index >= 0 && index < bank.Count ? bank[index] : null;

	/// <summary>
	/// <c>Text_GetString</c>(<c>0009afa8</c>, <paramref name="number"/>), a line of <c>eng\watext.bin</c>; none, so nothing
	/// drawn, outside the table or without it, an engine guard.
	/// </summary>
	private static string? Line(Es1ShellData data, short number) => data.Text is { } text && number >= 0 && number < text.Length ? text[number] : null;
}
