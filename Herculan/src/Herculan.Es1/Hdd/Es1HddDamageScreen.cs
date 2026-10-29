using System.Globalization;
using System.Text;
using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// Page 1 of the Heads-Down Display, the damage screen (docs/es1/simulation/hdd-damage-screen.md): the subject machine's
/// paper doll, each region tinted by its damage, with its weapon icons over it, beside twelve rows that each name a
/// component, an internal system or a weapon and give its integrity in percent, in three views, structural (<c>S</c>),
/// internal (<c>I</c>) and weapons (<c>W</c>). Its slots are <c>HDDDamageVtable</c> (<c>00094aaa</c>)'s:
/// <see cref="Repaint"/>, <see cref="Update"/>, <see cref="Hide"/>, <see cref="Show"/> and <see cref="KeyDispatch"/>.
/// </summary>
public sealed class Es1HddDamageScreen : Es1HddPage {
	/// <summary>The views, by <see cref="View"/>.</summary>
	public const int StructuralView = 0, InternalView = 1, WeaponsView = 2;

	/// <summary>The label pairs, one per row shown.</summary>
	public const int RowCount = 12;

	/// <summary><c>STRINGS0.STR</c>'s groups of the structural and internal views' names (docs/es1/formats/str-strings.md#the-groups).</summary>
	public const int StructuralGroup = 12, InternalGroup = 13;

	/// <summary>The structural and internal views' row counts.</summary>
	public const int StructuralRows = 0x13, InternalRows = 0xc;

	/// <summary>The first readout of each view in <see cref="Es1Mech.ReadDamageReadouts"/>.</summary>
	public const int FirstStructural = 1, FirstInternal = 20, FirstWeapon = 32;

	/// <summary>The palette indices the screen fills with: the labels', the no-subject label's and the page's.</summary>
	public const byte LabelFill = 0x3f, NoSubjectFill = 0x3b, PageFill = 0x1f;

	/// <summary>The palette indices the full redraw's recolor passes take: what they mute to, the doll's key and the icons' key.</summary>
	public const byte MutedColor = 0x32, DollKey = 0x36, IconKey = 0x38;

	/// <summary>The weapons view's names: <c>HddDamage_WeaponNames</c> (<c>000b160c</c>), ten pointers.</summary>
	public const int WeaponNameCount = 10;

	/// <summary>The bytes of <c>HddDamage_WeaponNameBuffer</c> (<c>000b1634</c>–<c>000b1693</c>), in 8-byte slots.</summary>
	public const int WeaponNameBufferSize = 0x60, WeaponNameSlot = 8;

	/// <summary>The string the value column's width is measured on, <c>100</c> (<c>00094ac6</c>).</summary>
	private const string WidestValue = "100";

	/// <summary><c>Label_SetRect</c>'s flags 1, which start the text at the anchor.</summary>
	private const int LeftAligned = 1;

	/// <summary>The rows' label font by damage state 0 to 4: <c>cpgreen</c>, <c>cpylw</c>, <c>cporange</c>, <c>cpred</c> and <c>cpgrey</c>, <c>ColorSchemePanels</c> 1, 3, 9, 2 and 7.</summary>
	private static readonly int[] StateFonts = [1, 3, 9, 2, 7];

	private readonly Es1Label[] _names = new Es1Label[RowCount];
	private readonly Es1Label[] _values = new Es1Label[RowCount];
	private readonly Es1Label _caption;
	private readonly Es1Label _noSubject;
	private readonly short[] _drawnReadings = new short[Es1Mech.DamageReadingCount];
	private readonly byte[] _weaponNameBuffer = new byte[WeaponNameBufferSize];
	private readonly int[] _weaponNames = new int[WeaponNameCount];

	/// <summary>
	/// <c>HddDamageScreen_Ctor</c> (<c>ES1DBSIM 00042164</c>)(0, <paramref name="display"/>, <paramref name="damageColumn"/>,
	/// <paramref name="screen"/>) (docs/es1/simulation/hdd-damage-screen.md#building-it--hdddamagescreen_ctor-00042164):
	/// the page base; with <i>c</i> <c>cpgreen</c>'s cell height and <i>w</i> the width of <see cref="WidestValue"/> in
	/// it, for row <i>i</i> from the column's <i>y0</i> + <i>i</i> <i>c</i> to that + 4, a name label over the column's
	/// <i>x0</i> + 12 to <i>x1</i> − <i>w</i> and a value label over <i>x1</i> − <i>w</i> to <i>x1</i>, each
	/// <c>cpgreen</c>, left-aligned with the margin (0, −1), on <see cref="LabelFill"/>; the caption, <c>cpgreen</c>,
	/// centered with the margin (0, −1), over the screen rect's <i>x0</i> + <c>0x14</c> to <i>x0</i> + <c>0x3c</c> and
	/// from its <i>y1</i> − 2 − <i>c</i> to <i>y1</i> − 2; <see cref="SetView"/>(0) and the view drawn −1; the subject
	/// and the one drawn the display's <see cref="Es1HddDisplay.DamageSubject"/>; the drawn readouts 1, 20 and 32, the
	/// first of each view's, 1; and the no-subject label, <c>cpred</c>, centered with the margin (0, −1), on
	/// <see cref="NoSubjectFill"/>, over 40 pixels either side of the screen rect's middle <i>x</i> and from its middle
	/// <i>y</i> to that + 10.
	/// </summary>
	internal Es1HddDamageScreen(Es1HddDisplay display, Es1Rect damageColumn, Es1Rect screen)
		: base(display, damageColumn, screen) {
		var green = display.Font(Es1CockpitViews.CpGreen);
		int cell = green?.CellHeight ?? 0, width = green?.Measure(WidestValue) ?? 0;
		for (int i = 0; i < RowCount; i++) {
			int top = damageColumn.Y0 + i * cell;
			_names[i] = new Es1Label(green) { FillColor = LabelFill };
			_names[i].SetRect(LeftAligned, new Es1Rect(damageColumn.X0 + 12, top, damageColumn.X1 - width, top + 4), 0, -1);
			_values[i] = new Es1Label(green) { FillColor = LabelFill };
			_values[i].SetRect(LeftAligned, new Es1Rect(damageColumn.X1 - width, top, damageColumn.X1, top + 4), 0, -1);
		}

		_caption = new Es1Label(green);
		_caption.SetRect(Es1Label.Centered, new Es1Rect(screen.X0 + 0x14, screen.Y1 - 2 - cell, screen.X0 + 0x3c, screen.Y1 - 2), 0, -1);
		Array.Fill(_weaponNames, -1);
		SetView(StructuralView);
		DrawnView = -1;
		Subject = display.DamageSubject;
		DrawnSubject = Subject;
		_drawnReadings[FirstStructural] = 1;
		_drawnReadings[FirstInternal] = 1;
		_drawnReadings[FirstWeapon] = 1;
		int middleX = ((screen.X1 - screen.X0) >> 1) + screen.X0, middleY = ((screen.Y1 - screen.Y0) >> 1) + screen.Y0;
		_noSubject = new Es1Label(display.Font(Es1CockpitViews.CpRed)) { FillColor = NoSubjectFill };
		_noSubject.SetRect(Es1Label.Centered, new Es1Rect(middleX - 0x28, middleY, middleX + 0x28, middleY + 10), 0, -1);
	}

	/// <summary><c>+0x10</c> and <c>+0x40</c>, the twelve name labels and the twelve value labels.</summary>
	public IReadOnlyList<Es1Label> NameLabels => _names;

	/// <inheritdoc cref="NameLabels"/>
	public IReadOnlyList<Es1Label> ValueLabels => _values;

	/// <summary><c>+0x70</c>, the subject's caption.</summary>
	public Es1Label Caption => _caption;

	/// <summary>
	/// <c>+0x74</c>, the view, <see cref="StructuralView"/> to <see cref="WeaponsView"/>, which is also the entry of
	/// <c>STRINGS0.STR</c> group 11 that <see cref="Es1HddDisplay.SetTitle"/> shows on page 1.
	/// </summary>
	public int View { get; private set; }

	/// <summary><c>+0x78</c>, the view the last update drew, −1 to force a full redraw.</summary>
	public int DrawnView { get; private set; }

	/// <summary><c>+0x7c</c>, the row offset, the first row the labels show.</summary>
	public int RowOffset { get; private set; }

	/// <summary><c>+0x80</c>, the row offset the last update drew.</summary>
	public int DrawnOffset { get; private set; }

	/// <summary>
	/// <c>+0x84</c>, the view's name list as <see cref="SetView"/> writes it: <c>STRINGS0.STR</c>'s group, or −1 for the
	/// weapons view's <c>HddDamage_WeaponNames</c>. What reads it is Open (docs/es1/simulation/hdd-damage-screen.md#open);
	/// the update picks its names itself.
	/// </summary>
	public int NameList { get; private set; }

	/// <summary><c>+0x88</c>, the row count.</summary>
	public int RowTotal { get; private set; }

	/// <summary><c>+0x8c</c>, the 42 readouts the last update drew.</summary>
	public IReadOnlyList<short> DrawnReadings => _drawnReadings;

	/// <summary><c>+0xe0</c>, the subject the last update drew.</summary>
	public IEs1SimObject? DrawnSubject { get; private set; }

	/// <summary><c>+0xe4</c>, the subject, the display's <see cref="Es1HddDisplay.DamageSubject"/> as the last update read it.</summary>
	public IEs1SimObject? Subject { get; private set; }

	/// <summary><c>+0xe8</c>, the no-subject label.</summary>
	public Es1Label NoSubjectLabel => _noSubject;

	/// <summary>
	/// <c>HddDamage_TintGuard</c> (<c>000b1694</c>), a global the update sets before a tint; what reads it is Open
	/// (docs/es1/simulation/hdd-damage-screen.md#open). Kept with the screen, of which the simulator builds one, as the
	/// next two.
	/// </summary>
	public bool TintGuard { get; private set; }

	/// <summary>The dword at <c>000b16b0</c>, a global the weapons view's tint stores 0 in; what reads it is Open (docs/es1/simulation/hdd-damage-screen.md#open).</summary>
	public int Dword16B0 { get; private set; }

	/// <summary>
	/// <c>HddDamage_WeaponNames</c> (<c>000b160c</c>)[<paramref name="index"/>]: the string at the slot of
	/// <c>HddDamage_WeaponNameBuffer</c> (<c>000b1634</c>) the entry points at, up to its terminating zero; none for an
	/// entry no update has set or an index outside the ten.
	/// </summary>
	public string? WeaponName(int index) {
		if (index < 0 || index >= WeaponNameCount || _weaponNames[index] < 0) {
			return null;
		}

		int start = _weaponNames[index];
		int end = Array.IndexOf(_weaponNameBuffer, (byte)0, start);
		return Encoding.Latin1.GetString(_weaponNameBuffer, start, (end < 0 ? WeaponNameBufferSize : end) - start);
	}

	private Es1Cockpit Cockpit => Display.Cockpit;

	// ---- The slots.

	/// <summary>
	/// Slot <c>+4</c>, <c>HddDamageScreen_Repaint</c> (<c>000423a8</c>) (docs/es1/simulation/hdd-damage-screen.md#views):
	/// the view drawn −1, so the update redraws the whole page, then <see cref="Draw"/> with no copy. It does not read
	/// <see cref="Es1HddPage.Stage"/>.
	/// </summary>
	public override void Repaint() {
		DrawnView = -1;
		Draw(false);
	}

	/// <summary>Slot <c>+8</c>, <c>HddDamageScreen_Tick</c> (<c>000423c4</c>): <see cref="Draw"/> with the copy.</summary>
	public override void Update() => Draw(true);

	/// <summary>Slot <c>+0xc</c>, <c>HddDamageScreen_HideNoOp</c> (<c>00042399</c>): nothing.</summary>
	public override void Hide() {
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>HddDamageScreen_Show</c> (<c>00043294</c>): <see cref="SetView"/> of the view, then the view
	/// drawn −1.
	/// </summary>
	public override void Show() {
		SetView(View);
		DrawnView = -1;
	}

	/// <summary>Slot <c>+0x14</c>, <c>HddDamageScreen_KeyDispatch</c> (<c>0004239f</c>): 0.</summary>
	public override short KeyDispatch(ushort code) => 0;

	// ---- The views.

	/// <summary>
	/// <c>HddDamageScreen_SetView</c> (<c>000423d8</c>)(screen, <paramref name="view"/>)
	/// (docs/es1/simulation/hdd-damage-screen.md#views): the view stored, the row offset 0 and the offset drawn −1; then
	/// for view 0 the name list group 12 and <see cref="StructuralRows"/> rows, for 1 group 13 and
	/// <see cref="InternalRows"/>, for 2 the weapon names and the subject's mount count, 0 with no subject; another view
	/// sets neither. A HERC with no mount manager, which retail reads through, counts 0, an engine guard.
	/// </summary>
	public void SetView(int view) {
		View = view;
		RowOffset = 0;
		DrawnOffset = -1;
		switch (view) {
			case StructuralView:
				NameList = StructuralGroup;
				RowTotal = StructuralRows;
				break;
			case InternalView:
				NameList = InternalGroup;
				RowTotal = InternalRows;
				break;
			case WeaponsView:
				NameList = -1;
				RowTotal = (Subject as Es1Mech)?.Mounts?.Mounts.Count ?? 0;
				break;
		}
	}

	/// <summary><c>HddDamageScreen_NextView</c> (<c>0004246c</c>): <see cref="SetView"/> with the view one on, 2 wrapping to 0.</summary>
	public void NextView() => SetView(View < WeaponsView ? View + 1 : StructuralView);

	/// <summary><c>HddDamageScreen_PrevView</c> (<c>00042494</c>): <see cref="SetView"/> with the view one back, 0 wrapping to 2.</summary>
	public void PrevView() => SetView(View < InternalView ? WeaponsView : View - 1);

	/// <summary>
	/// <c>HddDamageScreen_PageDown</c> (<c>000424bc</c>): the row offset plus <see cref="RowCount"/> while that stays below
	/// the row count. What calls it is Open (docs/es1/simulation/hdd-damage-screen.md#open).
	/// </summary>
	public void PageDown() {
		if (RowOffset + RowCount < RowTotal) {
			RowOffset += RowCount;
		}
	}

	/// <summary>
	/// <c>HddDamageScreen_PageUp</c> (<c>000424dc</c>): the row offset less <see cref="RowCount"/> while that stays at or
	/// above 0. What calls it is Open (docs/es1/simulation/hdd-damage-screen.md#open).
	/// </summary>
	public void PageUp() {
		if (RowOffset - RowCount >= 0) {
			RowOffset -= RowCount;
		}
	}

	// ---- Drawing.

	/// <summary>
	/// <c>HddDamageScreen_Update</c> (<c>000424f8</c>)(screen, <paramref name="copy"/>)
	/// (docs/es1/simulation/hdd-damage-screen.md#drawing-it--hdddamagescreen_update-000424f8), where every copy is
	/// <see cref="Es1Cockpit.DrawToCockpit"/> with 1 and made only with <paramref name="copy"/>:
	///
	/// <list type="number">
	/// <item>While the display is dark, the screen rect filled <see cref="PageFill"/> and copied, and nothing more.</item>
	/// <item>The subject read from the display; a new subject runs <see cref="SetView"/> of the view and is kept as drawn,
	/// then a new view and a new row offset are kept as drawn, each in that order, so a new subject's
	/// <see cref="SetView"/> leaves the offset drawn −1 for the next update to find changed.</item>
	/// <item>With no subject: nothing unless the subject or the view changed; the offset put back to the one drawn.</item>
	/// <item>With one, the view's names, readouts, compared count and <c>.PDG</c> view (<see cref="ViewData"/>), the
	/// weapons view's names rebuilt first (<see cref="RebuildWeaponNames"/>); the readouts read; with none of the three
	/// changed and the compared readouts equal to the drawn ones, nothing more. A change of any of the three makes this a
	/// full redraw. The doll's origin is the <c>.PDG</c> view's point plus the screen rect's top-left; the rows shown end
	/// at the row count or the offset + 12, whichever is less, and in the weapons view at the icon count when less.</item>
	/// <item>The display's draw page made its source page. With only the offset changed, the labels' block filled
	/// <see cref="PageFill"/>; with the subject or the view changed, the screen rect filled, the caption set
	/// (<see cref="Es1HddDisplay.SetSubjectCaption"/>), and with no subject the no-subject label written with
	/// <see cref="Es1HddDisplay.NoSubjectText"/>, the screen rect copied and nothing more; otherwise the doll's picture
	/// blitted at its origin and the recolor passes (<see cref="PaintDoll"/>).</item>
	/// <item>The rows (<see cref="DrawRow"/>).</item>
	/// <item>The readouts kept as drawn, and on a full redraw the screen rect copied.</item>
	/// </list>
	///
	/// <para>A subject that is not a HERC, which the display's target step never hands the page, draws nothing past the
	/// second step, and a chassis with no <c>.PDG</c> or no such view, where retail reads through a null record, draws no
	/// doll and no region rows: engine guards.</para>
	/// </summary>
	private void Draw(bool copy) {
		if (Display.Dropout.Dark) {
			Cockpit.PushCanvasContext().FillRect(Screen.X0, Screen.Y0, Screen.X1, Screen.Y1, PageFill);
			Copy(Screen, copy);
			return;
		}

		Subject = Display.DamageSubject;
		bool newSubject = !ReferenceEquals(Subject, DrawnSubject), newView = View != DrawnView, newOffset = RowOffset != DrawnOffset;
		if (newSubject) {
			SetView(View);
			DrawnSubject = Subject;
		}

		if (newView) {
			DrawnView = View;
		}

		if (newOffset) {
			DrawnOffset = RowOffset;
		}

		bool full = newSubject || newView || newOffset;
		var readings = new short[Es1Mech.DamageReadingCount];
		var data = new ViewData(0, 0, null, null, [], [], (0, 0), 0);
		if (Subject == null) {
			if (!newSubject && !newView) {
				return;
			}

			RowOffset = DrawnOffset;
		} else {
			if (Subject is not Es1Mech machine) {
				return;
			}

			if (View == WeaponsView) {
				RebuildWeaponNames(machine);
			}

			data = ViewOf(machine);
			readings = machine.ReadDamageReadouts();
			if (!full && Same(readings, data.First, data.Compared)) {
				return;
			}
		}

		var canvas = Cockpit.PushCanvasContext();
		var video = canvas.Display;
		video.SourcePage = video.DrawPage;
		if (!newSubject && !newView) {
			if (newOffset) {
				var first = _names[0].Rect;
				var last = _values[RowCount - 1].Rect;
				canvas.FillRect(first.X0, first.Y0, last.X1, last.Y1, PageFill);
			}
		} else {
			canvas.FillRect(Screen.X0, Screen.Y0, Screen.X1, Screen.Y1, PageFill);
			Display.SetSubjectCaption(canvas, _caption);
			if (Subject == null) {
				_noSubject.SetText(canvas, Display.NoSubjectText);
				Copy(Screen, copy);
				return;
			}

			PaintDoll(canvas, data);
		}

		int rows = View == WeaponsView ? data.Icons.Count : data.Regions.Length;
		for (int i = 0; i < rows; i++) {
			DrawRow(canvas, data, readings, i, full, copy);
		}

		Array.Copy(readings, _drawnReadings, _drawnReadings.Length);
		if (full) {
			Copy(Screen, copy);
		}
	}

	/// <summary>
	/// The weapons view's names rebuilt: the row count cleared and, for each mount <i>i</i> below the row count it had,
	/// a mount's <see cref="Es1WeaponMount.DisplayName"/> copied whole, with its terminating zero, into the next 8-byte
	/// slot of the name buffer and <see cref="WeaponName"/>(<i>i</i>) pointed at it, and the row count one more for every
	/// mount, null or not, so it ends where it began. An empty hardpoint's entry keeps the pointer a previous update stored.
	/// Nothing bounds retail's copy, so a name of 8 characters or more runs into the next slot and the next mount's name
	/// then overwrites its tail: <see cref="WeaponName"/> of the first reads its first eight characters joined to the
	/// second's name. That is retail's buffer, and is ported so. A copy past the buffer's end, which retail writes into
	/// the globals after it, stops at the end, and a mount past the ten pointers stores none: engine guards.
	/// </summary>
	private void RebuildWeaponNames(Es1Mech machine) {
		var mounts = machine.Mounts?.Mounts ?? [];
		int count = RowTotal, slot = 0;
		RowTotal = 0;
		for (int i = 0; i < count; i++) {
			if (i < mounts.Count && mounts[i] is { } mount) {
				byte[] name = Encoding.Latin1.GetBytes(mount.DisplayName() + "\0");
				int start = slot * WeaponNameSlot;
				if (start < WeaponNameBufferSize) {
					Array.Copy(name, 0, _weaponNameBuffer, start, Math.Min(name.Length, WeaponNameBufferSize - start));
				}

				if (RowTotal < WeaponNameCount) {
					_weaponNames[RowTotal] = start < WeaponNameBufferSize ? start : -1;
				}

				slot++;
			}

			RowTotal++;
		}
	}

	/// <summary>
	/// The view's data: for view 0 group 12, the readouts from <see cref="FirstStructural"/>, 19 compared and
	/// <c>.PDG</c> view 0; for view 1 group 13, from <see cref="FirstInternal"/>, 12 and <c>.PDG</c> view 1; for view 2 the
	/// weapon names, from <see cref="FirstWeapon"/>, the row count and <c>.PDG</c> view 0. The doll is the record of the
	/// chassis' paper doll (<see cref="Es1MechType.PaperDoll"/>), the icons the machine's <see cref="Es1Mech.WeaponIcons"/>.
	/// </summary>
	private ViewData ViewOf(Es1Mech machine) {
		var (first, compared, dollView) = View switch {
			StructuralView => (FirstStructural, StructuralRows, StructuralView),
			InternalView => (FirstInternal, InternalRows, InternalView),
			_ => (FirstWeapon, RowTotal, StructuralView),
		};
		var doll = machine.Type.PaperDoll?.Entries is { } views && dollView < views.Length ? views[dollView] : null;
		var origin = ((doll?.Origin.X ?? 0) + Screen.X0, (doll?.Origin.Y ?? 0) + Screen.Y0);
		int end = Math.Min(RowTotal, RowOffset + RowCount);
		var icons = machine.WeaponIcons;
		if (View == WeaponsView && icons.Count < end) {
			end = icons.Count;
		}

		return new ViewData(first, compared, doll, machine.Type.PaperDollPicture(dollView), doll?.Regions ?? [], icons, origin, end);
	}

	/// <summary>
	/// The full redraw's doll: its picture blitted at the origin with flags 0; then in views 0 and 1
	/// <see cref="BlitWeaponIcons"/> and over each icon's rect <see cref="Es1PaperDoll.RecolorRect"/> in mode 1, its
	/// <see cref="IconKey"/> pixels made <see cref="MutedColor"/>; in view 2 the view's rect, the origin to the origin
	/// plus the view's size, in mode 3 from <see cref="DollKey"/>, so <c>0x36</c>–<c>0x38</c> become
	/// <see cref="MutedColor"/>, the same over each region whose <c>colorId</c> is not <see cref="DollKey"/> with that
	/// <c>colorId</c> as the key, <see cref="BlitWeaponIcons"/>, and over each icon's rect mode 1 with tint
	/// <see cref="IconKey"/> and key <see cref="DollKey"/>, its <c>0x36</c> pixels made <c>0x38</c>.
	/// </summary>
	private void PaintDoll(Es1PanelCanvas canvas, ViewData data) {
		var (x, y) = data.Origin;
		canvas.Blit(data.Picture, x, y);
		if (View is StructuralView or InternalView) {
			BlitWeaponIcons(canvas, data.Origin, data.Icons);
			foreach (var icon in data.Icons) {
				Es1PaperDoll.RecolorRect(canvas, (int)Es1PaperDoll.RecolorMode.Replace, MutedColor, IconKey, icon.Rect.Offset(x, y));
			}
		} else if (View == WeaponsView) {
			var size = data.Doll?.Size ?? default;
			Es1PaperDoll.RecolorRect(canvas, (int)Es1PaperDoll.RecolorMode.Fill, MutedColor, DollKey, new Es1Rect(x, y, x + size.X, y + size.Y));
			foreach (var region in data.Regions) {
				if (region.ColorId != DollKey) {
					Es1PaperDoll.RecolorRect(canvas, (int)Es1PaperDoll.RecolorMode.Fill, MutedColor, region.ColorId, RegionRect(region).Offset(x, y));
				}
			}

			BlitWeaponIcons(canvas, data.Origin, data.Icons);
			foreach (var icon in data.Icons) {
				Es1PaperDoll.RecolorRect(canvas, (int)Es1PaperDoll.RecolorMode.Replace, IconKey, DollKey, icon.Rect.Offset(x, y));
			}
		}
	}

	/// <summary>
	/// Row <paramref name="row"/> (docs/es1/simulation/hdd-damage-screen.md#the-rows), shown while the offset ≤ the row &lt;
	/// the rows' end, on label pair row − offset. In views 0 and 1 it is region <paramref name="row"/> of the view, whose
	/// index <i>k</i> picks the name, entry <i>k</i> of the view's group, and the reading, readout first + <i>k</i>; its rect,
	/// <c>colorId</c> and <c>recolorMode</c> the tint. In view 0 a region with index 0 or 2 tints on the mean of its
	/// reading and the next index's, and one with index 1 or 3 tints nothing. In view 2 it is icon <paramref name="row"/>:
	/// its rect, the key <see cref="DollKey"/> and mode 0 for the tint, whose color the view's first region picks; readout
	/// 32 + the row; and the name <see cref="WeaponName"/>(the icon's mount index).
	///
	/// <para>A row that tints, on a full redraw or when its tint reading differs from the drawn one: <see cref="TintGuard"/>
	/// set, and in view 2 <see cref="Dword16B0"/> 0; <see cref="Es1PaperDoll.PickRegionTint"/> for both readings; on a full
	/// redraw or a changed state, view 2's <see cref="Es1PaperDoll.RecolorRect"/> with the new color, keyed on the drawn
	/// state's color unless this is a full redraw, or views 0 and 1's <see cref="Es1PaperDoll.RecolorRectFromArt"/> on the
	/// doll's picture; and the rect copied.</para>
	///
	/// <para>A row with a name, on a full redraw or when its own reading differs from the drawn one: the states of both
	/// readings; while it is shown, the value label written with (<c>0x100</c> − reading) × 100 ≫ 8 in decimal, and the
	/// name label with the name on a full redraw or a changed state, each in <see cref="StateFonts"/>' font for the new
	/// state and copied when this is not a full redraw. A row with no name sets neither.</para>
	///
	/// <para>The states the name reads are the tint region's: in view 0 a region with index 1 or 3 reads the region before
	/// it, the first row the region itself where retail reads the bytes before the array, and in view 2 the view's first
	/// region; they differ from <see cref="Es1PaperDoll.ToConditionState"/> only for a mode above 3. A region index
	/// outside the readouts reads 0, a group entry missing from the strings gives no name, and view 2 with no region tints
	/// nothing: engine guards.</para>
	/// </summary>
	private void DrawRow(Es1PanelCanvas canvas, ViewData data, short[] readings, int row, bool full, bool copy) {
		bool shown = RowOffset <= row && row < data.End, tints = true;
		short tintNew, tintDrawn, nameNew, nameDrawn;
		PaperDollGraphic.ViewRegion? tintRegion, stateRegion;
		Es1Rect rect;
		int key, mode;
		string? name;
		if (View == WeaponsView) {
			var icon = data.Icons[row];
			rect = icon.Rect;
			key = DollKey;
			mode = (int)Es1PaperDoll.RecolorMode.Shift;
			tintNew = nameNew = Reading(readings, data.First + row);
			tintDrawn = nameDrawn = Reading(_drawnReadings, data.First + row);
			tintRegion = stateRegion = data.Regions.Length > 0 ? data.Regions[0] : null;
			name = WeaponName(icon.MountIndex);
		} else {
			var region = data.Regions[row];
			int k = region.Index;
			tintNew = nameNew = Reading(readings, data.First + k);
			tintDrawn = nameDrawn = Reading(_drawnReadings, data.First + k);
			tintRegion = stateRegion = region;
			if (View == StructuralView && k is 0 or 2) {
				tintNew = (short)((tintNew + Reading(readings, data.First + k + 1)) >> 1);
				tintDrawn = (short)((tintDrawn + Reading(_drawnReadings, data.First + k + 1)) >> 1);
			} else if (View == StructuralView && k is 1 or 3) {
				tints = false;
				stateRegion = row > 0 ? data.Regions[row - 1] : region;
			}

			rect = RegionRect(region);
			key = region.ColorId;
			mode = region.RecolorMode;
			name = Display.Text(View == StructuralView ? StructuralGroup : InternalGroup, k);
		}

		var (x, y) = data.Origin;
		rect = rect.Offset(x, y);
		bool setName = shown;
		if (tints && tintRegion != null && (full || tintNew != tintDrawn)) {
			TintGuard = true;
			if (View == WeaponsView) {
				Dword16B0 = 0;
			}

			var (newState, newTint) = Es1PaperDoll.PickRegionTint(tintRegion, tintNew);
			var (drawnState, drawnTint) = Es1PaperDoll.PickRegionTint(tintRegion, tintDrawn);
			if (full || newState != drawnState) {
				if (View == WeaponsView) {
					Es1PaperDoll.RecolorRect(canvas, mode, newTint, full ? key : drawnTint, rect);
				} else {
					Es1PaperDoll.RecolorRectFromArt(canvas, mode, newTint, key, data.Origin, data.Picture, rect);
				}

				Copy(rect, copy);
			}
		}

		short state = 0;
		if (name == null || (!full && nameNew == nameDrawn)) {
			shown = false;
			setName = false;
		} else {
			state = State(stateRegion, nameNew);
			if (!full && state == State(stateRegion, nameDrawn)) {
				setName = false;
			}
		}

		if (!shown) {
			return;
		}

		var font = Display.Font(StateFonts[state]);
		int pair = row - RowOffset;
		if (setName) {
			_names[pair].Font = font;
			_names[pair].SetText(canvas, name);
			Copy(_names[pair].Rect, !full && copy);
		}

		_values[pair].Font = font;
		_values[pair].SetText(canvas, ((0x100 - nameNew) * 100 >> 8).ToString(CultureInfo.InvariantCulture));
		Copy(_values[pair].Rect, !full && copy);
	}

	/// <summary>
	/// <c>HddDamageScreen_BlitWeaponIcons</c> (<c>000432b0</c>)(screen, <paramref name="origin"/>, <paramref name="icons"/>):
	/// each icon with a bitmap blitted with its flags at its point plus the origin.
	/// </summary>
	private static void BlitWeaponIcons(Es1PanelCanvas canvas, (int X, int Y) origin, IReadOnlyList<Es1WeaponIcon> icons) {
		foreach (var icon in icons) {
			if (icon.Bitmap != null) {
				canvas.Blit(icon.Bitmap, icon.Position.X + origin.X, icon.Position.Y + origin.Y, icon.BlitFlags);
			}
		}
	}

	/// <summary>Whether entries <paramref name="first"/> to <paramref name="first"/> + <paramref name="count"/> of the readouts equal the drawn ones, retail's <c>memcmp</c>; entries past the 42 are not compared.</summary>
	private bool Same(short[] readings, int first, int count) {
		for (int i = first; i < first + count && i < readings.Length; i++) {
			if (readings[i] != _drawnReadings[i]) {
				return false;
			}
		}

		return true;
	}

	/// <summary>The damage state <see cref="Es1PaperDoll.PickRegionTint"/> gives on <paramref name="region"/>, or <see cref="Es1PaperDoll.ToConditionState"/> with none.</summary>
	private static short State(PaperDollGraphic.ViewRegion? region, short reading) =>
		region != null ? Es1PaperDoll.PickRegionTint(region, reading).State : Es1PaperDoll.ToConditionState(reading);

	private static short Reading(short[] readings, int index) => index >= 0 && index < readings.Length ? readings[index] : (short)0;

	private static Es1Rect RegionRect(PaperDollGraphic.ViewRegion region) =>
		new(region.TopLeft.X, region.TopLeft.Y, region.BottomRight.X, region.BottomRight.Y);

	/// <summary><see cref="Es1Cockpit.DrawToCockpit"/> of <paramref name="rect"/> with 1 when <paramref name="copy"/> is set.</summary>
	private void Copy(Es1Rect rect, bool copy) {
		if (copy) {
			Cockpit.DrawToCockpit(rect);
		}
	}

	/// <summary>
	/// What the update reads for the view: the first readout, the count compared, the <c>.PDG</c> view and its picture, its
	/// regions, the icons, the doll's origin on the canvas and the rows' end.
	/// </summary>
	private readonly record struct ViewData(int First, int Compared, PaperDollGraphic.ViewEntry? Doll, DynamixBitmap? Picture,
		PaperDollGraphic.ViewRegion[] Regions, IReadOnlyList<Es1WeaponIcon> Icons, (int X, int Y) Origin, int End);
}
