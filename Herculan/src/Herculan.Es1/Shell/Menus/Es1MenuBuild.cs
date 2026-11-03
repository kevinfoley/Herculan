using System.Globalization;
using System.Text;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Shell;
using Herculan.Es1.Shell.Hangar;
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

	/// <summary>The maximum the build gives a kind-17 control, <c>0x13</c>.</summary>
	private const short AnimationMax = 0x13;

	/// <summary>The id of kinds 0, 1, 4 and 5 that builds nothing, the first of the five at <c>0008196c</c>.</summary>
	private const short SkippedId = 1211;

	/// <summary>The other four ids at <c>0008196c</c>, whose text comes from the current career record.</summary>
	private static readonly short[] CareerTextIds = [ReleaseLabel, ReleaseArticleLabel, ReleaseNameLabel, 1223];

	/// <summary>The options menu and its RETURN button, whose text <c>Shell_ReturnScreen</c> picks (docs/es1/shell/main-menu.md#the-return-screen).</summary>
	private const short OptionsMenu = 1300, ReturnButton = 100;

	/// <summary>The menu whose kind-1 text holding <c>#</c> goes to <c>Hangar_FormatScrapValue</c> (<see cref="Es1ScrapAlert.FormatValue"/>) first.</summary>
	private const short ScrapMenu = Es1ScrapAlert.Screen;

	/// <summary>
	/// The 17 kind-14 ids at <c>00081b08</c>, each of whose cases at <c>00081b4c</c> adds a hotspot with
	/// <c>DUIWindow_AddHotspot</c>, the handler by the id: 1612–1614 <c>00017a19</c>, 1619–1622 <c>00017b4a</c>,
	/// 1615–1618 <c>00017d9f</c>, 1231 <c>0001705e</c>, 1522 <c>000175d7</c>, 1502 <c>0002bbaf</c> (with <c>a</c> the rect
	/// <c>000a02d4</c>), 2005 <c>0002845c</c>, 1803 <c>0002d5bf</c> and 1811 <c>0002dacf</c>. Any other id builds nothing.
	/// </summary>
	private static readonly short[] HotspotIds = [
		1612, 1613, 1614, 1619, 1620, 1621, 1622, 1615, 1616, 1617, 1618, 1231, 1522, 1502, 2005, 1803, 1811,
	];

	/// <summary>The main menu's list, which shows the current career's name.</summary>
	public const short CareerNameList = 1002;

	/// <summary>SYSTEM REPAIR's lists: the items, the status, whose lines are <see cref="Es1RepairMenu.LevelLines"/> as CONDITION's are, and the number lists of the cost of one level up and the salvage allocated in the window.</summary>
	private const short ItemList = Es1SystemRepairMenu.ItemList, StatusList = Es1SystemRepairMenu.StatusList,
		StepCostList = Es1SystemRepairMenu.StepCostList, AllocatedList = Es1SystemRepairMenu.AllocatedList;

	/// <summary>The lines of <c>eng\watext.bin</c> <see cref="NumberLines"/> prints into, each "0123456789" in the retail file.</summary>
	private const short SalvageNumberLine = 0xb9, CostNumberLine = 0xba, AllocatedNumberLine = 0xbb;

	/// <summary>
	/// The ids of the 28 at <c>000812f4</c> whose cases give <see cref="LinesFromList"/> of a word list: 1101, the SINGLE
	/// MISSION menu's mission types (<c>000376f5</c>, the list at <c>00081264</c>), and 1116 to 1120, the MISSION PARAMS
	/// menu's values (<c>0003765c</c>, <c>0003766d</c>, <c>000376a0</c>, <c>0003767e</c>, <c>0003768f</c>, the lists at
	/// <c>000811c4</c>, <c>000811cc</c>, <c>00081244</c>, <c>000811d4</c> and <c>000811dc</c>)
	/// (docs/es1/shell/single-mission.md); 1331, the battle-set picker's list (<c>000376e4</c>, the list at
	/// <c>000811e4</c>), the one line <c>0x13</c>, which names the retail battle set; and PREFERENCES' boxes
	/// (docs/es1/shell/options.md#opening-it), 1314 and 1315 (<c>000376b1</c>) <see cref="Es1PrefsMenu.OffOnLines"/>,
	/// 1316 (<c>000376c2</c>) <see cref="Es1PrefsMenu.DetailLines"/> and 1318 (<c>000376d3</c>)
	/// <see cref="Es1PrefsMenu.SpeechLines"/>.
	/// </summary>
	private static readonly Dictionary<short, short[]> WaTextLists = new() {
		[Es1PrefsMenu.MusicList] = Es1PrefsMenu.OffOnLines,
		[Es1PrefsMenu.SoundList] = Es1PrefsMenu.OffOnLines,
		[Es1PrefsMenu.DetailList] = Es1PrefsMenu.DetailLines,
		[Es1PrefsMenu.SpeechList] = Es1PrefsMenu.SpeechLines,
		[Es1MissionMenu.KindList] = [0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x29, -1],
		[1116] = [8, 9, -1],
		[1117] = [10, 11, -1],
		[1118] = [23, 24, 25, 26, -1],
		[1119] = [12, 4, -1],
		[1120] = [13, 14, 15, -1],
		[Es1BattlesetAlert.BattleSetList] = [0x13, -1],
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
					text = TechReleaseText(context, table, record.Id);
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
						text = Es1ScrapAlert.FormatValue(text, context.Mission.Hangar, context.Data.RepairCosts);
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
						var handler = record.Id switch {
							Es1DebriefingMenu.ResultsPanel => Es1DebriefingMenu.ResultsHandler(context),
							Es1CareerStatsAlert.StatsPanel => Es1CareerStatsAlert.StatsHandler(context),
							Es1ArmingMenu.WeaponPanel => Es1ArmingMenu.WeaponInfoHandler(context),
							Es1ArmingMenu.MissilePanel => Es1ArmingMenu.MissileInfoHandler(context),
							Es1FabricationMenu.Panel => Es1FabricationMenu.PanelHandler(context),
							>= Es1CrewAssignMenu.FirstPortrait and <= Es1CrewAssignMenu.LastPortrait => Es1CrewAssignMenu.PortraitHandler(context),
							>= Es1CrewAssignMenu.FirstLines and <= Es1CrewAssignMenu.LastLines => Es1CrewAssignMenu.LinesHandler(context),
							>= Es1CrewAssignMenu.FirstTile and <= Es1CrewAssignMenu.LastTile => Es1CrewAssignMenu.BayNumberHandler(context),
							_ => MapAreaNoOp,
						};
						// 1502's a is the map's rect 000a02d4 (Es1ShellMap.Area), which holds its own: no point hits it.
						Es1Rect? excluded = record.Id == MapArea ? Map.Es1ShellMap.Area : null;
						menu.Window?.AddHotspot(excluded, record.Id, handler, rect);
					}

					break;
				case ListKind:
					BuildList(menu, number, record, rect);
					break;
				case Kind16:
					BuildPictureTile(menu, record, rect);
					break;
				case Kind17:
					// 0001eff6(0, 0x13, 0, 0xffff, id, Menu_OpenControlTtms(id), rect), added to the window.
					menu.Window?.AddControl(new Es1AnimationControl(context.Data, () => context.Screens.Scripts, AnimationMax, 0, -1,
						record.Id, Es1AnimationControl.OpenScripts(context, record.Id), rect));
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

	/// <summary>The TECH RELEASE labels' ids among <see cref="CareerTextIds"/>: the opening line, the line with its article, and the item's name.</summary>
	private const short ReleaseLabel = 5556, ReleaseArticleLabel = 5557, ReleaseNameLabel = 5558;

	/// <summary>The first lines of <c>eng\watext.bin</c> the item's name is read from: the chassis' names for kind 0, the weapons' for any other.</summary>
	private const short ChassisNames = 0x1b, WeaponNames = 0xd5;

	/// <summary>
	/// The text of TECH RELEASE's labels (docs/es1/shell/menus.md#opening-a-menu--menu_open-0003969e), from the equipment
	/// state's item last made available on the roster's record its word <c>+7</c> names: 5556 (<c>0003a5c8</c>) the menu's
	/// line (the kind <c>+0xa8</c>) + 2, 5557 (<c>0003a60e</c>) the menu's line (the kind) + 4, and 5558
	/// (<c>0003a654</c>) the line (the index <c>+0xaa</c>) + <see cref="ChassisNames"/> of <c>eng\watext.bin</c> for kind
	/// 0 and + <see cref="WeaponNames"/> otherwise. Each sum is a 16-bit add.
	/// </summary>
	private static string? TechReleaseText(Es1MenuContext context, string[] table, short id) {
		var equipment = context.Mission.Roster.CurrentRecord.Equipment;
		return id switch {
			ReleaseLabel => Line(table, (short)(equipment.LastKind + 2)),
			ReleaseArticleLabel => Line(table, (short)(equipment.LastKind + 4)),
			ReleaseNameLabel => Line(context.Data.Text ?? [], (short)(equipment.LastIndex + (equipment.LastKind == 0 ? ChassisNames : WeaponNames))),
			_ => null,
		};
	}

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

	/// <summary>The alignment HERC's and PILOT's readouts take outside ARMING, 2, centered.</summary>
	private const int CenteredReadout = 2;

	/// <summary>
	/// Kind 15: the lines (<see cref="ListLines"/>), then a list box, <c>MTStrListBox_Ctor</c>(0, <c>0x100</c>, 0,
	/// alignment, selection, style, id, lines, rect), added to the window: the alignment 0 and the selection 0, or for the
	/// 23 ids at <c>00081a00</c> what their case sets: PREFERENCES' boxes their setting, 1314 (<c>0003a973</c>)
	/// <see cref="Es1ShellData.MusicOption"/>, 1315 (<c>0003a986</c>) <see cref="Es1ShellData.SoundOption"/>, 1316
	/// (<c>0003a999</c>) <see cref="Es1ShellData.DetailOption"/> with the alignment 2, and 1318 (<c>0003a9b5</c>)
	/// <see cref="Es1ShellData.RadioOption"/>; MISSION PARAMS' lists 1116 to 1120 their word of
	/// <see cref="Es1ShellData.MissionParams"/>; the career list 1202 <see cref="Es1ShellData.SelectedEntry"/>
	/// (<c>0003ab5f</c>); the backup list 1223 <c>Career_BackupLine</c> (<see cref="Es1CareerRoster.BackupLine"/>,
	/// <c>0003abf0</c>); HERC's readout 1903 (<c>0003aa57</c>) the chassis of <c>Hangar_SelectedSlot</c>'s HERC, −1 for
	/// none, and PILOT's 1904 (<c>0003aac4</c>) <see cref="Es1ArmingMenu.PilotLine"/>(<c>CareerRecord_Current</c>, its
	/// current pilot <c>+0x12</c>), each with the alignment 2 unless <paramref name="number"/> is 1800 or 1820; REPAIR's
	/// CONDITION 1905 (<c>0003ab09</c>) <see cref="Es1RepairCosts.LevelForCondition"/> of the selected HERC's overall
	/// condition, of 0 for none, and the number lists 1907, 1923, 1906 and 1927 (<c>0003ab50</c>, where 1905's case goes
	/// on) the selection 0, each with the alignment 2; SYSTEM REPAIR's STATUS 1922 (<c>0003ab71</c>)
	/// <see cref="Es1RepairCosts.LevelForCondition"/> of internal 0's condition (<see cref="Es1HercDiagrams.GetStatus"/>),
	/// of 0 for no HERC, with the alignment 2; and the weapon list 1808 (<c>0003abdd</c>) its counts written
	/// (<see cref="Es1ArmingGlobals.FormatCounts"/>). The other three of the 23, 1325 to 1327, are in none of the 39
	/// <c>.DLG</c> files of <c>SHELL0.VOL</c> (docs/es1/shell/options.md#opening-it), so their cases are not ported and
	/// such a list takes the selection 0 here.
	/// </summary>
	private static void BuildList(Es1Menu menu, short number, Es1MenuControl record, Es1Rect rect) {
		var context = menu.Context;
		var lines = ListLines(context, record.Id);
		short selected = 0;
		int alignment = 0;
		bool arming = number is Es1ArmingMenu.Screen or Es1ArmingMenu.LowMemoryScreen;
		if (record.Id == Es1PrefsMenu.MusicList) {
			selected = context.Data.MusicOption;
		} else if (record.Id == Es1PrefsMenu.SoundList) {
			selected = context.Data.SoundOption;
		} else if (record.Id == Es1PrefsMenu.DetailList) {
			selected = context.Data.DetailOption;
			alignment = CenteredReadout;
		} else if (record.Id == Es1PrefsMenu.SpeechList) {
			selected = context.Data.RadioOption;
		} else if (record.Id is >= FirstParamsList and <= LastParamsList) {
			selected = context.Data.MissionParams[record.Id - FirstParamsList];
		} else if (record.Id == Es1CareerMenu.CareerList) {
			selected = context.Data.SelectedEntry;
		} else if (record.Id == Es1CareerRecordsMenu.BackupList) {
			// 0003abf0.
			selected = context.Mission.Roster.BackupLine;
		} else if (record.Id == Es1ArmingMenu.ChassisList) {
			var hangar = context.Mission.Hangar;
			selected = hangar?.HercIn(hangar.SelectedSlot)?.Chassis ?? -1;
			alignment = arming ? 0 : CenteredReadout;
		} else if (record.Id == Es1ArmingMenu.PilotList) {
			var current = context.Mission.CurrentRecord;
			selected = Es1ArmingMenu.PilotLine(current, current?.CurrentPilot ?? -1);
			alignment = arming ? 0 : CenteredReadout;
		} else if (record.Id == Es1RepairMenu.LevelList) {
			var hangar = context.Mission.Hangar;
			selected = Es1RepairCosts.LevelForCondition(hangar?.HercIn(hangar.SelectedSlot)?.OverallCondition() ?? 0);
			alignment = CenteredReadout;
		} else if (record.Id is Es1RepairMenu.SalvageList or Es1RepairMenu.CostList or AllocatedList or StepCostList) {
			alignment = CenteredReadout;
		} else if (record.Id == StatusList) {
			var hangar = context.Mission.Hangar;
			var herc = hangar?.HercIn(hangar.SelectedSlot);
			selected = Es1RepairCosts.LevelForCondition(herc != null ? Es1HercDiagrams.GetStatus(herc, 0, Es1HercDiagrams.Internal) : (short)0);
			alignment = CenteredReadout;
		} else if (record.Id == Es1ArmingMenu.WeaponList) {
			context.Arming.FormatCounts(context, lines);
		}

		menu.Window?.AddControl(new Es1MTStrListBox(context.Data, 0x100, 0, alignment, selected, record.Style, record.Id, lines, rect));
	}

	/// <summary>The kind-16 ids at <c>00081ab8</c> whose word is set by their own code, the ported ones: CREW's insignia and HERC tiles.</summary>
	private const short InsigniaTile = Es1CrewAssignMenu.InsigniaTile, FirstHercTile = Es1CrewAssignMenu.FirstTile,
		LastHercTile = Es1CrewAssignMenu.LastTile;

	/// <summary>
	/// Kind 16 (<c>0003ac49</c>): the bank <c>00037827</c>(id) (<see cref="Es1MenuBanks.ForControl"/>), then a picture tile,
	/// <c>0001eaef</c>(0, <c>0x100</c>, 0, word, id, bank, rect) (<see cref="Es1MTGfxListBox"/>), added to the window. The
	/// word is −1, or for the ten ids at <c>00081ab8</c> (targets at <c>00081ae0</c>, in bytes Ghidra left undefined) set by
	/// their own code: 1611 (<c>0003ac98</c>) 0; 1615 to 1618 (<c>0003aca6</c>) the chassis, word <c>+0</c>, of the
	/// HERC in the slot <c>+8</c> of pilot entry id − 1615 of the roster's record its word <c>+7</c> names, read from the
	/// roster in either mode, −1 for a slot of −1 or one holding no HERC; and PREFERENCES' 1914 to 1916 (<c>0003ad45</c>,
	/// <c>0003ad65</c>, <c>0003ad87</c>) <see cref="Es1RepairPrefsAlert.TileWord"/> of <c>Career_AutoRepairMode</c>.
	/// </summary>
	private static void BuildPictureTile(Es1Menu menu, Es1MenuControl record, Es1Rect rect) {
		var context = menu.Context;
		if (!Es1MenuBanks.HasBankFor(record.Id)) {
			// The other two of the ten, 1316 (0003ac86, the word Shell_DetailOption) and 1321 (0003ac78, the word 0), take
			// banks from cases of 00037827 that are not ported (Es1MenuBanks.ForControl), since no retail .DLG has a kind-16
			// record of either; any id outside the eleven at 0008144c gets no bank. No tile is built for them here.
			return;
		}

		var bank = context.Banks.ForControl(record.Id);
		short word = -1;
		if (record.Id == InsigniaTile) {
			word = 0;
		} else if (record.Id is >= FirstHercTile and <= LastHercTile) {
			short slot = context.Mission.Roster.CurrentRecord.Pilots[record.Id - FirstHercTile].Slot;
			word = context.Mission.Hangar?.HercIn(slot)?.Chassis ?? -1;
		} else if (record.Id is >= Es1RepairPrefsAlert.FirstTile and <= Es1RepairPrefsAlert.LastTile) {
			word = Es1RepairPrefsAlert.TileWord(record.Id, context.Mission.Roster.AutoRepairMode);
		}

		menu.Window?.AddControl(new Es1MTGfxListBox(context.Data, context.Banks, 0x100, 0, word, record.Id, bank, rect));
	}

	/// <summary>
	/// <c>Menu_ListLines</c> (<c>0003750d</c>)(<paramref name="id"/>): for 1002, the main menu's list,
	/// <c>CareerLog_CurrentNameArray</c> (<c>000182f8</c>) of the careers log (docs/es1/shell/main-menu.md#the-list); for
	/// 1202, the career menu's list, <c>CareerLog_NameLines</c> (<see cref="Es1ShellData.CareerNameLines"/>); for 1223,
	/// the backup list, <c>Roster_BackupListLines</c> (<see cref="Es1CareerRoster.BackupListLines"/>); for the
	/// single-mission lists and the battle-set list, <see cref="LinesFromList"/> of their word list (<see cref="WaTextLists"/>);
	/// for ARMING's, 1903 (<c>00037760</c>) the chassis' names (<see cref="Es1ArmingGlobals.ChassisLines"/>), 1904
	/// (<c>00037728</c>) PLAYER, the squadmates' names and UNASSIGNED (<see cref="Es1ArmingGlobals.PilotLines"/>), the
	/// first line replaced in <c>Shell_Mode</c> 1 by the career's name, that of the careers-log entry the roster's dword
	/// <c>+1</c> points at, 1808 (<c>00037782</c>) <see cref="Es1ArmingGlobals.BuildWeaponList"/>(−1, −1) and 1809
	/// (<c>0003779a</c>) the missile types (<see cref="Es1ArmingGlobals.MissileTypeLines"/>); for REPAIR's, 1905 and
	/// SYSTEM REPAIR's 1922 (<c>00037771</c>) the five levels (<see cref="Es1RepairMenu.LevelLines"/>), 1907
	/// (<c>0003753d</c>) <see cref="NumberLine"/>(<c>Career_Salvage</c>, <c>0xb9</c>) and 1906 (<c>00037554</c>)
	/// <see cref="NumberLine"/>(<see cref="Es1RepairCosts.HercCost"/>(100, the selected HERC), 0 for none, <c>0xba</c>);
	/// for SYSTEM REPAIR's, 1921 and 1924 to 1926 (<c>0003752e</c>) <see cref="Es1SystemRepairMenu.SelectedComponentLines"/>(the
	/// id), 1927 (<c>0003759a</c>) <see cref="NumberLine"/>(<see cref="Es1RepairCosts.UnitValueCost"/>(up, internal 0's
	/// condition, 0 for no HERC, 0, <c>0x784</c>, the chassis, −1 for none), <c>0xba</c>) and 1923 (<c>00037649</c>)
	/// <see cref="NumberLine"/>(0, <c>0xbb</c>); for any id outside the 28 at <c>000812f4</c>, <see cref="LinesFromList"/> of the list at <c>000811a4</c>, which
	/// numbers none.
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

		switch (id) {
			case Es1ArmingMenu.ChassisList:
				return LinesFromList(context.Data.Text, Es1ArmingGlobals.ChassisLines);
			case Es1ArmingMenu.PilotList:
				var pilots = LinesFromList(context.Data.Text, Es1ArmingGlobals.PilotLines);
				if (context.Mission.Mode == 1) {
					// A roster with no log entry leaves a null pointer there, which ends the lines at once.
					pilots[0] = context.Mission.Roster.LogEntry is { } entry ? Es1CareerLogEntry.Text(entry.Name) : null;
				}

				return pilots;
			case Es1ArmingMenu.WeaponList:
				return LinesFromList(context.Data.Text, context.Arming.BuildWeaponList(context, -1, -1));
			case Es1ArmingMenu.MissileTypeList:
				return LinesFromList(context.Data.Text, Es1ArmingGlobals.MissileTypeLines);
			case Es1RepairMenu.LevelList:
			case StatusList:
				return LinesFromList(context.Data.Text, Es1RepairMenu.LevelLines);
			case Es1RepairMenu.SalvageList:
				return NumberLine(context.Data, context.Mission.Roster.Salvage, SalvageNumberLine);
			case Es1RepairMenu.CostList:
				var hangar = context.Mission.Hangar;
				var herc = hangar?.HercIn(hangar.SelectedSlot);
				return NumberLine(context.Data, herc != null ? context.Data.RepairCosts.HercCost(100, herc) : 0, CostNumberLine);
			case ItemList:
			case Es1SystemRepairMenu.InternalButton:
			case Es1SystemRepairMenu.ExternalButton:
			case Es1SystemRepairMenu.WeaponsButton:
				return Es1SystemRepairMenu.SelectedComponentLines(context, id);
			case StepCostList:
				var selectedHangar = context.Mission.Hangar;
				var selected = selectedHangar?.HercIn(selectedHangar.SelectedSlot);
				short condition = selected != null ? Es1HercDiagrams.GetStatus(selected, 0, Es1HercDiagrams.Internal) : (short)0;
				int cost = context.Data.RepairCosts.UnitValueCost(true, condition, 0, Es1HercDiagrams.Internal, selected?.Chassis ?? -1);
				return NumberLine(context.Data, cost, CostNumberLine);
			case AllocatedList:
				return NumberLine(context.Data, 0, AllocatedNumberLine);
		}

		return LinesFromList(context.Data.Text, DefaultLineNumbers);
	}

	/// <summary>
	/// <c>Menu_NumberLines</c> (<c>000374af</c>)(<paramref name="value"/>, <paramref name="id"/>): <see cref="NumberLine"/>
	/// of the value on the line the id gives among the four at <c>000812d4</c> (targets at <c>000812e4</c>): 1907
	/// <c>0xb9</c>, 1906 and 1927 <c>0xba</c>, 1923 <c>0xbb</c>. For any other id retail returns the id itself, which the
	/// <c>REPNE SCASD</c> left in <c>EAX</c>; here an array holding only its end, an engine guard.
	/// </summary>
	public static string?[] NumberLines(Es1ShellData data, int value, short id) => id switch {
		Es1RepairMenu.SalvageList => NumberLine(data, value, SalvageNumberLine),
		Es1RepairMenu.CostList or StepCostList => NumberLine(data, value, CostNumberLine),
		AllocatedList => NumberLine(data, value, AllocatedNumberLine),
		_ => [null],
	};

	/// <summary>
	/// <c>WaText_NumberLine</c> (<c>00037468</c>)(<paramref name="value"/>, <paramref name="line"/>): a new array of
	/// <c>eng\watext.bin</c> line <paramref name="line"/>'s own buffer, into which the value is printed with <c>%ld</c>,
	/// and the end. Retail's arrays of one line share its buffer, so a list still holding an earlier one shows the new
	/// value at its next draw; here the table's line takes the printed value and each array holds its own string. With no
	/// table, or a line past it, the value is printed and not stored, an engine guard.
	/// </summary>
	public static string?[] NumberLine(Es1ShellData data, int value, short line) {
		string printed = value.ToString(CultureInfo.InvariantCulture);
		if (data.Text is { } table && line >= 0 && line < table.Length) {
			table[line] = printed;
		}

		return [printed, null];
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

	/// <summary>The war map's area, whose hotspot <see cref="MapAreaNoOp"/> handles.</summary>
	private const short MapArea = 1502;

	/// <summary>
	/// <c>WarMapMenu_MapAreaNoOp</c> (<c>0002bbaf</c>), 1502's handler (docs/es1/shell/war-map.md#where-the-object-file-starts-and-ends):
	/// it draws nothing. The other handlers of <see cref="HotspotIds"/> are 1522's, the debriefing's results panel
	/// (<see cref="Es1DebriefingMenu.ResultsHandler"/>), 1231's, the career's stats
	/// (<see cref="Es1CareerStatsAlert.StatsHandler"/>), CREW's three (<see cref="Es1CrewAssignMenu.PortraitHandler"/>,
	/// <see cref="Es1CrewAssignMenu.LinesHandler"/>, <see cref="Es1CrewAssignMenu.BayNumberHandler"/>), ARMING's two
	/// (<see cref="Es1ArmingMenu.WeaponInfoHandler"/>, <see cref="Es1ArmingMenu.MissileInfoHandler"/>) and BUILD's
	/// (<see cref="Es1FabricationMenu.PanelHandler"/>).
	/// </summary>
	private static void MapAreaNoOp(Es1ShellRenderContext context, Es1Rect rect, short id) {
	}
}
