using System.Buffers.Binary;
using Herculan.Engine.Content;

namespace Herculan.Engine.World;

/// <summary>
/// One follower slot of a base formation: an unrotated (x, y) spread offset, and how far the
/// structure standing there is turned relative to the rest of its group.
/// </summary>
/// <param name="HeadingNudge">
/// Binary-angle turn added to the group's heading for this slot — the trailing <c>int16</c> of the
/// 10-byte slot record. Every nonzero value in the retail table is a clean quarter turn: 8190 (45
/// degrees), 16380 (90), 32760 (180) or their negatives. See
/// <see cref="BaseFormationTable.HeadingNudgeFor"/>.
/// </param>
public readonly record struct BaseFormationOffset(int X, int Y, short HeadingNudge);

/// <summary>
/// A formation's terrain layout: which <c>dat\mat0</c> material its ground is painted with, and the
/// square occupancy map that accompanies it. Eleven of the seventeen retail formations carry one;
/// the other six carry <c>-1</c> and no map, and their groups leave the ground alone. See
/// docs/retail/formats/script-dat.md#the-per-formation-trailer.
/// </summary>
/// <param name="MaterialIndex">
/// Index into <c>dat\mat0</c>, whose <c>Index</c> field is the theater bank frame that gets drawn —
/// one of the eleven pad layouts in frames 2-12. Each mapped formation uses a distinct one.
/// </param>
/// <param name="Dimension">
/// The map's side in entries, 8 or 16 in retail data. It equals the tile the material's own
/// <c>BlockShift</c> covers, so the map spans exactly the painted area.
/// </param>
/// <param name="AnchorFractionX">
/// Where the group's anchor sits within the tile, in 256ths of it, measured from the low-x edge.
/// </param>
/// <param name="AnchorFractionY">
/// The same, measured <i>down</i> from the tile's high-y edge — the axis inversion that matches
/// <see cref="Map"/> being stored top row first.
/// </param>
/// <param name="Map">
/// <see cref="Dimension"/> rows of <see cref="Dimension"/> 0/1 bytes, row 0 at the tile's high-y
/// edge. Not read by the painting itself, which covers the whole tile; it marks which of that
/// ground is also levelled.
/// </param>
public sealed record BaseFormationLayout(int MaterialIndex, int Dimension,
		int AnchorFractionX, int AnchorFractionY, byte[] Map) {
	/// <summary>
	/// The tile this formation's material and map both span, in world units — always
	/// <c>1 &lt;&lt; (0x15 - BlockShift)</c>, and independent of the zone's cell size.
	/// </summary>
	public int TileSize => Dimension << 13;

	/// <summary>
	/// Where the group's shared anchor actually sits, given the point the mission placed it on.
	/// Only which tile that point falls in survives: the position within the tile is replaced by
	/// this formation's own fixed fraction of it, which is what lines the structures up with the
	/// pad painted over the same tile.
	///
	/// <para>The original computes this per member inside <c>Base_AttachToGroup</c>
	/// (<c>00405c3c</c>) on a copy of the group's point, as
	/// <c>(x &amp; ~mask) + b*step</c> / <c>((y &amp; ~mask) + mask + 1) - c*step</c> with
	/// <c>mask = dim*0x2000 - 1</c> and <c>step = dim*0x20</c> — the same arithmetic as below, since
	/// <c>mask + 1</c> is the tile and <c>step</c> is a 256th of it.</para>
	/// </summary>
	public (int X, int Y) SnapAnchor(int worldX, int worldY) {
		int tile = TileSize;
		int step = tile >> 8;

		return ((worldX & ~(tile - 1)) + AnchorFractionX * step,
			(worldY & ~(tile - 1)) + tile - AnchorFractionY * step);
	}
}

/// <summary>
/// <c>dat\BFORMS.DAT</c> — per-formation spread offsets for a <c>script.dat</c> block-11 structure
/// group, so a multi-structure group (a fortress cluster, a turret ring) does not stand every
/// member on the group's single point.
///
/// <para>Loaded by <c>Base_LoadResources</c> (<c>00405fac</c>) and read through
/// <c>Base_ApplyFormationOffset</c> (<c>00405c04</c>), every structure class's vtable <c>+0x78</c>:
/// each member but the one in member slot 0 takes its slot's offset (<see cref="OffsetFor"/>),
/// rotated by the leader's heading. A slot also turns its structure (<see cref="HeadingNudgeFor"/>).
/// See docs/retail/formats/script-dat.md#placement--the-actual-rule.</para>
///
/// <para>Each formation's trailer describes the ground it paints and where the group stands on it
/// (<see cref="BaseFormationLayout"/>). When the group's <c>PaintsGround</c> is set, its anchor is
/// moved onto the formation's spot in its terrain tile (<see cref="BaseFormationLayout.SnapAnchor"/>)
/// before any member offset is added, and the tile is painted with the formation's material
/// (<see cref="Herculan.Engine.Terrain.HeightGrid.PaintFormationPad"/>). See
/// docs/retail/formats/script-dat.md#base-formation-terrain.</para>
/// </summary>
public sealed class BaseFormationTable {
	/// <summary>VOL folder and name of the table.</summary>
	public const string ResourceFolder = "dat";

	/// <summary>The table's resource name.</summary>
	public const string ResourceName = "BFORMS.DAT";

	private readonly BaseFormationOffset[][] _formations;
	private readonly BaseFormationLayout?[] _layouts;

	private BaseFormationTable(BaseFormationOffset[][] formations, BaseFormationLayout?[] layouts) {
		_formations = formations;
		_layouts = layouts;
	}

	/// <summary>How many formations the table declares.</summary>
	public int Count => _formations.Length;

	/// <summary>
	/// The spread offset for a group's <paramref name="memberIndex"/>-th member (its position within
	/// the claiming block-11 record's <c>MemberRefs</c> array), or null when the slot takes no
	/// offset — member index 0 (the group's first-claimed member), an out-of-range formation id, or a
	/// formation with fewer follower slots than this index needs.
	/// </summary>
	public BaseFormationOffset? OffsetFor(int formationId, int memberIndex) {
		if (memberIndex <= 0 || formationId < 0 || formationId >= _formations.Length) {
			return null;
		}

		var slots = _formations[formationId];
		int slotIndex = memberIndex - 1;
		return slotIndex < slots.Length ? slots[slotIndex] : null;
	}

	/// <summary>
	/// How far this slot's structure is turned relative to its group, in binary-angle units. Zero for
	/// the group's first-claimed member and for any slot the table does not reach.
	///
	/// <para><c>Base_AttachToGroup</c> (<c>00405c3c</c>) adds it to the group's heading, wrapping as a
	/// short, for a structure whose own record names no heading. It is separate from the spread
	/// offset, which only moves the structure. See
	/// docs/retail/formats/script-dat.md#placement--the-actual-rule.</para>
	/// </summary>
	public short HeadingNudgeFor(int formationId, int memberIndex) =>
		OffsetFor(formationId, memberIndex)?.HeadingNudge ?? 0;

	/// <summary>
	/// This formation's terrain layout, or null when it declares none — the six retail formations
	/// whose material index is <c>-1</c>, and any id outside the table.
	/// </summary>
	public BaseFormationLayout? LayoutFor(int formationId) =>
		formationId >= 0 && formationId < _layouts.Length ? _layouts[formationId] : null;

	public static BaseFormationTable Load(GameContent content) => Parse(content.ReadRequired(ResourceFolder, ResourceName));

	/// <summary>The table out of its bytes.</summary>
	public static BaseFormationTable Parse(byte[] bytes) {
		int offset = 0;

		int NextInt32() {
			int value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
			offset += 4;
			return value;
		}

		int formationCount = NextInt32();
		var formations = new BaseFormationOffset[formationCount][];
		var layouts = new BaseFormationLayout?[formationCount];

		for (int f = 0; f < formationCount; f++) {
			int slotCount = NextInt32();
			var slots = new BaseFormationOffset[slotCount];
			for (int s = 0; s < slotCount; s++) {
				int x = NextInt32();
				int y = NextInt32();
				short nudge = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(offset));
				offset += 2;
				slots[s] = new BaseFormationOffset(x, y, nudge);
			}
			formations[f] = slots;

			// The material index, then where in its tile the group's anchor sits.
			int materialIndex = NextInt32();
			int anchorFractionX = NextInt32();
			int anchorFractionY = NextInt32();

			// The formation's layout map: `bufferCount` rows of `bufferCount` 0/1 bytes (8x8 or
			// 16x16 in retail data), or nothing at all for the six formations that carry none —
			// exactly the six whose material index is -1.
			int bufferCount = NextInt32();
			var map = new byte[bufferCount * bufferCount];
			for (int b = 0; b < bufferCount; b++) {
				byte length = bytes[offset];
				offset++;
				if (length != bufferCount) {
					throw new InvalidDataException(
						$"{ResourceFolder}\\{ResourceName}: formation {f} row {b} is {length} bytes " +
						$"across {bufferCount} rows — the layout map is not square.");
				}

				bytes.AsSpan(offset, length).CopyTo(map.AsSpan(b * bufferCount));
				offset += length;
			}

			layouts[f] = materialIndex >= 0 && bufferCount > 0
				? new BaseFormationLayout(materialIndex, bufferCount, anchorFractionX,
					anchorFractionY, map)
				: null;
		}

		if (offset != bytes.Length) {
			throw new InvalidDataException(
				$"{ResourceFolder}\\{ResourceName}: walked {offset} of {bytes.Length} bytes across " +
				$"{formationCount} formations — the record shape does not match this file.");
		}

		return new BaseFormationTable(formations, layouts);
	}
}
