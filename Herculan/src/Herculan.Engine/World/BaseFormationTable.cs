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
/// the other six carry <c>-1</c> and no map, and their groups leave the ground alone.
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
/// <c>dat\BFORMS.DAT</c> — per-formation spread offsets for a <c>script.dat</c> block-11 base/
/// structure group. This is what stops a multi-structure group (a fortress cluster, a turret ring)
/// from placing every member on the group's single point.
///
/// <para>Read from the base-group-attach chain: <c>DBSim_BuildGroupRecord</c> (<c>00423b34</c>)
/// carries the group's formation id (script.dat block-11's <c>FormationId</c> field, raw msn
/// offset <c>0x30</c> — see <c>docs/retail/formats/msn-mission-file.md</c>'s row #16 decode) into
/// <c>Base_AttachToGroup</c> (<c>00405c3c</c>), which stores it at the attached object's group-relative member index
/// (<c>+0x49</c>, the object's position within the group's <c>MemberRefs</c> array — <b>not</b>
/// a compacted live-member count) and then unconditionally calls the object's own vtable
/// <c>+0x78</c>. For every base subtype's vtable that slot is <c>Base_ApplyFormationOffset</c> (<c>00405c04</c>), which — when the
/// member index is nonzero, i.e. every member but the group's first-claimed ("leader") slot — looks
/// up this table's <c>(formationId, memberIndex-1)</c> entry via <c>BaseFormation_GetSlotOffset</c> (<c>00405b9c</c>) and rotates it
/// into world space by the leader's heading (<c>Formation_RotateAndAddOffset</c>, <c>00411d64</c>)
/// before adding it to the group's position — mirrors <c>Mech_ApplyFormationOffset</c>
/// (<c>00417898</c>): same vtable slot, same "member index 0 gets no offset" rule. Load site:
/// <c>Base_LoadResources</c> (<c>00405fac</c>) streams the table from a file literally named <c>"bforms"</c>. Byte-exact: the
/// retail file is 3,186 content bytes, formation count 17 (matches block-11 formation id's 0-16
/// range), formation 0's seven offsets a symmetric wedge — one point ahead, three mirrored pairs
/// behind.</para>
///
/// <para><b>A slot turns its structure as well as placing it</b> — see
/// <see cref="HeadingNudgeFor"/>. That half arrives by a different path (<c>Base_AttachToGroup</c>
/// itself, not the vtable slot above) and was missed when this table was first read, which left one
/// structure of a group standing in the right place facing the wrong way.</para>
///
/// <para><b>The trailer describes the formation's ground, and where the group stands on it</b> —
/// see <see cref="BaseFormationLayout"/>. When the block-11 record's own <c>PaintsGround</c> (raw msn
/// offset <c>0x06</c>) is set, the group's shared anchor is moved to that formation's fixed
/// position within its terrain tile before any per-member offset above is added
/// (<see cref="BaseFormationLayout.SnapAnchor"/>), and the same tile is painted with the
/// formation's material by <see cref="Herculan.Engine.Terrain.HeightGrid.PaintFormationPad"/>.
/// The two go together: the move is what puts the structures on the pad. Trailer layout, the
/// derivation and the evidence are in docs/retail/formats/script-dat.md, "Base formation terrain".</para>
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
	/// <para><b>This is not the same thing as the spread offset</b>, and missing it is why one
	/// structure of a group could stand at the right spot facing the wrong way.
	/// <c>Base_AttachToGroup</c> (<c>00405c3c</c>) fills a structure's heading only when its own
	/// record names none (the <c>-0x8000</c> sentinel — a block-9 record whose heading ref is -1),
	/// and when it does, it adds this slot's turn on top of the group's:</para>
	/// <code>
	/// h = group.heading;
	/// if (slot != 0) h += formation.slots[slot - 1].headingNudge;   // +405c9a, the slot's trailing int16
	/// object.heading = (short)h;
	/// </code>
	/// <para>Note this is applied at <i>attach</i> time and is entirely separate from
	/// <c>Base_ApplyFormationOffset</c>, which reads the same slot record's two <c>int32</c>s and only
	/// moves the structure. The heading is a short in the original, so the sum wraps.</para>
	///
	/// <para>Confirmed on the Scramble training base: its group uses formation 9, whose slots 6 and 8
	/// carry 16380 (90 degrees) and 32760 (180). Roster slots 6 and 8 of that group are two of the
	/// three identical silo-cluster structures, and in retail they stand turned by exactly those
	/// amounts while the third does not.</para>
	/// </summary>
	public short HeadingNudgeFor(int formationId, int memberIndex) =>
		OffsetFor(formationId, memberIndex)?.HeadingNudge ?? 0;

	/// <summary>
	/// This formation's terrain layout, or null when it declares none — the six retail formations
	/// whose material index is <c>-1</c>, and any id outside the table.
	/// </summary>
	public BaseFormationLayout? LayoutFor(int formationId) =>
		formationId >= 0 && formationId < _layouts.Length ? _layouts[formationId] : null;

	public static BaseFormationTable Load(GameContent content) {
		byte[] bytes = content.ReadRequired(ResourceFolder, ResourceName);
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
