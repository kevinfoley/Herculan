using HercWorks.Core.Data.File.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// How a machine's damage colours its <c>.PDG</c> paper doll and the rows beside it — the piece
/// shared by the MFD's STATUS and TARGET screens (<c>MfdStatusScreen_Paint</c>, <c>0043a5a0</c>) and
/// the Heads-Down Display's damage detail (<c>HddDamageScreen_Update</c>, <c>00450c54</c>).
///
/// <para>A region is not tinted as a block: the paint hands its rect, the colour the art drew that
/// body part in, and the colour it should now be to <c>PaperDoll_RecolorRect</c>, which walks the rect a pixel
/// at a time and rewrites only the pixels that still hold the first. That is why a doll recolours
/// limb by limb without disturbing the outlines drawn over it — see
/// <c>Render.Cockpit.PaperDollPainter.AddPaperDollTint</c> for the engine's copy of that walk, and
/// docs/retail/formats/mfd.md for the region record itself.</para>
///
/// <para>Both the key and the tint arrive as <see cref="HudColorTable"/> ids. The key is authored in
/// the <c>.PDG</c> and resolved to a palette index at load (<c>PaperDoll_Load</c>, <c>004379cc</c>);
/// the tint is one of the five ids below, which the original reads straight out of the same
/// table.</para>
/// </summary>
public static class PaperDollDamage {
	/// <summary>Tint for a component at 90% integrity or better — <c>COLORS.DAT</c> id 12, green.</summary>
	public const int OkColorId = 12;

	/// <summary>74% and up — id 15, yellow.</summary>
	public const int LightColorId = 15;

	/// <summary>51% and up — id 20, orange.</summary>
	public const int HeavyColorId = 20;

	/// <summary>Anything still standing — id 9, red.</summary>
	public const int CriticalColorId = 9;

	/// <summary>Gone — id 18, grey.</summary>
	public const int DestroyedColorId = 18;

	/// <summary>
	/// The damage detail's blue — id 6. The structural and internal views recolour the weapon icons'
	/// <see cref="OkColorId"/> pixels to it; the weapons view recolours the doll to it instead.
	/// </summary>
	public const int WeaponIconColorId = 6;

	/// <summary>The weapon-icon bank, <c>hba\WEAPONS.HBA</c>, whose frame sizes are <c>pdg\WEAPONS.PDG</c>.</summary>
	public const string WeaponIconBank = "WEAPONS";

	/// <summary>
	/// One weapon icon placed over the doll: its <see cref="WeaponIconBank"/> frame and its rect in
	/// device pixels relative to the view's origin, <see cref="X"/>/<see cref="Y"/> being where the
	/// frame is blitted. The recolour walks <c>X..X+Width</c> and <c>Y..Y+Height</c> inclusive.
	/// </summary>
	public readonly record struct WeaponIcon(int Frame, int X, int Y, int Width, int Height);

	/// <summary>
	/// <c>PaperDoll_BuildWeaponIcons</c> (<c>00437c8c</c>) for one <c>.PDG</c> hardpoint: frame
	/// <paramref name="iconIndex"/> plus the entry's own offset, anchored at its origin by its
	/// alignment. Arithmetic is in device pixels, as the original's is after the load-time shift, so
	/// a centred 9-pixel icon backs off 9 device pixels rather than 8. Null for a weapon with no icon
	/// or a frame <paramref name="sizes"/> has no size for.
	///
	/// <para>The entry's blit flags are not applied. Flag 2 moves the icon left by the bitmap's width
	/// less its <c>WEAPONS.PDG</c> width, but every retail hardpoint entry states 0.</para>
	/// </summary>
	public static WeaponIcon? PlaceWeaponIcon(PaperDollGraphic.HardpointEntry hardpoint, int iconIndex,
			WeaponPaperDiagram sizes, int scale) {
		int frame = iconIndex + hardpoint.FrameOffset;
		if (iconIndex < 0 || sizes.Entries is not { } entries || frame < 0 || frame >= entries.Length) {
			return null;
		}

		int width = entries[frame].Width * scale;
		int height = entries[frame].Height * scale;
		int x = hardpoint.Origin.X * scale;
		int y = hardpoint.Origin.Y * scale;

		x -= (hardpoint.Alignment & 7) switch {
			2 => width,
			4 => width >> 1,
			_ => 0,
		};
		y -= (hardpoint.Alignment & ~7) switch {
			0x10 => height,
			0x20 => height >> 1,
			_ => 0,
		};

		return new WeaponIcon(frame, x, y, width, height);
	}

	/// <summary>
	/// <c>Damage_PickRegionTint</c>'s ladder, in its own bands: one Q8 damage reading in, one of five states
	/// out. The bands are <see cref="MfdStatusSubject.ConditionFromDamage"/>'s, because they are
	/// literally the same four thresholds on the same integrity percentage — this function is the
	/// per-component reading of what that one says about a whole machine.
	/// </summary>
	public static int State(int damage) => MfdStatusSubject.ConditionFromDamage(damage);

	/// <summary>The <c>COLORS.DAT</c> id state <paramref name="state"/> tints a doll region with.</summary>
	public static int TintColorId(int state) => state switch {
		0 => OkColorId,
		1 => LightColorId,
		2 => HeavyColorId,
		3 => CriticalColorId,
		_ => DestroyedColorId,
	};

	/// <summary>
	/// The font a damage-detail row is written in — <c>ColorSchemePanels</c> 1, 3, 9, 2, 7 for the
	/// five states, which is the manual's own green / yellow / orange / red / grey. Both of a row's
	/// labels take it, so the name changes colour with its percentage.
	/// </summary>
	public static string RowFont(int state) => state switch {
		0 => "CPGREEN",
		1 => "CPYLW",
		2 => "CPORANGE",
		3 => "CPRED",
		_ => "CPGREY",
	};

	/// <summary>
	/// What one region of the MFD status screen's compact doll (<c>.PDG</c> view
	/// <see cref="MfdLayout.WireframeViewIndex"/>) reads, averaged over the armour components it
	/// covers. Null for a region id the paint's switch does not name — no retail file has one in this
	/// view, and the original would divide by zero on it.
	///
	/// <para>The compact view merges: its single torso region averages the two cockpit halves, and
	/// each of its four limbs averages the three armour components down that limb. A flyer-variant
	/// chassis — the RAZOR, the one retail type whose record sets
	/// <see cref="Sim.MechTypeRecord.IsFlyer"/> — has no such stacks and reads one component per
	/// region.</para>
	/// </summary>
	public static int? StatusRegionReading(int regionId, bool flyer, IReadOnlyList<short> readouts) {
		int A(int component) => readouts[ComponentDamage.FirstArmorReadout + component - 1];

		if (flyer) {
			return regionId switch {
				0 => A(1),
				4 => A(5),
				5 => A(6),
				6 => A(7),
				7 => A(8),
				8 => A(9),
				_ => null,
			};
		}

		return regionId switch {
			0 => (A(1) + A(2)) / 2,
			4 => A(5),
			5 => A(6),
			6 => A(7),
			7 => (A(8) + A(10) + A(12)) / 3,
			8 => (A(9) + A(11) + A(13)) / 3,
			13 => (A(14) + A(16) + A(18)) / 3,
			14 => (A(15) + A(17) + A(19)) / 3,
			_ => null,
		};
	}

	/// <summary>
	/// One damage-detail row's printed reading: armour component <c>1 + id</c> for the structural
	/// view, dependent <c>id</c> for the internal one. Rows are in the <c>.PDG</c> view's own region
	/// order, which is not the string table's — a pilotable chassis's internal view lists its regions
	/// 0,1,2,5,6,7,8,3,4,9 — and each region's id is the index into both the name group and this buffer.
	/// </summary>
	public static int RowReading(HddDamageView view, int regionId, IReadOnlyList<short> readouts) =>
		view == HddDamageView.Internal
			? readouts[ComponentDamage.FirstDependentReadout + regionId]
			: readouts[ComponentDamage.FirstArmorReadout + regionId];

	/// <summary>
	/// The reading behind a weapons-view row and its icon: the weapon mount's component and the
	/// dependent under it, weighed together. Mount <paramref name="loadoutSlot"/> is component
	/// <see cref="Sim.WeaponMounts.FirstMountComponent"/><c> + slot</c>, which is the buffer's
	/// <see cref="ComponentDamage.FirstCombinedReadout"/><c> + slot</c>.
	/// </summary>
	public static int? WeaponRowReading(int loadoutSlot, IReadOnlyList<short> readouts) =>
		loadoutSlot >= 0 && loadoutSlot < ComponentDamage.CombinedReadoutCount
			? readouts[ComponentDamage.FirstCombinedReadout + loadoutSlot]
			: null;

	/// <summary>
	/// The structural view's two cockpit halves share one region rect, so the row for region id 0
	/// tints on the average of both and the row for id 1 does not tint at all. Returns the reading the
	/// tint should use, which differs from the row's printed one only for those two.
	/// </summary>
	public static int? TintReading(HddDamageView view, PaperDollGraphic.ViewRegion[] regions, int row,
			bool flyer, IReadOnlyList<short> readouts) {
		int id = regions[row].Index;
		if (view != HddDamageView.Structural || flyer) {
			return RowReading(view, id, readouts);
		}

		return id switch {
			0 => (RowReading(view, 0, readouts) + RowReading(view, 1, readouts)) / 2,
			1 => null,
			_ => RowReading(view, id, readouts),
		};
	}
}

/// <summary>
/// One chassis hardpoint as the damage detail sees it: the name its weapons-view row prints and the
/// icon it draws around the doll.
/// </summary>
/// <param name="Name">The mount's own name, with none of the cockpit row's pod suffix.</param>
/// <param name="Icon">The weapon's <see cref="WeaponMount.DamageIcon"/>.</param>
public readonly record struct DamageHardpoint(string Name, int Icon) {
	/// <summary>
	/// A machine's hardpoints indexed by <see cref="WeaponMount.LoadoutSlot"/> — the <c>.GL</c> slot
	/// byte that <c>.PDG</c> hardpoint entry <c>n</c>, the weapons view's row <c>n</c> and combined
	/// readout <c>n</c> all name. One entry per slot of the mount array; an empty hardpoint is null.
	/// </summary>
	public static IReadOnlyList<DamageHardpoint?> Build(WeaponMounts mounts) {
		var list = new DamageHardpoint?[mounts.Slots.Count];
		foreach (var mount in mounts.Mounts) {
			if (mount.LoadoutSlot >= 0 && mount.LoadoutSlot < list.Length) {
				list[mount.LoadoutSlot] = new DamageHardpoint(mount.Name, mount.DamageIcon);
			}
		}

		return list;
	}
}
