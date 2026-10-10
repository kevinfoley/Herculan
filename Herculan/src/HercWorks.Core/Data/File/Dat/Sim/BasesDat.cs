namespace HercWorks.Core.Data.File.Dat.Sim;

/// <summary>
/// FILE - /SIMVOL0/DAT/BASES.DAT — the structure type table: an <c>int16</c> count, then one
/// <see cref="BaseTypeRecord"/> per type with its components inline. A mission's row-14 type field
/// indexes it. 65 types in retail. See docs/retail/formats/bases-dat.md.
/// </summary>
public class BasesDat {
	public BaseTypeRecord[] Types { get; set; } = [];
}

/// <summary>
/// One structure type. The offsets are the runtime record's, which <c>Base_LoadResources</c>
/// (<c>00405fac</c>) fills in file order; on disk the component array sits inline where the runtime
/// record holds a pointer at <c>+0x14</c> (docs/retail/formats/bases-dat.md#the-type-record).
/// </summary>
public class BaseTypeRecord {
	/// <summary>+0x00 — not read by anything traced.</summary>
	public short Unk00 { get; set; }

	/// <summary>+0x02 — the shape's index in the library the type draws from.</summary>
	public short ShapeIndex { get; set; }

	/// <summary>+0x04 — the wreck type in <c>dgs\BHULKS.DGS</c>, <c>-1</c> for none.</summary>
	public short HulkTypeIndex { get; set; }

	/// <summary>+0x06 — how many animation threads the constructor builds; non-zero on the eight types drawn from <c>dts\BASES_AN.DTS</c>.</summary>
	public short AnimThreadCount { get; set; }

	/// <summary>+0x08 — the whole-structure fire shape, <c>-1</c> for none.</summary>
	public short FireShapeIndex { get; set; }

	/// <summary>+0x0A — where that fire sits, X/Y/Z in the structure's own frame.</summary>
	public short[] FirePoint { get; set; } = new short[3];

	/// <summary>+0x10 — the whole-structure death sequence.</summary>
	public short DestroyedEffect { get; set; }

	/// <summary>+0x12 count, +0x14 array — the destructible parts, inline on disk.</summary>
	public BaseComponentRecord[] Components { get; set; } = [];

	/// <summary>+0x18 — 6 bytes not read by anything traced.</summary>
	public short[] Unk18 { get; set; } = new short[3];

	/// <summary>+0x1E — non-zero: the type takes no damage.</summary>
	public short Invulnerable { get; set; }

	/// <summary>+0x20 — the playback rate of each animation thread.</summary>
	public short[] AnimThreadRates { get; set; } = new short[2];

	/// <summary>+0x24 — the idle cell-flipbook sequence, <c>-1</c> for none.</summary>
	public short AnimCellSequence { get; set; }

	/// <summary>+0x26 — that flipbook's frame interval.</summary>
	public short AnimCellInterval { get; set; }

	/// <summary>
	/// +0x28 — the MFD silhouette frame and type-name index: <c>STRINGS0.STR</c> group 23 when
	/// <see cref="TextureSelector"/> is 0, group 24 when it is not
	/// (docs/retail/simulation/mfd.md#viewport-and-condition-per-class).
	/// </summary>
	public short SilhouetteIndex { get; set; }

	/// <summary>+0x2A — the body radius.</summary>
	public short HitRadius { get; set; }

	/// <summary>+0x2C — how far up the structure a shooter aims.</summary>
	public short AimPointHeight { get; set; }

	/// <summary>+0x2E — what the type shoots: 0 nothing, 1 a gun, 2 a launcher.</summary>
	public short Armament { get; set; }

	/// <summary>+0x30 — non-zero installs the type's <c>BASECOL.DAT</c> model.</summary>
	public short CollisionModel { get; set; }

	/// <summary>+0x32 — the texture bank selector; non-zero on the vehicle types.</summary>
	public short TextureSelector { get; set; }
}

/// <summary>One destructible part of a structure type, 30 bytes (docs/retail/formats/bases-dat.md#the-component-record-30-bytes).</summary>
public class BaseComponentRecord {
	/// <summary>+0 — the damage the part absorbs before it is destroyed.</summary>
	public short MaxDamage { get; set; }

	/// <summary>+2 — the cell-animation sequence the part drives, <c>-1</c> for none.</summary>
	public short DestroyedSubShape { get; set; }

	/// <summary>+4 — the death sequence the part runs, <c>-1</c> for none.</summary>
	public short DestroyedEffect { get; set; }

	/// <summary>+6 — the fire shape the part burns, <c>-1</c> for none.</summary>
	public short FireShapeIndex { get; set; }

	/// <summary>+8 — the debris group the part throws.</summary>
	public short DebrisGroup { get; set; }

	/// <summary>+0x0A — where the debris and fire come from, X/Y/Z.</summary>
	public short[] EmitPoint { get; set; } = new short[3];

	/// <summary>+0x10 — the part's position in the structure's frame, X/Y/Z.</summary>
	public short[] Position { get; set; } = new short[3];

	/// <summary>+0x16 — half-extents of the box the death sequence scatters smoke in, X/Y/Z.</summary>
	public short[] SmokeSpread { get; set; } = new short[3];

	/// <summary>+0x1C — the part this one hangs off.</summary>
	public short ParentComponent { get; set; }
}
