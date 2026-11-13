using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using HercWorks.Core.Data.File.Dat.Shell;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Ttm;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// CREW ASSIGNMENT's screen object, class <c>CrewAssignMenu</c> (vtable <c>00080c9e</c>, <c>0x40</c> bytes)
/// (docs/es1/shell/hangar.md#crew): screen 1610, over the service bay, four columns, one per pilot entry, each with its
/// HERC's picture tile (<see cref="Es1MTGfxListBox"/>); a column is picked, its pilot's HERC changed by stepping through the
/// hangar's HERCs, and ACCEPT keeps the changes or CANCEL drops them. Its three kinds of hotspot draw through
/// <see cref="PortraitHandler"/>, <see cref="LinesHandler"/> and <see cref="BayNumberHandler"/>.
/// </summary>
public sealed class Es1CrewAssignMenu : Es1Menu {
	/// <summary>The screen and the menu, <c>dlg\menu1610.dlg</c>.</summary>
	public const short Screen = 1610;

	/// <summary>The player's insignia tile, over column 0.</summary>
	public const short InsigniaTile = 1611;

	/// <summary>The squadmates' portrait hotspots, entries 1 to 3.</summary>
	public const short FirstPortrait = 1612, LastPortrait = 1614;

	/// <summary>The HERC tiles, and the bay-number hotspots over them, entries 0 to 3.</summary>
	public const short FirstTile = 1615, LastTile = 1618;

	/// <summary>The columns' line hotspots, entries 0 to 3.</summary>
	public const short FirstLines = 1619, LastLines = 1622;

	/// <summary>The controls of the eleven ids of the table at <c>00080cd0</c> besides the tiles.</summary>
	private const short Prev = 2003, Next = 2004, NoHerc = 1623, Cancel = Es1ServiceBayMenu.Screen, Accept = 100, Herc = 1624;

	/// <summary>The colors a tile is framed in: the picked column's, and a HERC's whose pilot a swap gave another.</summary>
	private const short PickedColor = 4, SwappedColor = 10;

	/// <summary>The group script the menu stops while it is up.</summary>
	private const int StoppedScript = 3;

	/// <summary>
	/// <c>CrewAssignMenu_Ctor</c> (<c>0002c743</c>)(0) (docs/es1/shell/hangar.md#opening-it): the menu's base, group script
	/// 3 stopped (<c>Ttm_SetMode</c>(2, its handle)), the fields below set from <c>CareerRecord_Current</c> and
	/// <c>Hangar_SelectedSlot</c>, and <c>res32\pilots.dba</c> and <c>res32\mesh.dba</c> loaded into
	/// <see cref="Es1MenuBanks.CrewPortraits"/> and <see cref="Es1MenuBanks.CrewMesh"/>. With no record or no hangar, before
	/// a load, the fields keep their defaults and the handlers do nothing, engine guards.
	/// </summary>
	public Es1CrewAssignMenu(Es1MenuContext context) : base(context) {
		SetScriptMode(Es1TtmPlayer.Hidden);
		_record = context.Mission.CurrentRecord;
		PickedTile = FirstTile;
		OpenedSlot = Hangar?.SelectedSlot ?? -1;
		LastSlot = -1;
		PickedSlot = -1;
		if (_record != null) {
			OpenedPilot = _record.CurrentPilot;
			Word3C = _record.CurrentPilot;
			for (int i = 0; i < Es1CareerRecord.PilotCount; i++) {
				OpenedSlots[i] = _record.Pilots[i].Slot;
			}
		}

		SwappedEntry = -1;
		var banks = context.Banks;
		banks.CrewPortraits = banks.Load("PILOTS.DBA");
		banks.CrewMesh = banks.Load("MESH.DBA");
	}

	/// <summary><c>+0x26</c>, the career record, <c>CareerRecord_Current</c>'s at the opening.</summary>
	private readonly Es1CareerRecord? _record;

	/// <summary><c>+0x2a</c>, the picked column's tile id, 1615 to 1618; 1615 at the opening.</summary>
	public short PickedTile { get; private set; }

	/// <summary><c>+0x2c</c>, the bay selected when CREW opened.</summary>
	public short OpenedSlot { get; }

	/// <summary><c>+0x2e</c>, the slot the last assignment gave; −1 at the opening.</summary>
	public short LastSlot { get; private set; }

	/// <summary><c>+0x30</c>, the slot the picked column's pilot held when the column was picked; −1 at the opening.</summary>
	public short PickedSlot { get; private set; }

	/// <summary><c>+0x32</c>, each entry's slot <c>+8</c> at the opening, which CANCEL puts back.</summary>
	public short[] OpenedSlots { get; } = new short[Es1CareerRecord.PilotCount];

	/// <summary><c>+0x3a</c>, the record's current pilot <c>+0x12</c> at the opening, which CANCEL selects again.</summary>
	public short OpenedPilot { get; }

	/// <summary><c>+0x3c</c>, the current pilot at the opening again; what reads it is hangar.md's Open.</summary>
	public short Word3C { get; }

	/// <summary><c>+0x3e</c>, the entry the last assignment took its HERC from, or −1.</summary>
	public short SwappedEntry { get; private set; }

	private Es1Hangar? Hangar => Context.Mission.Hangar;

	/// <summary><c>Ttm_SetMode</c>(<paramref name="mode"/>, the handle of group script 3); nothing before the bay exists, an engine guard.</summary>
	private void SetScriptMode(int mode) {
		if (Context.Screens.Bay is { } bay) {
			Context.Screens.Scripts?.SetMode(mode, bay.Handle(StoppedScript));
		}
	}

	/// <summary>
	/// <c>Menu_Open</c>'s change for 1610 (disassembly <c>0003969e</c>; docs/es1/shell/hangar.md#opening-it): tile 1615,
	/// found with <c>DUIWindow_FindEnabledControlById</c>, framed in color 4 through its slot <c>+0x14</c>, and NONE, found
	/// in any state, made inactive. A control missing is passed over, an engine guard.
	/// </summary>
	public static void SetOpeningControls(Es1DUIWindow window, Action<Es1DUIWindow, short> makeInactive) {
		Tile(window, FirstTile)?.HighlightIn(PickedColor);
		makeInactive(window, window.FindControlById(NoHerc));
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>CrewAssignMenu_OnMessage</c> (<c>0002d19e</c>) (docs/es1/shell/hangar.md#the-buttons): the
	/// target looked up among the eleven ids at <c>00080cd0</c> (cases at <c>00080cfc</c>); nothing for any other id. Of the
	/// targets only 1700, which ACCEPT's case also sets, is a screen. ACCEPT's case, which waits, is
	/// <see cref="OnMessageWaiting"/>'s.
	/// </summary>
	public override IEnumerable<Es1ShellWait> OnMessageWaiting(StrongBox<Es1MenuMessage> message) =>
		message.Value.Target == Accept ? AcceptCrew(message) : base.OnMessageWaiting(message);

	/// <summary>
	/// ACCEPT's case (<c>0002d284</c>): the target 1700; <c>CareerRecord_SaveCurrent</c>; the player's entry made the current
	/// pilot; <c>HangarBay_AssignScripts</c> (<c>000122e0</c>), whose waits are its scripts'; and the slot the screen opened
	/// with selected again.
	/// </summary>
	private IEnumerable<Es1ShellWait> AcceptCrew(StrongBox<Es1MenuMessage> message) {
		var hangar = Hangar;
		message.Value = message.Value with { Target = Es1ServiceBayMenu.Screen };
		Context.Mission.SaveCurrentRecord();
		if (hangar != null && _record != null) {
			_record.SelectPilot(_record.PlayerEntry, hangar);
		}

		if (Context.Screens.Bay is { } bay) {
			foreach (var wait in bay.AssignScripts()) {
				yield return wait;
			}
		}

		if (hangar != null) {
			hangar.SelectedSlot = OpenedSlot;
		}
	}

	/// <inheritdoc cref="OnMessageWaiting"/>
	public override void OnMessage(ref Es1MenuMessage message) {
		var hangar = Hangar;
		switch (message.Target) {
			case Prev:
				// 0002d1c4.
				UndoSwap();
				hangar?.PrevHercSlot();
				AssignSelectedSlot();
				break;
			case Next:
				// 0002d1de.
				StepNext();
				break;
			case NoHerc:
				// 0002d1f8.
				UndoSwap();
				if (hangar != null) {
					hangar.SelectedSlot = -1;
				}

				PickedSlot = -1;
				AssignSelectedSlot();
				break;
			case Cancel:
				// 0002d220: the record's current pilot selected before the slots are put back.
				if (hangar != null && _record != null) {
					hangar.SelectedSlot = OpenedSlot;
					_record.SelectPilot(OpenedPilot, hangar);
					for (int i = 0; i < Es1CareerRecord.PilotCount; i++) {
						_record.Pilots[i].Slot = OpenedSlots[i];
					}
				}

				break;
			case >= FirstTile and <= LastTile:
				// 0002d2bb: the picked column's tile steps as Next does; another picks its column.
				if (message.Target == PickedTile) {
					StepNext();
				} else {
					SelectColumn(message.Target);
				}

				break;
			case Herc:
				// 0002d2f7: the next column's tile, 1618 going round to 1615.
				short next = (short)(PickedTile + 1);
				SelectColumn(next > LastTile ? FirstTile : next);
				break;
			case InsigniaTile:
				// 0002d320: nothing.
				break;
		}
	}

	/// <summary><c>→</c>'s case, which a click on the picked column's tile also runs: the swap undone, the selection stepped forward (<c>Hangar_NextHercSlot</c>) and the slot assigned.</summary>
	private void StepNext() {
		UndoSwap();
		Hangar?.NextHercSlot();
		AssignSelectedSlot();
	}

	/// <summary>
	/// <c>CrewAssignMenu_SelectColumn</c> (<c>0002cf16</c>)(menu, <paramref name="id"/>)
	/// (docs/es1/shell/hangar.md#assigning-a-herc): for <see cref="PickedTile"/> not −1, its tile unframed (slot
	/// <c>+0x18</c>) and its bay number's and lines' hotspots (id and id + 4) marked for redrawing; with an entry in
	/// <see cref="SwappedEntry"/>, that entry's tile unframed, its bay number's hotspot marked and the entry −1; NONE, found
	/// in any state, made inactive (state 2) for tile 1615 and up (0) for any other, <i>changed</i> set; the new tile
	/// framed in color 4 (slot <c>+0x14</c>), its two hotspots marked, entry id − 1615 made the current pilot
	/// (<c>CareerRecord_SelectPilot</c>), which selects its slot, the id kept as <see cref="PickedTile"/> and
	/// <c>Hangar_SelectedSlot</c> as <see cref="PickedSlot"/>.
	/// </summary>
	private void SelectColumn(short id) {
		if (Window is not { } window || _record == null || Hangar is not { } hangar) {
			return;
		}

		if (PickedTile != -1) {
			Tile(window, PickedTile)?.Unhighlight();
			MarkHotspot(window, PickedTile);
			MarkHotspot(window, (short)(PickedTile + 4));
		}

		if (SwappedEntry != -1) {
			Tile(window, (short)(SwappedEntry + FirstTile))?.Unhighlight();
			MarkHotspot(window, (short)(SwappedEntry + FirstTile));
			SwappedEntry = -1;
		}

		short none = window.FindControlById(NoHerc);
		if (none >= 0) {
			window.Controls[none].State = id == FirstTile ? Es1DUIControl.Inactive : Es1DUIControl.Up;
			window.Controls[none].Changed = true;
		}

		Tile(window, id)?.HighlightIn(PickedColor);
		MarkHotspot(window, id);
		MarkHotspot(window, (short)(id + 4));
		_record.SelectPilot((short)(id - FirstTile), hangar);
		PickedTile = id;
		PickedSlot = hangar.SelectedSlot;
	}

	/// <summary>
	/// <c>CrewAssignMenu_AssignSelectedSlot</c> (<c>0002cc21</c>) (docs/es1/shell/hangar.md#assigning-a-herc):
	/// <c>Hangar_SelectedSlot</c> −1 when it is <see cref="LastSlot"/>; the slot kept as <see cref="LastSlot"/> and the entry
	/// <c>CareerRecord_FindPilotBySlot</c> finds holding it as <see cref="SwappedEntry"/>; when there is one, that entry given
	/// <see cref="PickedSlot"/>, its tile framed in color 10 (slot <c>+0x14</c>) and set to the chassis of
	/// <see cref="PickedSlot"/>'s HERC (slot <c>+0x24</c>, −1 for none), and its lines' and bay number's hotspots marked;
	/// then the current pilot <c>+0x12</c>'s tile set to the chassis of the selected slot's HERC, that entry given the slot,
	/// its two hotspots marked, and <c>Roster_ForEachPilotNoOp</c>. Retail writes the entry a current pilot outside the
	/// four names with no bounds test; such an entry is not written here, an engine guard.
	/// </summary>
	private void AssignSelectedSlot() {
		if (Window is not { } window || _record == null || Hangar is not { } hangar) {
			return;
		}

		if (LastSlot == hangar.SelectedSlot) {
			hangar.SelectedSlot = -1;
		}

		LastSlot = hangar.SelectedSlot;
		SwappedEntry = _record.FindPilotBySlot(hangar.SelectedSlot);
		if (SwappedEntry != -1) {
			_record.Pilots[SwappedEntry].Slot = PickedSlot;
			short tile = (short)(SwappedEntry + FirstTile);
			Tile(window, tile)?.HighlightIn(SwappedColor);
			Tile(window, tile)?.SetFrame(Chassis(hangar, PickedSlot));
			MarkHotspot(window, (short)(SwappedEntry + FirstLines));
			MarkHotspot(window, tile);
		}

		short current = _record.CurrentPilot;
		Tile(window, (short)(current + FirstTile))?.SetFrame(Chassis(hangar, hangar.SelectedSlot));
		if (current is >= 0 and < Es1CareerRecord.PilotCount) {
			_record.Pilots[current].Slot = hangar.SelectedSlot;
		}

		MarkHotspot(window, (short)(current + FirstLines));
		MarkHotspot(window, (short)(current + FirstTile));
		Es1CareerRoster.ForEachPilotNoOp();
	}

	/// <summary>
	/// <c>CrewAssignMenu_UndoSwap</c> (<c>0002ca5a</c>) (docs/es1/shell/hangar.md#assigning-a-herc): with an entry in
	/// <see cref="SwappedEntry"/>, its tile unframed (slot <c>+0x18</c>); and when the picked column is the current pilot,
	/// that entry given back the slot the current pilot now holds, its tile set to that HERC's chassis (slot <c>+0x24</c>,
	/// −1 for none) and its lines' and bay number's hotspots marked. <see cref="SwappedEntry"/> is left for
	/// <see cref="AssignSelectedSlot"/> to set. A current pilot outside the four reads slot −1 here, an engine guard.
	/// </summary>
	private void UndoSwap() {
		if (SwappedEntry == -1 || Window is not { } window || _record == null || Hangar is not { } hangar) {
			return;
		}

		short tile = (short)(SwappedEntry + FirstTile);
		Tile(window, tile)?.Unhighlight();
		if (PickedTile - FirstTile != _record.CurrentPilot) {
			return;
		}

		short current = _record.CurrentPilot;
		short slot = current is >= 0 and < Es1CareerRecord.PilotCount ? _record.Pilots[current].Slot : (short)-1;
		_record.Pilots[SwappedEntry].Slot = slot;
		Tile(window, tile)?.SetFrame(Chassis(hangar, slot));
		MarkHotspot(window, (short)(SwappedEntry + FirstLines));
		MarkHotspot(window, tile);
	}

	/// <summary>The chassis of the HERC in <paramref name="slot"/>, its word <c>+0</c>; −1 for −1 or an empty slot.</summary>
	private static short Chassis(Es1Hangar hangar, short slot) => hangar.HercIn(slot)?.Chassis ?? -1;

	/// <summary>
	/// The tile <c>DUIWindow_FindEnabledControlById</c> finds for <paramref name="id"/>, whose slots the functions call.
	/// Retail calls the slot of whatever control the index −1 reads; here a tile not found is passed over, an engine guard.
	/// </summary>
	private static Es1MTGfxListBox? Tile(Es1DUIWindow window, short id) =>
		window.FindEnabledControlById(id) is var index and >= 0 && window.Controls[index] is Es1MTGfxListBox tile ? tile : null;

	/// <summary>
	/// The redraw byte <c>+0</c> of the hotspot <c>DUIWindow_FindHotspotById</c> finds for <paramref name="id"/> set, so the
	/// next update draws it. Retail writes the byte before the hotspots for an id not found; here nothing is written, an
	/// engine guard.
	/// </summary>
	private static void MarkHotspot(Es1DUIWindow window, short id) {
		short index = window.FindHotspotById(id);
		if (index >= 0) {
			window.Hotspots[index].Pressed = true;
		}
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>CrewAssignMenu_Dtor</c> (<c>0002c952</c>) (docs/es1/shell/hangar.md#leaving-it): the two banks
	/// destroyed (<c>GLBitmapArray_Dtor</c>(bank, 3)) and cleared, group script 3 set playing again (<c>Ttm_SetMode</c>(0,
	/// its handle)), the base vtables stored and the menu freed.
	/// </summary>
	public override void Dtor() {
		var banks = Context.Banks;
		banks.CrewPortraits = null;
		banks.CrewMesh = null;
		SetScriptMode(Es1TtmPlayer.Playing);
	}

	/// <summary>The font slots of the hotspots: <c>nm_white</c> (<c>000aacee</c>), <c>nm_ylw</c> (<c>000aacf6</c>) and <c>big_red</c> (<c>000aacfa</c>).</summary>
	private const int WhiteFont = 10, YellowFont = 12, BigRedFont = 13;

	/// <summary>The lines' fill and frame color, <c>Raster_FillFrameRectColours</c>(<c>0x1f</c>, <c>0x1f</c>, rect).</summary>
	private const byte LinesColor = 0x1f;

	/// <summary>The first lines of <c>eng\watext.bin</c> the lines read: the ranks ROOKIE to ELITE, UNASSIGNED, and the chassis' names (chassis −1 reading the line before).</summary>
	private const int RankLines = 0xa1, UnassignedLine = 0xa6, ChassisLines = 0x1b;

	/// <summary>The text at <c>0007ed6e</c>, a HERC status with no HERC.</summary>
	private const string NoStatus = "---";

	/// <summary>
	/// The handler <c>Menu_BuildFromDlg</c> gives the hotspots 1612 to 1614, <c>PilotPortraitHotspot_Draw</c>
	/// (<c>00017a19</c>), over <paramref name="context"/>'s banks and roster.
	/// </summary>
	public static Es1HotspotHandler PortraitHandler(Es1MenuContext context) => (render, rect, id) => DrawPortrait(context, render, rect, id);

	/// <summary>The handler the hotspots 1619 to 1622 are given, <c>PilotLinesHotspot_Draw</c> (<c>00017b4a</c>).</summary>
	public static Es1HotspotHandler LinesHandler(Es1MenuContext context) => (render, rect, id) => DrawLines(context, render, rect, id);

	/// <summary>The handler the hotspots 1615 to 1618 are given, <c>BayNumberHotspot_Draw</c> (<c>00017d9f</c>).</summary>
	public static Es1HotspotHandler BayNumberHandler(Es1MenuContext context) => (render, rect, id) => DrawBayNumber(context, render, rect, id);

	/// <summary>
	/// <c>PilotPortraitHotspot_Draw</c> (<c>00017a19</c>)(<paramref name="rect"/>, <paramref name="id"/>)
	/// (docs/es1/shell/hangar.md#the-screen), on the roster's record its word <c>+7</c> names, read from the roster in
	/// either mode, with <i>n</i> = id − 1611: frame (entry <i>n</i>'s word <c>+0</c>) of
	/// <see cref="Es1MenuBanks.CrewPortraits"/> blitted at the rect's corner plus (1, 1); then, while the record's byte
	/// <c>+1</c> is set and the entry's byte <c>+0xa</c> is 0, frame 0 of <see cref="Es1MenuBanks.CrewMesh"/> at the
	/// corner. Retail reads an entry, and a frame, past their arrays unchecked; either draws nothing here, an engine guard.
	/// </summary>
	private static void DrawPortrait(Es1MenuContext context, Es1ShellRenderContext render, Es1Rect rect, short id) {
		int n = id - InsigniaTile;
		var record = context.Mission.Roster.CurrentRecord;
		if (n is < 0 or >= Es1CareerRecord.PilotCount) {
			return;
		}

		var pilot = record.Pilots[n];
		render.Blit(Frame(context.Banks.CrewPortraits, pilot.NameGroup), rect.X0 + 1, rect.Y0 + 1);
		if (record.Byte1 != 0 && pilot.OnStrength == 0) {
			render.Blit(Frame(context.Banks.CrewMesh, 0), rect.X0, rect.Y0);
		}
	}

	/// <summary>Frame <paramref name="index"/> of <paramref name="bank"/>; none outside it or without it.</summary>
	private static DynamixBitmap? Frame(IReadOnlyList<DynamixBitmap>? bank, int index) =>
		bank != null && index >= 0 && index < bank.Count ? bank[index] : null;

	/// <summary>
	/// <c>PilotLinesHotspot_Draw</c> (<c>00017b4a</c>)(<paramref name="rect"/>, <paramref name="id"/>) (docs/es1/shell/hangar.md#the-screen), with
	/// <i>n</i> = id − 1619 and the record <c>CareerRecord_Current</c>'s: the font <c>nm_ylw</c> for the record's current
	/// pilot <c>+0x12</c> and <c>nm_white</c> otherwise; the alignment 0; the rect filled and framed in
	/// <see cref="LinesColor"/>; then four lines from (left + 2, top + 7), 8 apart: the name, for the player's entry
	/// <c>+0xe</c> the career's name, the careers-log entry the roster's <c>+1</c> points at, and otherwise
	/// <c>PilotEntry_Name</c>(the roster's record its word <c>+7</c> names, <i>n</i>); line <c>0xa1</c> + the entry's rank
	/// <c>+0xd</c>; for a slot <c>+8</c> of −1 line <c>0xa6</c> and <see cref="NoStatus"/>, otherwise line <c>0x1b</c> + the
	/// chassis of the slot's HERC (−1 for an empty slot) and <c>%d%%</c> (<c>0007ed69</c>) of its
	/// <c>Herc_OverallCondition</c>, 0 for an empty slot. Retail reads an entry past the four unchecked, and draws through
	/// a null name; either draws nothing here, engine guards.
	/// </summary>
	private static void DrawLines(Es1MenuContext context, Es1ShellRenderContext render, Es1Rect rect, short id) {
		int n = id - FirstLines;
		var mission = context.Mission;
		if (mission.CurrentRecord is not { } record || n is < 0 or >= Es1CareerRecord.PilotCount) {
			return;
		}

		var data = context.Data;
		var font = data.Fonts[n == record.CurrentPilot ? YellowFont : WhiteFont];
		render.Alignment = 0;
		// Raster_FillFrameRectColours (0004b8d8): the rect filled under the brush {0, fill}, then its edges under {4, frame}.
		var canvas = render.Canvas;
		canvas.FillRect(rect.X0, rect.Y0, rect.X1, rect.Y1, LinesColor);
		canvas.OutlineRect(rect.X0, rect.Y0, rect.X1, rect.Y1, LinesColor);

		int x = rect.X0 + 2, y = rect.Y0 + 7;
		render.DrawString(font, n == record.PlayerEntry ? CareerName(mission.Roster) : PilotName(data, mission.Roster.CurrentRecord, n), x, y);
		y += 8;
		var pilot = record.Pilots[n];
		render.DrawString(font, Line(data, RankLines + pilot.Rank), x, y);
		y += 8;
		string status;
		if (pilot.Slot == -1) {
			render.DrawString(font, Line(data, UnassignedLine), x, y);
			status = NoStatus;
		} else {
			var herc = mission.Hangar?.HercIn(pilot.Slot);
			render.DrawString(font, Line(data, ChassisLines + (herc?.Chassis ?? -1)), x, y);
			status = string.Create(CultureInfo.InvariantCulture, $"{herc?.OverallCondition() ?? 0}%");
		}

		y += 8;
		render.DrawString(font, status, x, y);
	}

	/// <summary>The career's name, the name of the careers-log entry the roster's <c>+1</c> points at; none without one.</summary>
	private static string? CareerName(Es1CareerRoster roster) {
		if (roster.LogEntry is not { } entry) {
			return null;
		}

		int end = Array.IndexOf(entry.Name, (byte)0);
		return Encoding.Latin1.GetString(entry.Name, 0, end < 0 ? entry.Name.Length : end);
	}

	/// <summary>
	/// <c>PilotEntry_Name</c> (<c>00015fc0</c>)(<paramref name="record"/>, <paramref name="entry"/>): none when the pilot
	/// names are not loaded, otherwise name <c>+2</c> of group <c>+0</c>. A group or name outside the file's gives none
	/// here, an engine guard.
	/// </summary>
	private static string? PilotName(Es1ShellData data, Es1CareerRecord record, int entry) {
		var pilot = record.Pilots[entry];
		return data.PilotNames?.Groups is { } groups && pilot.NameGroup >= 0 && pilot.NameGroup < groups.Length
				&& pilot.Name >= 0 && pilot.Name < groups[pilot.NameGroup].Length
			? groups[pilot.NameGroup][pilot.Name]
			: null;
	}

	/// <summary>
	/// <c>BayNumberHotspot_Draw</c> (<c>00017d9f</c>)(<paramref name="rect"/>, <paramref name="id"/>) (docs/es1/shell/hangar.md#the-screen), with <i>n</i>
	/// = id − 1615 and the record <c>CareerRecord_Current</c>'s: for a slot <c>+8</c> of 0 or more, the alignment 0 and
	/// <c>%d</c> (<c>0007ed72</c>) of the slot + 1 in <c>big_red</c> at the rect's corner plus (3, 8). Nothing is drawn
	/// under it. An entry past the four draws nothing here, an engine guard.
	/// </summary>
	private static void DrawBayNumber(Es1MenuContext context, Es1ShellRenderContext render, Es1Rect rect, short id) {
		int n = id - FirstTile;
		if (context.Mission.CurrentRecord is not { } record || n is < 0 or >= Es1CareerRecord.PilotCount) {
			return;
		}

		short slot = record.Pilots[n].Slot;
		if (slot < 0) {
			return;
		}

		render.Alignment = 0;
		render.DrawString(context.Data.Fonts[BigRedFont], (slot + 1).ToString(CultureInfo.InvariantCulture), rect.X0 + 3, rect.Y0 + 8);
	}

	/// <summary>
	/// <c>Text_GetString</c>(<c>0009afa8</c>, <paramref name="number"/>), a line of <c>eng\watext.bin</c>; none, so nothing
	/// drawn, outside the table or without it, an engine guard for its missing bounds test.
	/// </summary>
	private static string? Line(Es1ShellData data, int number) => data.Text is { } text && number >= 0 && number < text.Length ? text[number] : null;
}
