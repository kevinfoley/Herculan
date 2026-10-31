using System.Globalization;
using HercWorks.Core.Data.File.Msn.Es1;
using Herculan.Es1.Shell.MessageBox;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The debriefing's screen object, class <c>DebriefingMenu</c> (vtable <c>00080c4e</c>, <c>0x26</c> bytes)
/// (docs/es1/shell/debriefing.md): screens 1510, a single mission's, and 1520, a career's, the mission's debriefing in a
/// box at the window's top, its page arrows, the results panel (<see cref="ResultsHandler"/>) and MAIN MENU with MISSION >
/// or PROCEED >.
/// </summary>
public sealed class Es1DebriefingMenu : Es1Menu {
	/// <summary>A single mission's debriefing, <c>dlg\menu1510.dlg</c>.</summary>
	public const short Screen = 1510;

	/// <summary>A career's debriefing, <c>dlg\menu1520.dlg</c>.</summary>
	public const short CareerScreen = 1520;

	/// <summary>The results panel, the kind-14 hotspot whose handler is <see cref="ResultsHandler"/>.</summary>
	public const short ResultsPanel = 1522;

	/// <summary>The controls of the five ids of the table at <c>00080c64</c>.</summary>
	private const short PageUp = 101, PageDown = 102, MainMenu = 1000, Mission = 1100, Proceed = 1710;

	/// <summary>The career menu, which PROCEED > sets <see cref="Es1ShellScreens.ReturnScreen"/> to.</summary>
	private const short CareerMenu = 1200;

	/// <summary>The <c>.ANM</c> MAIN MENU and MISSION > put into <c>000aacfe</c>, the string at <c>00080994</c>.</summary>
	private const string LeaveAnm = "anm\\esiege2.anm";

	private readonly Es1ShellDialog _dialog;

	/// <summary>
	/// <c>DebriefingMenu_Ctor</c> (<c>0002c422</c>)(0)'s screen object, built over <c>Menu</c>; its load of the block is
	/// <see cref="Load"/>'s, which screen 1510's and 1520's case runs next.
	/// </summary>
	/// <param name="dialog"><c>Shell_Dialog</c> (<c>0009afc8</c>), which the debriefing is loaded into.</param>
	public Es1DebriefingMenu(Es1MenuContext context, Es1ShellDialog dialog) : base(context) {
		_dialog = dialog;
	}

	/// <summary>
	/// <c>DebriefingMenu_Ctor</c>'s load (docs/es1/shell/debriefing.md#opening-it): the <c>DEB:</c> block (the string
	/// <c>00080c5e</c>) whose ordinal is <see cref="Es1ShellMission.Outcome"/> (<c>000aad58</c>), of
	/// <see cref="Es1ShellMission.TextPath"/> (<c>000aad2a</c>), loaded into the text box with
	/// <see cref="Es1ShellDialog.LoadConditional"/>, on its first page. The waits are those of the box the load deletes.
	/// </summary>
	public IEnumerable<Es1ShellWait> Load() {
		var mission = Context.Mission;
		foreach (var wait in _dialog.LoadConditional(mission.Outcome, Es1MissionText.DebriefingTag, mission.TextPath, mission.ReadFlag)) {
			yield return wait;
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>DebriefingMenu_OnMessage</c> (<c>0002c638</c>) (docs/es1/shell/debriefing.md#the-buttons-and-keys):
	/// by the message's target, the case of the table at <c>00080c64</c> (cases at <c>00080c78</c>); nothing for any other
	/// id, 1522 among them.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var screens = Context.Screens;
		switch (message.Target) {
			case PageUp:
				// 0002c661.
				_dialog.PrevPage();
				break;
			case PageDown:
				// 0002c670.
				_dialog.NextPage();
				break;
			case MainMenu:
			case Mission:
				// 0002c67f and 0002c6a3: the return screen the button's own id.
				screens.ReturnScreen = message.Target;
				screens.AnmName = LeaveAnm;
				screens.AnmMode = 2;
				break;
			case Proceed:
				// 0002c6c7: the target 5555 when the word +0xa8 of the roster's record its word +7 names is not -1, read from the
				// roster in either mode, not through CareerRecord_Current.
				if (Context.Mission.Roster.CurrentRecord.Equipment.LastKind != -1) {
					message = message with { Target = Es1CareerMenu.TechRelease };
				}

				screens.ReturnScreen = CareerMenu;
				break;
		}
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>DebriefingMenu_Dtor</c> (<c>0002c574</c>) (docs/es1/shell/debriefing.md#leaving-it): the
	/// debriefing's box deleted (<see cref="Es1ShellDialog.DeleteBox"/>) on every way out, with no test of
	/// <see cref="Es1ShellScreens.MapDrawCount"/>. The box is the block's, whose destructor waits for nothing, so its
	/// deletion is run through here.
	/// </summary>
	public override void Dtor() {
		foreach (var _ in _dialog.DeleteBox()) {
		}
	}

	/// <summary>The font slots of the panel: <c>cstats_2</c> (<c>000aacea</c>) for the labels, <c>cstats_1</c> (<c>000aace6</c>) for the values.</summary>
	private const int LabelFontSlot = 9, ValueFontSlot = 8;

	/// <summary>The panel's fill and frame color, <c>Raster_FillFrameRectColours</c>(<c>0x1f</c>, <c>0x1f</c>, rect).</summary>
	private const byte PanelColor = 0x1f;

	/// <summary>
	/// The text at <c>0007d808</c>, <c>NONE</c> in the image, the panel's new technology recovered, which
	/// <c>MessageBox_PickT1Line</c> (<see cref="Es1MessageBox.PickT1Line"/>) compares for kind 7. Its one write
	/// found, <c>Tech_FormatLockedName</c> (<c>000021b5</c>)'s, is unported, and the shell after a mission starts from the
	/// image; what writes it is debriefing.md's Open.
	/// </summary>
	internal static readonly string NewTechnology = "NONE";

	/// <summary>
	/// The handler <c>Menu_BuildFromDlg</c> gives the hotspot 1522, <c>DebriefingResultsHotspot_Draw</c>
	/// (<c>000175d7</c>), over <paramref name="context"/>'s shared data and mission.
	/// </summary>
	public static Es1HotspotHandler ResultsHandler(Es1MenuContext context) => (render, rect, _) => DrawResults(context, render, rect);

	/// <summary>
	/// <c>DebriefingResultsHotspot_Draw</c> (<c>000175d7</c>)(<paramref name="rect"/>, id)
	/// (docs/es1/shell/debriefing.md#the-results-panel), in the window's render context: the alignment 0, the rect filled
	/// and framed in <see cref="PanelColor"/>; the ten <c>eng\watext.bin</c> labels in <c>cstats_2</c>; the values, each
	/// <c>sprintf</c>'s decimal (<c>%ld</c> for the salvage, <c>%d</c> for the rest), in <c>cstats_1</c>; then the
	/// alignment 2 with the rect's width as the field width and <see cref="NewTechnology"/> at (<i>x0</i>, <i>y0</i> + 81),
	/// the alignment left at 2.
	/// </summary>
	private static void DrawResults(Es1MenuContext context, Es1ShellRenderContext render, Es1Rect rect) {
		var data = context.Data;
		var mission = context.Mission;
		var results = mission.Results;
		var labels = data.Fonts[LabelFontSlot];
		var values = data.Fonts[ValueFontSlot];
		int x0 = rect.X0, y0 = rect.Y0;

		render.Alignment = 0;
		// Raster_FillFrameRectColours (0004b8d8): the rect filled under the brush {0, fill}, then its edges under {4, frame}.
		var canvas = render.Canvas;
		canvas.FillRect(rect.X0, rect.Y0, rect.X1, rect.Y1, PanelColor);
		canvas.OutlineRect(rect.X0, rect.Y0, rect.X1, rect.Y1, PanelColor);

		// The labels, in the code's order: SALVAGE:, KILLS, HERCS:, FLYERS:, STRUCTURES:, SQUAD LOSSES:, WEAPONS RECOVERED:,
		// NEW TECHNOLOGY RECOVERED:, YOURS and TOTAL.
		render.DrawString(labels, Line(data, 0xab), x0 + 5, y0 + 8);
		render.DrawString(labels, Line(data, 0xac), x0 + 5, y0 + 17);
		render.DrawString(labels, Line(data, 0xad), x0 + 14, y0 + 26);
		render.DrawString(labels, Line(data, 0xaf), x0 + 14, y0 + 35);
		render.DrawString(labels, Line(data, 0xae), x0 + 14, y0 + 44);
		render.DrawString(labels, Line(data, 0xb3), x0 + 5, y0 + 53);
		render.DrawString(labels, Line(data, 0xb4), x0 + 5, y0 + 62);
		render.DrawString(labels, Line(data, 0xb5), x0 + 5, y0 + 71);
		render.DrawString(labels, Line(data, 0xb1), x0 + 83, y0 + 17);
		render.DrawString(labels, Line(data, 0xb2), x0 + 118, y0 + 17);

		// The salvage, the dword 0007fcf4.
		render.DrawString(values, Decimal(results.Salvage), x0 + 89, y0 + 8);

		// YOURS: the words +0x25, +0x29 and +0x27 of CareerRecord_Current's record, its pilot entry 0's gains of HERCs, flyers
		// and structures. In Shell_Mode 0 the results' read sets the record before this screen opens; the test for it is an
		// engine guard.
		if (mission.CurrentRecord is { } record) {
			short[] gains = record.Pilots[0].Gains;
			render.DrawString(values, Decimal(gains[0]), x0 + 94, y0 + 26);
			render.DrawString(values, Decimal(gains[2]), x0 + 94, y0 + 35);
			render.DrawString(values, Decimal(gains[1]), x0 + 94, y0 + 44);
		}

		// SQUAD LOSSES:, the word 0007ebca.
		render.DrawString(values, Decimal(results.LostPilots), x0 + 94, y0 + 53);

		// TOTAL: the words 0009ae5c, 0009ae60 and 0009ae5e, HERCs, flyers and structures.
		render.DrawString(values, Decimal(results.Totals[0]), x0 + 127, y0 + 26);
		render.DrawString(values, Decimal(results.Totals[2]), x0 + 127, y0 + 35);
		render.DrawString(values, Decimal(results.Totals[1]), x0 + 127, y0 + 44);

		// WEAPONS RECOVERED:, the pair count 000817cc; the results' read keeps no pairs for a negative count, so such a count
		// shows 0 here where retail shows the word.
		render.DrawString(values, Decimal(results.Pairs.Length), x0 + 127, y0 + 62);

		render.Alignment = 2;
		render.FieldWidth = rect.X1 - rect.X0;
		render.DrawString(values, NewTechnology, x0, y0 + 81);
	}

	/// <summary>
	/// <c>Text_GetString</c>(<c>0009afa8</c>, <paramref name="number"/>), a line of <c>eng\watext.bin</c>; none, so nothing
	/// drawn, past the table or without it, an engine guard for its missing bounds test.
	/// </summary>
	private static string? Line(Es1ShellData data, int number) => data.Text is { } text && number < text.Length ? text[number] : null;

	/// <summary><c>sprintf</c>'s <c>%d</c> and <c>%ld</c>: the signed decimal.</summary>
	private static string Decimal(int value) => value.ToString(CultureInfo.InvariantCulture);
}
