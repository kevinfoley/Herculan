using System.Buffers.Binary;
using Herculan.Engine.Content;

namespace Herculan.Engine.Terrain;

/// <summary>
/// One record from <c>dat\mat0</c> — the shared (not per-zone) terrain material/detail-type table
/// DBSIM loads alongside every zone heightmap, into <c>HeightGrid+0x121</c>. Records are 8 bytes:
/// two little-endian <see cref="int"/>s.
/// </summary>
/// <param name="Index">
/// First field. Runs 0, 1, 2, ... in file order in the real <c>MAT0.DAT</c>, so it reads like a
/// self-index — but it is <b>a frame index into the zone's terrain texture bank</b>.
/// <c>Terrain_ResolveCellTexture</c> uses it as <c>descriptorTable[Index * 0x14]</c> against the
/// same 20-byte-per-frame descriptor table the mech texture path uses. See
/// docs/retail/formats/terrain-texturing.md.
/// </param>
/// <param name="BlockShift">
/// Second field, with two distinct consumers.
///
/// <para><b>At load:</b> <c>TerrainZone_PopulateFromBitmap</c> reads record 0's copy to size the
/// block over which one material roll is shared —
/// <c>blockMask = (1 &lt;&lt; (0x15 - BlockShift - CellShift)) - 1</c>. With the retail values
/// (record 0's BlockShift = 6, CellShift = 14) that is a 2x2-cell block.</para>
///
/// <para><b>At render:</b> it selects how the frame is placed on a cell. Zero means the frame's own
/// UV corners are used verbatim (stretched across the quad). Non-zero tiles it:
/// <c>shift = CellShift + BlockShift - 13</c>, then the cell's rect is
/// <c>u0 = (cellX &lt;&lt; shift) &amp; 0xff</c>, <c>v0 = (cellY &lt;&lt; shift) &amp; 0xff</c>,
/// spanning <c>1 &lt;&lt; shift</c> texels, V negated. The <c>&amp; 0xff</c> is a 256-texel wrap —
/// which is why terrain banks ship as 256x256 and must be edge-tileable.</para>
/// </param>
public readonly record struct TerrainMaterial(int Index, int BlockShift);

/// <summary>
/// Loads and holds <c>dat\mat0</c>: a count-prefixed array of <see cref="TerrainMaterial"/>
/// records. Confirmed against the real retail <c>ES2/VOL/simvol0/dat/MAT0.DAT</c> (13 records,
/// followed by a trailing block this table's own consumer never reads — see <see cref="Load"/>).
/// </summary>
public sealed class TerrainMaterialTable {
	/// <summary>Resource folder and name the original builds for this table (<c>Mat0ResourceName</c>).</summary>
	public const string ResourceFolder = "dat";
	public const string ResourceName = "MAT0.DAT";

	private readonly TerrainMaterial[] _materials;

	private TerrainMaterialTable(TerrainMaterial[] materials) {
		_materials = materials;
	}

	public int Count => _materials.Length;

	public TerrainMaterial this[int index] => _materials[index];

	/// <summary>
	/// Reads the table out of mounted content. The file continues past the declared record count
	/// with a second, differently-shaped block; <c>TerrainZone_LoadHeightmap</c> reads exactly
	/// <c>count</c> 8-byte records and stops, so this does too rather than guessing at the tail.
	/// </summary>
	public static TerrainMaterialTable Load(GameContent content) {
		byte[] bytes = content.ReadRequired(ResourceFolder, ResourceName);

		int count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0));
		if (count < 0 || 4 + count * 8 > bytes.Length) {
			throw new InvalidDataException(
				$"{ResourceFolder}\\{ResourceName} declares {count} material records but is only {bytes.Length} bytes.");
		}

		var materials = new TerrainMaterial[count];
		for (int i = 0; i < count; i++) {
			int offset = 4 + i * 8;
			materials[i] = new TerrainMaterial(
				BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset)),
				BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + 4)));
		}

		return new TerrainMaterialTable(materials);
	}
}
