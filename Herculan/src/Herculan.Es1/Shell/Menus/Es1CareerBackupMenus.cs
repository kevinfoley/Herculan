using System.Globalization;
using HercWorks.Core.Data.File.Dat.Shell;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// The backups' screen object, class <c>CareerRecordsMenu</c> (vtable <c>00081e90</c>) (docs/es1/shell/career.md#backups):
/// screen 1220, CAREER BACKUPS, the list of the career's backups, SAVE, RESTORE, STATS and DONE.
/// </summary>
public sealed class Es1CareerRecordsMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1220.dlg</c>.</summary>
	public const short Screen = 1220;

	/// <summary>The list of the backups.</summary>
	public const short BackupList = 1223;

	/// <summary>The controls of the six ids of the table at <c>00080a24</c>.</summary>
	private const short UpArrow = 101, DownArrow = 102, Save = 1221, Restore = 1222, Stats = Es1CareerStatsAlert.Screen,
		Done = Es1CareerMenu.Screen;

	/// <summary>The <c>.ANM</c> RESTORE puts into <c>000aacfe</c>, the string at <c>00080984</c>.</summary>
	private const string MissionAnm = "anm\\esiege3.anm";

	public Es1CareerRecordsMenu(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>CareerRecordsMenu_OnMessage</c> (<c>0002af94</c>) (docs/es1/shell/career.md#backups): by the
	/// message's target, the case of the table at <c>00080a24</c> (cases at <c>00080a3c</c>); nothing for any other target.
	/// Each case reads the list 1223 found with <c>DUIWindow_FindEnabledControlById</c> (<see cref="Es1Menu.FindList"/>);
	/// with none, the steps that read it are passed over, an engine guard.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		var roster = Context.Mission.Roster;
		switch (message.Target) {
			case UpArrow:
				// 0002afba: the list's slot +0x1c.
				FindList(BackupList)?.SelectPrev();
				break;
			case DownArrow:
				// 0002aff4: the list's slot +0x20.
				FindList(BackupList)?.SelectNext();
				break;
			case Save:
				// 0002b02e.
				SaveCase(ref message);
				break;
			case Restore:
				// 0002b0eb.
				RestoreCase(ref message);
				break;
			case Stats:
				// 0002b22e: the record the stats show, the backup the selected line names.
				if (FindList(BackupList) is { } list) {
					roster.Current = roster.NthUsedRecord((short)(list.GetValue() + 1));
					roster.BackupLine = list.GetValue();
				}

				break;
			case Done:
				// 0002b2bc.
				Context.Screens.ReturnScreen = Es1CareerMenu.Screen;
				break;
		}
	}

	/// <summary>
	/// SAVE (docs/es1/shell/career.md#save-and-the-overwrite-warning): the target 1240 when
	/// <see cref="Es1CareerRoster.SaveSlotUsed"/>; otherwise the backup made (<see cref="Es1CareerRoster.SaveBackup"/>),
	/// the list given <see cref="Es1CareerRoster.BackupListLines"/> through its slot <c>+0x44</c> and the line
	/// <see cref="Es1CareerRoster.CountUsedRecords"/> − 1 selected through its slot <c>+0x24</c>. The target 1221 opens no
	/// screen.
	/// </summary>
	private void SaveCase(ref Es1MenuMessage message) {
		var roster = Context.Mission.Roster;
		if (roster.SaveSlotUsed()) {
			message = message with { Target = Es1SaveWarningAlert.Screen };
			return;
		}

		roster.SaveBackup();
		var lines = roster.BackupListLines();
		FindList(BackupList)?.SetLines(lines);
		FindList(BackupList)?.SelectLine((short)(roster.CountUsedRecords() - 1));
	}

	/// <summary>
	/// RESTORE (docs/es1/shell/career.md#restore): <see cref="Es1CareerRoster.RestoreBackup"/>(the selected line + 1);
	/// <c>Career_BackupLine</c> the selected line; the target record 0's word <c>+4</c>. With the player's pilot lost
	/// (<c>PilotEntry_Condition</c> 0), the target 1250 and <see cref="Es1ShellScreens.ReturnScreen"/> 1220; otherwise,
	/// unless the target is 1710, the career's mission loaded; <c>Career_Save</c>(roster, its dword <c>+7</c>), whose low
	/// word, <see cref="Es1CareerRoster.Current"/>, is 0; and <c>anm\esiege3.anm</c> to play after the next screen.
	/// </summary>
	private void RestoreCase(ref Es1MenuMessage message) {
		var roster = Context.Mission.Roster;
		if (FindList(BackupList) is not { } list) {
			return;
		}

		roster.RestoreBackup((short)(list.GetValue() + 1));
		roster.BackupLine = list.GetValue();
		var record = roster.CurrentRecord;
		message = message with { Target = record.Word4 };
		if (Es1CareerRoster.PlayerCondition(record) == 0) {
			message = message with { Target = Es1KIAWarningAlert.KilledScreen };
			Context.Screens.ReturnScreen = Screen;
			return;
		}

		if (message.Target != Es1CareerMenu.ServiceBay) {
			// PLACEHOLDER: Career_StartMission (00016e93)(Career_Roster, 0) and Shell_PrepareMission (0002a523), the career's
			// mission load, P10's.
		}

		roster.Save(roster.Current);
		Context.Screens.AnmName = MissionAnm;
		Context.Screens.AnmMode = 1;
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The overwrite warning's screen object, class <c>SaveWarningAlert</c> (vtable <c>00081ed8</c>)
/// (docs/es1/shell/career.md#save-and-the-overwrite-warning): screen 1240, the backup SAVE would replace, CANCEL and
/// OVERWRITE.
/// </summary>
public sealed class Es1SaveWarningAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1240.dlg</c>.</summary>
	public const short Screen = 1240;

	/// <summary>The label whose text <c>Menu_BuildFromDlg</c> takes from <see cref="Es1CareerRoster.SaveSlotText"/>.</summary>
	public const short SlotLabel = 1223;

	/// <summary>OVERWRITE's and CANCEL's ids.</summary>
	private const short Overwrite = 100, Cancel = 99;

	public Es1SaveWarningAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>SaveWarningAlert_OnMessage</c> (<c>0002b30a</c>): OVERWRITE makes the backup
	/// (<see cref="Es1CareerRoster.SaveBackup"/>); both buttons turn the target into the backups, 1220.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target == Overwrite) {
			Context.Mission.Roster.SaveBackup();
			message = message with { Target = Es1CareerRecordsMenu.Screen };
		} else if (message.Target == Cancel) {
			message = message with { Target = Es1CareerRecordsMenu.Screen };
		}
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}
}

/// <summary>
/// The stats' screen object, class <c>CareerStatsAlert</c> (vtable <c>00081eb4</c>) (docs/es1/shell/career.md#the-stats):
/// screen 1230, CAREER STATS, the panel of a career record (<see cref="StatsHandler"/>) and DONE.
/// </summary>
public sealed class Es1CareerStatsAlert : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1230.dlg</c>.</summary>
	public const short Screen = 1230;

	/// <summary>The panel, the kind-14 hotspot whose handler is <see cref="StatsHandler"/>.</summary>
	public const short StatsPanel = 1231;

	/// <summary>DONE's id, a <i>back</i>.</summary>
	private const short Done = 99;

	public Es1CareerStatsAlert(Es1MenuContext context) : base(context) {
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>CareerStatsAlert_OnMessage</c> (<c>0002b2cc</c>): for DONE, <see cref="Es1CareerRoster.Current"/>
	/// set back to <c>Roster_NthUsedRecord</c>(roster, 0), 0; <see cref="Es1ShellScreens.HandleMessage"/>'s <i>back</i>
	/// then returns to the screen STATS was pressed on.
	/// </summary>
	public override void OnMessage(ref Es1MenuMessage message) {
		if (message.Target == Done) {
			var roster = Context.Mission.Roster;
			roster.Current = roster.NthUsedRecord(0);
		}
	}

	/// <summary>Slot <c>+4</c>, the class's destructor: its vtable, then <c>Menu</c>'s, stored and the menu freed.</summary>
	public override void Dtor() {
	}

	/// <summary>The font slots of the panel: <c>cstats_2</c> (<c>000aacea</c>) for the labels, <c>cstats_1</c> (<c>000aace6</c>) for the values.</summary>
	private const int LabelFontSlot = 9, ValueFontSlot = 8;

	/// <summary>The panel's fill and frame color, <c>Raster_FillFrameRectColours</c>(<c>0x1f</c>, <c>0x1f</c>, rect).</summary>
	private const byte PanelColor = 0x1f;

	/// <summary>The lines of <c>eng\watext.bin</c> STATUS: reads, ACTIVE and DECEASED.</summary>
	private const int ActiveLine = 0xb6, DeceasedLine = 0xb7;

	/// <summary>
	/// The handler <c>Menu_BuildFromDlg</c> gives the hotspot 1231, <c>CareerStatsHotspot_Draw</c> (<c>0001705e</c>), over
	/// <paramref name="context"/>'s shared data and mission.
	/// </summary>
	public static Es1HotspotHandler StatsHandler(Es1MenuContext context) => (render, rect, _) => DrawStats(context, render, rect);

	/// <summary>
	/// <c>CareerStatsHotspot_Draw</c> (<c>0001705e</c>)(<paramref name="rect"/>, id) (docs/es1/shell/career.md#the-stats),
	/// in the window's render context: the alignment 0, the rect filled and framed in <see cref="PanelColor"/>; the ten
	/// <c>eng\watext.bin</c> labels in <c>cstats_2</c>; the values in <c>cstats_1</c>, each <c>sprintf</c>'d with
	/// <c>%d</c>, the salvage with <c>%ld KG</c> (<c>0007ed3b</c>). The career's name, the position, the salvage and the
	/// kills are read from the roster's record its <see cref="Es1CareerRoster.Current"/> names, the missions played and the
	/// status from <c>CareerRecord_Current</c>'s, the same record in <c>Shell_Mode</c> 1. Retail reads through a null
	/// careers-log entry and a pilot entry past the four; here such a name, or such a pilot's kills, is not drawn, engine
	/// guards.
	/// </summary>
	private static void DrawStats(Es1MenuContext context, Es1ShellRenderContext render, Es1Rect rect) {
		var data = context.Data;
		var roster = context.Mission.Roster;
		var record = roster.CurrentRecord;
		var current = context.Mission.CurrentRecord;
		var labels = data.Fonts[LabelFontSlot];
		var values = data.Fonts[ValueFontSlot];
		int x0 = rect.X0, y0 = rect.Y0;

		render.Alignment = 0;
		// Raster_FillFrameRectColours (0004b8d8) on the copy Rect_CopyCtorCareerCopy makes: the rect filled under the brush
		// {0, fill}, then its edges under {4, frame}.
		var canvas = render.Canvas;
		canvas.FillRect(rect.X0, rect.Y0, rect.X1, rect.Y1, PanelColor);
		canvas.OutlineRect(rect.X0, rect.Y0, rect.X1, rect.Y1, PanelColor);

		// NAME: and the careers-log entry's name at the roster's +1.
		render.DrawString(labels, Line(data, 0xa7), x0 + 8, y0 + 9);
		if (roster.LogEntry is { } entry) {
			render.DrawString(values, Es1CareerLogEntry.Text(entry.Name), x0 + 36, y0 + 9);
		}

		// CURRENT MISSION: and the record's position + 1.
		render.DrawString(labels, Line(data, 0xa9), x0 + 8, y0 + 18);
		render.DrawString(values, Decimal(record.Position + 1), x0 + 81, y0 + 18);

		// TOTAL MISSIONS: and CareerRecord_Current's missions played; with no record, as in Shell_Mode 0 before a mission's
		// results, none is drawn, an engine guard.
		render.DrawString(labels, Line(data, 0xaa), x0 + 8, y0 + 27);
		if (current != null) {
			render.DrawString(values, Decimal(current.MissionsPlayed), x0 + 81, y0 + 27);
		}

		// SALVAGE: and the record's dword +0x129.
		render.DrawString(labels, Line(data, 0xab), x0 + 8, y0 + 36);
		render.DrawString(values, Decimal(record.Dword129) + " KG", x0 + 81, y0 + 36);

		// KILLS, then HERCS:, FLYERS: and STRUCTURES: with the player's pilot entry's words +0x17, +0x1b and +0x19.
		render.DrawString(labels, Line(data, 0xac), x0 + 8, y0 + 45);
		render.DrawString(labels, Line(data, 0xad), x0 + 46, y0 + 45);
		var pilot = record.PlayerEntry is >= 0 and < Es1CareerRecord.PilotCount ? record.Pilots[record.PlayerEntry] : null;
		if (pilot != null) {
			render.DrawString(values, Decimal(pilot.GainSums[0]), x0 + 81, y0 + 45);
		}

		render.DrawString(labels, Line(data, 0xaf), x0 + 46, y0 + 54);
		if (pilot != null) {
			render.DrawString(values, Decimal(pilot.GainSums[2]), x0 + 81, y0 + 54);
		}

		render.DrawString(labels, Line(data, 0xae), x0 + 106, y0 + 45);
		if (pilot != null) {
			render.DrawString(values, Decimal(pilot.GainSums[1]), x0 + 161, y0 + 45);
		}

		// MISC: and the text 0 (0007ed4b), no value read.
		render.DrawString(labels, Line(data, 0xb0), x0 + 106, y0 + 54);
		render.DrawString(values, "0", x0 + 161, y0 + 54);

		// STATUS: and ACTIVE, or DECEASED with CareerRecord_Current's player's condition 0, in cstats_1.
		render.DrawString(labels, Line(data, 0xa8), x0 + 113, y0 + 9);
		bool lost = current == null || Es1CareerRoster.PlayerCondition(current) == 0;
		render.DrawString(values, Line(data, lost ? DeceasedLine : ActiveLine), x0 + 147, y0 + 9);
	}

	/// <summary>
	/// <c>Text_GetString</c>(<c>0009afa8</c>, <paramref name="number"/>), a line of <c>eng\watext.bin</c>; none, so nothing
	/// drawn, past the table or without it, an engine guard for its missing bounds test.
	/// </summary>
	private static string? Line(Es1ShellData data, int number) => data.Text is { } text && number < text.Length ? text[number] : null;

	/// <summary><c>sprintf</c>'s <c>%d</c> and <c>%ld</c>: the signed decimal.</summary>
	private static string Decimal(int value) => value.ToString(CultureInfo.InvariantCulture);
}
