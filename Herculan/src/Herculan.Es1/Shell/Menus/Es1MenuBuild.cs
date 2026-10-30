using System.Text;
using HercWorks.Core.Data.File;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// <c>Menu_BuildFromDlg</c> (<c>0003a1f7</c>) and the lists it gives its list boxes
/// (docs/es1/formats/dlg-menu-dialogs.md#the-kinds): a menu's window and controls from <c>dlg\menu&lt;n&gt;.dlg</c>, their
/// text from <c>eng\menu&lt;n&gt;.bin</c>.
/// </summary>
public static class Es1MenuBuild {
	/// <summary>The record kinds the build handles.</summary>
	private const byte WindowKind = 0, LeftLabelKind = 1, Kind2 = 2, ButtonKind = 3, RightLabelKind = 4, CenteredLabelKind = 5,
		HotspotKind = 14, ListKind = 15, Kind16 = 16, Kind17 = 17, Kind18 = 18;

	/// <summary>The id of kinds 0, 1, 4 and 5 that builds nothing, the first of the five at <c>0008196c</c>.</summary>
	private const short SkippedId = 1211;

	/// <summary>The other four ids at <c>0008196c</c>, whose text comes from the current career record.</summary>
	private static readonly short[] CareerTextIds = [5556, 5557, 5558, 1223];

	/// <summary>The options menu and its RETURN button, whose text <c>Shell_ReturnScreen</c> picks (docs/es1/shell/main-menu.md#the-return-screen).</summary>
	private const short OptionsMenu = 1300, ReturnButton = 100;

	/// <summary>The menu whose kind-1 text holding <c>#</c> goes to <c>0002a1aa</c> first.</summary>
	private const short ScrapMenu = 4444;

	/// <summary>
	/// The 17 kind-14 ids at <c>00081b08</c>, each of whose cases at <c>00081b4c</c> adds a hotspot with
	/// <c>DUIWindow_AddHotspot</c>, the handler by the id: 1612–1614 <c>00017a19</c>, 1619–1622 <c>00017b4a</c>,
	/// 1615–1618 <c>00017d9f</c>, 1231 <c>0001705e</c>, 1522 <c>000175d7</c>, 1502 <c>0002bbaf</c> (with <c>a</c> the rect
	/// <c>000a02d4</c>), 2005 <c>0002845c</c>, 1803 <c>0002d5bf</c> and 1811 <c>0002dacf</c>. Any other id builds nothing.
	/// </summary>
	private static readonly short[] HotspotIds = [
		1612, 1613, 1614, 1619, 1620, 1621, 1622, 1615, 1616, 1617, 1618, 1231, 1522, 1502, 2005, 1803, 1811,
	];

	/// <summary>The 23 kind-15 ids at <c>00081a00</c> that set their list's selection by their own code.</summary>
	private static readonly short[] OwnSelectionLists = [
		1314, 1315, 1316, 1318, 1116, 1117, 1118, 1119, 1120, 1325, 1326, 1327, 1903, 1904, 1905, 1907, 1923, 1906, 1927,
		1202, 1922, 1808, 1223,
	];

	/// <summary>The main menu's list, which shows the current career's name.</summary>
	public const short CareerNameList = 1002;

	/// <summary>
	/// The 19 ids that <c>Menu_ListLines</c> gives lines of their own and that are not ported, of the 28 at
	/// <c>000812f4</c>: all but 1002, 1202, 1223 and the single-mission lists of <see cref="WaTextLists"/>.
	/// </summary>
	private static readonly short[] OwnLinesLists = [
		1921, 1924, 1925, 1926, 1907, 1906, 1927, 1923, 1314, 1315, 1316, 1318, 1331, 1904, 1903, 1905, 1922, 1808, 1809,
	];

	/// <summary>
	/// The ids of the 28 at <c>000812f4</c> whose cases give <see cref="LinesFromList"/> of a word list
	/// (docs/es1/shell/single-mission.md): 1101, the SINGLE MISSION menu's mission types (<c>000376f5</c>, the list at
	/// <c>00081264</c>), and 1116 to 1120, the MISSION PARAMS menu's values (<c>0003765c</c>, <c>0003766d</c>,
	/// <c>000376a0</c>, <c>0003767e</c>, <c>0003768f</c>, the lists at <c>000811c4</c>, <c>000811cc</c>, <c>00081244</c>,
	/// <c>000811d4</c> and <c>000811dc</c>).
	/// </summary>
	private static readonly Dictionary<short, short[]> WaTextLists = new() {
		[Es1MissionMenu.KindList] = [0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, -1],
		[1116] = [8, 9, -1],
		[1117] = [10, 11, -1],
		[1118] = [23, 24, 25, 26, -1],
		[1119] = [12, 4, -1],
		[1120] = [13, 14, 15, -1],
	};

	/// <summary>
	/// The MISSION PARAMS menu's lists, 1116 to 1120, of the 23 at <c>00081a00</c>: their cases (<c>0003a9c7</c>–<c>0003aa1c</c>)
	/// select <see cref="Es1ShellData.MissionParams"/>' word of the same position.
	/// </summary>
	private const short FirstParamsList = 1116, LastParamsList = 1120;

	/// <summary>The list at <c>000811a4</c> that any other id's lines are numbered by: it opens with −1, so it numbers none.</summary>
	private static readonly short[] DefaultLineNumbers = [-1];

	/// <summary>
	/// <c>Menu_BuildFromDlg</c>(<paramref name="menu"/>, <paramref name="number"/>): the text table
	/// <c>eng\menu&lt;n&gt;.bin</c> (empty when it does not open), then each record of <c>dlg\menu&lt;n&gt;.dlg</c> by its
	/// kind (docs/es1/formats/dlg-menu-dialogs.md#the-kinds), its text the table's line of the record's position
	/// (docs/es1/formats/dlg-menu-dialogs.md#the-text). Retail reads a <c>.DLG</c> that does not open as whatever the stream
	/// gives; here it builds nothing, an engine guard.
	/// </summary>
	public static void BuildFromDlg(Es1Menu menu, short number) {
		var context = menu.Context;
		var content = context.Content;
		string[] table = content.Read("eng", $"MENU{number}.BIN") is { } bin ? new BinStringFileTransformer().Parse(bin)?.Values ?? [] : [];
		if (content.Read("dlg", $"MENU{number}.DLG") is not { } dlg || new Es1MenuDialogTransformer().Parse(dlg) is not { } dialog) {
			Console.Error.WriteLine($"dlg\\menu{number}.dlg is missing or unreadable: menu {number} has no window.");
			return;
		}

		// The text pointer, a local kept from record to record: menu 1300's button 100 keeps the previous one.
		string? text = null;
		for (short i = 0; i < dialog.Controls.Length; i++) {
			var record = dialog.Controls[i];
			var rect = new Es1Rect(record.Left, record.Top, record.Right, record.Bottom);
			if (record.Kind is WindowKind or LeftLabelKind or RightLabelKind or CenteredLabelKind) {
				if (record.Id == SkippedId) {
					continue;
				}

				if (record.Id == Es1SaveWarningAlert.SlotLabel) {
					// 0003a705: Roster_SaveSlotText, the overwrite warning's line of the backup SAVE would replace.
					text = context.Mission.Roster.SaveSlotText();
				} else if (Array.IndexOf(CareerTextIds, record.Id) >= 0) {
					// PLACEHOLDER: ids 5556, 5557 and 5558 take their text from the current career record (0003a5c8, 0003a60e,
					// 0003a654), TECH RELEASE's, P11's; the record is built with no text.
					text = "";
				} else {
					text = Line(table, i);
				}
			}

			switch (record.Kind) {
				case WindowKind:
					BuildWindow(menu, record, text, rect);
					break;
				case LeftLabelKind or RightLabelKind or CenteredLabelKind:
					if (number == ScrapMenu && record.Kind == LeftLabelKind && text != null && text.Contains('#', StringComparison.Ordinal)) {
						// PLACEHOLDER: 0002a1aa, which menu 4444's kind-1 text holding '#' passes through first, is unread; the
						// scrap alert is a later module's.
					}

					int mode = record.Kind switch { LeftLabelKind => 0, RightLabelKind => 1, _ => 2 };
					menu.Window?.AddLabel(mode, rect.X1 - rect.X0, rect.X0, rect.Y0, text ?? "");
					break;
				case Kind2:
					// MTEditBox_Ctor (0001f442)(0, id, rect), the name box (docs/es1/shell/career.md#the-name-box).
					menu.Window?.AddControl(new Es1MTEditBox(context.Data, context.Globals, record.Id, rect));
					break;
				case ButtonKind:
					text = BuildButton(menu, number, record, table, i, text, rect);
					break;
				case HotspotKind:
					if (Array.IndexOf(HotspotIds, record.Id) >= 0) {
						// PLACEHOLDER: 1502's a, the rect 000a02d4, is a later module's; no hotspot keeps one until it lands.
						var handler = record.Id switch {
							Es1DebriefingMenu.ResultsPanel => Es1DebriefingMenu.ResultsHandler(context),
							Es1CareerStatsAlert.StatsPanel => Es1CareerStatsAlert.StatsHandler(context),
							_ => HotspotPlaceholder,
						};
						menu.Window?.AddHotspot(null, record.Id, handler, rect);
					}

					break;
				case ListKind:
					BuildList(menu, record, rect);
					break;
				case Kind16:
					// PLACEHOLDER: 0001eaef(0, 0x100, 0, a word, id, 00037827(id), rect), kind 16's control, is
					// dlg-menu-dialogs.md's Open.
					break;
				case Kind17:
					// PLACEHOLDER: 0001eff6(0, 0x13, 0, 0xffff, id, 00037996(id), rect), kind 17's control, is
					// dlg-menu-dialogs.md's Open.
					break;
				case Kind18:
					if (record.Style == 1) {
						// PLACEHOLDER: 0004c83a(rect) stores the rect in the scripts' clip table 000a0a9c; the shell's script
						// interpreter is not ported.
					}

					break;
			}
		}
	}

	/// <summary>Line <paramref name="index"/> of <paramref name="table"/>; <c>Text_GetString</c> has no bounds test, and a line past the table is none here, an engine guard.</summary>
	private static string? Line(string[] table, int index) => index >= 0 && index < table.Length ? table[index] : null;

	/// <summary>
	/// Kind 0: style 0 a menu window (<c>MTWindow_Ctor</c>) with the text as its title, style 1 a plain one
	/// (<c>DUIWindow_Ctor</c>), any other style none; the rect copied to the menu's region whatever the style.
	/// </summary>
	private static void BuildWindow(Es1Menu menu, Es1MenuControl record, string? text, Es1Rect rect) {
		if (record.Style == 0) {
			menu.Window = new Es1MTWindow(menu.Context, record.Id, text, rect);
		} else if (record.Style == 1) {
			menu.Window = new Es1DUIWindow(menu.Context.Display, rect);
		}

		menu.Region = rect;
	}

	/// <summary>
	/// Kind 3: style 1 an icon button (<see cref="Es1MTBmpButton"/>) with the record's line when its key is 0 and no text
	/// otherwise; any other style a text button (<see cref="Es1MTButton"/>) with the record's line, except menu 1300's
	/// button 100, whose line <c>Shell_ReturnScreen</c> picks among the four values at <c>000819e0</c>: 1000 the record's
	/// own, 1100 the next, 1710 and 1200 the one after, and any other value none, the text left as the previous record's.
	/// Returns the text pointer as the record leaves it.
	/// </summary>
	private static string? BuildButton(Es1Menu menu, short number, Es1MenuControl record, string[] table, short index, string? text, Es1Rect rect) {
		var data = menu.Context.Data;
		if (record.Style == 1) {
			text = record.Key == 0 ? Line(table, index) : null;
			menu.Window?.AddControl(new Es1MTBmpButton(data, text, record.Id, record.Key, rect));
			return text;
		}

		if (number == OptionsMenu && record.Id == ReturnButton) {
			text = menu.Context.Screens.ReturnScreen switch {
				1000 => Line(table, index),
				1100 => Line(table, index + 1),
				1710 or 1200 => Line(table, index + 2),
				_ => text,
			};
		} else {
			text = Line(table, index);
		}

		menu.Window?.AddControl(new Es1MTButton(data, record.Id, record.Key, text, rect));
		return text;
	}

	/// <summary>
	/// Kind 15: the lines (<see cref="ListLines"/>), then a list box, <c>MTStrListBox_Ctor</c>(0, <c>0x100</c>, 0, 0,
	/// selection, style, id, lines, rect), added to the window: the selection 0, or for the 23 ids at <c>00081a00</c> the
	/// one their case sets, MISSION PARAMS' lists 1116 to 1120 their word of <see cref="Es1ShellData.MissionParams"/> and
	/// the career list 1202 <see cref="Es1ShellData.SelectedEntry"/> (<c>0003ab5f</c>) and the backup list 1223
	/// <c>Career_BackupLine</c> (<see cref="Es1CareerRoster.BackupLine"/>, <c>0003abf0</c>).
	/// </summary>
	private static void BuildList(Es1Menu menu, Es1MenuControl record, Es1Rect rect) {
		var lines = ListLines(menu.Context, record.Id);
		short selected = 0;
		if (record.Id is >= FirstParamsList and <= LastParamsList) {
			selected = menu.Context.Data.MissionParams[record.Id - FirstParamsList];
		} else if (record.Id == Es1CareerMenu.CareerList) {
			selected = menu.Context.Data.SelectedEntry;
		} else if (record.Id == Es1CareerRecordsMenu.BackupList) {
			// 0003abf0.
			selected = menu.Context.Mission.Roster.BackupLine;
		} else if (Array.IndexOf(OwnSelectionLists, record.Id) >= 0) {
			// PLACEHOLDER: the other 16 ids at 00081a00 build their list with a selection, and 1903 an alignment, set by their
			// own code (dlg-menu-dialogs.md's Open); their menus are later modules'.
			return;
		}

		menu.Window?.AddControl(new Es1MTStrListBox(menu.Context.Data, 0x100, 0, 0, selected, record.Style, record.Id, lines, rect));
	}

	/// <summary>
	/// <c>Menu_ListLines</c> (<c>0003750d</c>)(<paramref name="id"/>): for 1002, the main menu's list,
	/// <c>CareerLog_CurrentNameArray</c> (<c>000182f8</c>) of the careers log (docs/es1/shell/main-menu.md#the-list); for
	/// 1202, the career menu's list, <c>CareerLog_NameLines</c> (<see cref="Es1ShellData.CareerNameLines"/>); for 1223,
	/// the backup list, <c>Roster_BackupListLines</c> (<see cref="Es1CareerRoster.BackupListLines"/>); for the
	/// single-mission lists, <see cref="LinesFromList"/> of their word list (<see cref="WaTextLists"/>); for any id outside
	/// the 28 at <c>000812f4</c>, <see cref="LinesFromList"/> of the list at <c>000811a4</c>, which numbers none.
	/// </summary>
	public static string?[] ListLines(Es1MenuContext context, short id) {
		if (id == CareerNameList) {
			return CurrentCareerName(context.Data.CareerLog);
		}

		if (id == Es1CareerMenu.CareerList) {
			return context.Data.CareerNameLines();
		}

		if (id == Es1CareerRecordsMenu.BackupList) {
			return context.Mission.Roster.BackupListLines();
		}

		if (WaTextLists.TryGetValue(id, out var numbers)) {
			return LinesFromList(context.Data.Text, numbers);
		}

		if (Array.IndexOf(OwnLinesLists, id) >= 0) {
			// PLACEHOLDER: the 19 other ids at 000812f4 give lines of their own (the cases at 00081364), unread; their menus are
			// later modules', and the list is empty until they land.
			return [null];
		}

		return LinesFromList(context.Data.Text, DefaultLineNumbers);
	}

	/// <summary>
	/// <c>CareerLog_CurrentNameArray</c> (<c>000182f8</c>)(<paramref name="log"/>): with the current entry −1, an array
	/// holding only its end; otherwise the current entry's name and the end. With no log, as <c>CareerLog_Load</c> leaves a
	/// file that does not open, the current entry is −1. A current entry past the entries reads past them in retail; here
	/// it gives no name, an engine guard.
	/// </summary>
	public static string?[] CurrentCareerName(HercWorks.Core.Data.File.Dat.Shell.Es1CareerLog? log) {
		if (log == null || log.Current < 0 || log.Current >= log.Entries.Length) {
			return [null];
		}

		byte[] name = log.Entries[log.Current].Name;
		int end = Array.IndexOf(name, (byte)0);
		return [Encoding.Latin1.GetString(name, 0, end < 0 ? name.Length : end), null];
	}

	/// <summary>
	/// <c>WaText_LinesFromList</c> (<c>000373fa</c>)(<paramref name="numbers"/>): the lines of <c>eng\watext.bin</c> that
	/// the words before the first negative one number, then the end. A number past the table gives an empty line, an engine
	/// guard for <c>Text_GetString</c>'s missing bounds test.
	/// </summary>
	public static string?[] LinesFromList(string[]? text, IReadOnlyList<short> numbers) {
		var lines = new List<string?>();
		foreach (short number in numbers) {
			if (number < 0) {
				break;
			}

			lines.Add(text is { } table && number < table.Length ? table[number] : "");
		}

		lines.Add(null);
		return [.. lines];
	}

	/// <summary>
	/// The handler every hotspot but 1522, the debriefing's results panel (<see cref="Es1DebriefingMenu.ResultsHandler"/>),
	/// and 1231, the career's stats (<see cref="Es1CareerStatsAlert.StatsHandler"/>), is given in place of its own
	/// (<see cref="HotspotIds"/>): it draws nothing. 00017a19 draws the current career record's pilot portraits and their
	/// marks, and the other handlers are unread; all are later modules'.
	/// </summary>
	private static void HotspotPlaceholder(Es1ShellRenderContext context, Es1Rect rect, short id) {
		// PLACEHOLDER: the hotspot handlers (00017a19 and the six other handlers above, 000175d7 and 0001705e aside), the later
		// modules'; the area draws nothing.
	}
}
