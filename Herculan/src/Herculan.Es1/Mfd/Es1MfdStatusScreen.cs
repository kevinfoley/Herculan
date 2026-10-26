using System.Globalization;
using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Mfd;

/// <summary>
/// The MFD's status screen, modes 0 and 4 (docs/es1/simulation/mfd-status-screen.md): the subject's picture, a HERC's
/// paper doll tinted region by region or a flyer's silhouette, with its name and condition in five labels down a
/// column. Its slots are <c>MFDStatusVtable</c> (<c>000935fe</c>)'s.
/// </summary>
public sealed class Es1MfdStatusScreen : Es1MfdScreen {
	/// <summary>The labels, <c>+0xc</c>–<c>+0x1c</c>.</summary>
	public const int LabelCount = 5;

	/// <summary>The labels' column, from the rect's <i>x0</i>, and their height.</summary>
	public const int LabelLeft = 30, LabelRight = 60, LabelHeight = 10;

	/// <summary>The palette index the paint fills the rect in.</summary>
	public const byte Fill = 0x3f;

	/// <summary>The paper doll's view the screen draws.</summary>
	public const int DollView = 2;

	/// <summary>The rows a silhouette's bottom row stands above the rect's, and the columns it stands in from <i>x0</i>.</summary>
	public const int SilhouetteBottom = 5, SilhouetteLeft = 2;

	/// <summary>
	/// <c>STRINGS0.STR</c>'s groups the labels read (docs/es1/formats/str-strings.md#the-groups): <c>TARGET:</c>,
	/// <c>STATUS:</c>, the chassis names, the structures' names, the flyers' names, <c>NONE</c>, <c>UNKNOWN</c> and the
	/// five conditions.
	/// </summary>
	public const int TargetGroup = 17, StatusGroup = 18, ChassisGroup = 19, BaseGroup = 20, FlyerGroup = 21, NoneGroup = 22,
		UnknownGroup = 23, ConditionGroup = 24;

	/// <summary>The target classes the paint tells apart, the object's <c>+0x162</c>.</summary>
	public const short HercClass = 0, StructureClass = 1, FlyerClass = 2;

	/// <summary>The damage readings the condition scans, 20 to 31 of <see cref="Es1Mech.ReadDamageReadouts"/>.</summary>
	private const int FirstScanned = 20, ScannedCount = 12;

	private static readonly int[] LabelTops = [0, 8, 21, 28, 35];

	private readonly Es1Label[] _labels = new Es1Label[LabelCount];
	private readonly IReadOnlyList<HudFont?> _fonts;
	private readonly StringFile? _strings;
	private readonly IReadOnlyList<DynamixBitmap>? _flyers;

	/// <summary>
	/// <c>MfdStatusScreen_Ctor</c> (<c>ES1DBSIM 00032f74</c>)(0, <paramref name="rect"/>): the base, then the five labels
	/// in <c>cpred</c> with no plate, each centered in {<i>x0</i> + 30, <i>y0</i> + top, <i>x0</i> + 60, <i>y0</i> + top
	/// + 10}, the tops 0, 8, 21, 28 and 35.
	/// </summary>
	/// <param name="flyers"><c>MfdStatus_FlyersBank</c> (<c>000935f8</c>), the frames of <c>dba\flyers</c>, which the constructor loads once with <c>dba\bases</c>; none when null.</param>
	internal Es1MfdStatusScreen(Es1Cockpit cockpit, Es1Rect rect, Es1MfdSharedBlock shared, IReadOnlyList<HudFont?> fonts,
			StringFile? strings, IReadOnlyList<DynamixBitmap>? flyers)
		: base(cockpit, rect, shared) {
		_fonts = fonts;
		_strings = strings;
		_flyers = flyers;
		for (int i = 0; i < LabelCount; i++) {
			_labels[i] = new Es1Label(Font(Es1CockpitViews.CpRed));
			int top = rect.Y0 + LabelTops[i];
			_labels[i].SetRect(Es1Label.Centered, new Es1Rect(rect.X0 + LabelLeft, top, rect.X0 + LabelRight, top + LabelHeight));
		}
	}

	/// <summary><c>+0xc</c>–<c>+0x1c</c>, labels 0 to 4: the caption, the name, <c>STATUS:</c>, the condition and the readout.</summary>
	public IReadOnlyList<Es1Label> Labels => _labels;

	/// <summary>
	/// Slot <c>+4</c>, <c>MfdStatusScreen_Paint</c> (<c>000330b4</c>) (docs/es1/simulation/mfd-status-screen.md#the-paint):
	/// the rect filled in <see cref="Fill"/>; then, by the shared block's subject: none, <c>TARGET:</c> and <c>NONE</c> in
	/// <c>cpblue</c>; a HERC (<see cref="PaintHerc"/>); a flyer, frame 0 of <c>dba\flyers</c> at the bottom left,
	/// <c>TARGET:</c>, <c>LANDSKIMMER</c> and the condition from its damage; any class past 2, <c>TARGET:</c> and
	/// <c>UNKNOWN</c> in <c>cpblue</c>.
	/// </summary>
	public override void Paint() {
		var canvas = Cockpit.PushCanvasContext();
		canvas.FillRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, Fill);
		var subject = Shared.Subject;
		if (subject == null) {
			_labels[1].Font = Font(Es1CockpitViews.CpBlue);
			_labels[0].SetText(canvas, Text(TargetGroup, 0));
			_labels[1].SetText(canvas, Text(NoneGroup, 0));
			return;
		}

		switch (subject.TargetClass) {
			case HercClass:
				// Retail reads every class-0 object as a HERC; this engine's only class-0 object is one.
				if (subject is Es1Mech herc) {
					PaintHerc(canvas, herc);
				}

				break;
			case StructureClass:
				// PLACEHOLDER: frame n of dba\bases (MfdStatus_BasesBank, 000935f4) at the bottom left, n the type record's
				// int16 +0, TARGET:, group 20 entry n and the condition from the damage; no structure object is built
				// until the plan's flyers-and-bases step, which brings its type record.
				break;
			case FlyerClass:
				var silhouette = _flyers is { Count: > 0 } flyers ? flyers[0] : null;
				canvas.Blit(silhouette, Rect.X0 + SilhouetteLeft, Rect.Y1 - (silhouette?.Rows ?? 0) - SilhouetteBottom);
				_labels[0].SetText(canvas, Text(TargetGroup, 0));
				_labels[1].SetText(canvas, Text(FlyerGroup, 0));
				SetCondition(canvas, -1, IsFriendly(subject), Damage(subject));
				break;
			default:
				_labels[1].Font = Font(Es1CockpitViews.CpBlue);
				_labels[0].SetText(canvas, Text(TargetGroup, 0));
				_labels[1].SetText(canvas, Text(UnknownGroup, 0));
				break;
		}
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MfdStatusScreen_Update</c> (<c>00033690</c>): the paint, then the rect copied from the page drawn
	/// to the page on the screen (<see cref="Es1Cockpit.DrawToCockpit"/> with 1).
	/// </summary>
	public override void Update() {
		Paint();
		Cockpit.DrawToCockpit(Rect);
	}

	/// <summary>
	/// <c>MfdStatusScreen_SetCondition</c> (<c>000336b0</c>)(screen, <paramref name="state"/>, <paramref name="friendly"/>,
	/// <paramref name="damage"/>) (docs/es1/simulation/mfd-status-screen.md#the-condition-line): a state of −1 taken from
	/// the damage (<see cref="Es1PaperDoll.ToConditionState"/>); label 3 group 24's entry for it; for a friend, label 4
	/// <c>[ </c>, (256 − damage) × 100 &gt;&gt; 8 in base 10, and <c>% ]</c>.
	/// </summary>
	public void SetCondition(Es1PanelCanvas canvas, short state, bool friendly, int damage) {
		if (state == -1) {
			state = Es1PaperDoll.ToConditionState(damage);
		}

		_labels[3].SetText(canvas, Text(ConditionGroup, state));
		if (friendly) {
			_labels[4].SetText(canvas, "[ " + ((0x100 - damage) * 100 >> 8).ToString(CultureInfo.InvariantCulture) + "% ]");
		}
	}

	/// <summary>
	/// The paint's HERC (docs/es1/simulation/mfd-status-screen.md#a-herc): view <see cref="DollView"/>'s picture of the
	/// chassis' paper doll blitted at the view's origin plus the rect's top-left; for any machine but the cockpit's own,
	/// <c>TARGET:</c> and the chassis name, group 19 by the type number, in <c>cpgreen</c> for a friend and <c>cpred</c>
	/// for a foe; <c>STATUS:</c>; each region of the view tinted by the mean of the readings its id selects
	/// (<see cref="RegionDamage"/>, <see cref="Es1PaperDoll.PickRegionTint"/>, <see cref="Es1PaperDoll.RecolorRect"/>);
	/// and the condition scanned: destroyed (4) when <c>+0x93</c> is set, else critical (3) when the least of readings 20
	/// to 31 is above <c>0x80</c>, internal damage (2) when the greatest is above 0, shields down (1) when <c>+0xaa</c> is
	/// set, OK (0) otherwise.
	/// </summary>
	private void PaintHerc(Es1PanelCanvas canvas, Es1Mech herc) {
		var doll = herc.Type.PaperDoll;
		var view = doll?.Entries is { } views && DollView < views.Length ? views[DollView] : null;
		int x = (view?.Origin.X ?? 0) + Rect.X0, y = (view?.Origin.Y ?? 0) + Rect.Y0;
		canvas.Blit(herc.Type.PaperDollPicture(DollView), x, y);
		var readings = herc.ReadDamageReadouts();
		if (!ReferenceEquals(herc, Cockpit.Machine)) {
			_labels[0].SetText(canvas, Text(TargetGroup, 0));
			_labels[1].Font = Font(IsFriendly(herc) ? Es1CockpitViews.CpGreen : Es1CockpitViews.CpRed);
			_labels[1].SetText(canvas, Text(ChassisGroup, herc.Type.Type));
		}

		_labels[2].SetText(canvas, Text(StatusGroup, 0));
		foreach (var region in view?.Regions ?? []) {
			var (_, tint) = Es1PaperDoll.PickRegionTint(region, RegionDamage(region.Index, readings));
			var rect = new Es1Rect(region.TopLeft.X + x, region.TopLeft.Y + y, region.BottomRight.X + x, region.BottomRight.Y + y);
			Es1PaperDoll.RecolorRect(canvas, region.RecolorMode, tint, region.ColorId, rect);
		}

		short state = 0;
		if (herc.Destroyed) {
			state = 4;
		} else {
			short least = 0x100, greatest = 0;
			for (int i = FirstScanned; i < FirstScanned + ScannedCount; i++) {
				least = Math.Min(least, readings[i]);
				greatest = Math.Max(greatest, readings[i]);
			}

			state = least > 0x80 ? (short)3 : greatest > 0 ? (short)2 : (short)0;
		}

		if (state == 0 && ShieldsDown(herc)) {
			state = 1;
		}

		SetCondition(canvas, state, IsFriendly(herc), Damage(herc));
	}

	/// <summary>
	/// The region's damage, the mean of the readings its id selects through the paint's jump table (<c>0009361c</c>): id 0
	/// readings 1 and 2; 4, 5 and 6 reading 5, 6 or 7; 7 readings 8, 10 and 12; 8 readings 9, 11 and 13; 13 readings 14,
	/// 16 and 18; 14 readings 15, 17 and 19. Any other id divides by a count of 0, which faults in retail; it reads 0
	/// here, an engine guard.
	/// </summary>
	private static int RegionDamage(int id, short[] readings) {
		int[] picked = id switch {
			0 => [1, 2],
			4 => [5],
			5 => [6],
			6 => [7],
			7 => [8, 10, 12],
			8 => [9, 11, 13],
			13 => [14, 16, 18],
			14 => [15, 17, 19],
			_ => [],
		};
		return picked.Length == 0 ? 0 : picked.Sum(i => readings[i]) / picked.Length;
	}

	/// <summary>The object's <c>+0xaa</c>, shields down (<see cref="Es1Mech.ShieldsMessagePosted"/>).</summary>
	private static bool ShieldsDown(Es1Mech herc) => herc.ShieldsMessagePosted;

	/// <summary>
	/// The <c>int16</c> slot <c>+0x44</c> of the object's vtable returns, its damage. PLACEHOLDER: the damage is plan step
	/// 7b's; until then it is 0, intact.
	/// </summary>
	private static int Damage(IEs1SimObject subject) {
		_ = subject;
		return 0;
	}

	/// <summary>
	/// Friend or foe: the side byte <c>+0x14</c> of the object's group <c>+0x3f</c> is 0 for a friend. Retail reads the
	/// group with no test; an object with none reads as a friend here, an engine guard.
	/// </summary>
	private static bool IsFriendly(IEs1SimObject subject) => (subject.Group?.Side ?? 0) == 0;

	private string? Text(int group, int entry) => _strings?.Text(group, entry);

	private HudFont? Font(int index) => index < _fonts.Count ? _fonts[index] : null;
}
