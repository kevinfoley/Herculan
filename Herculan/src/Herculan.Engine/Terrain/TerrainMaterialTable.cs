using System.Buffers.Binary;
using Herculan.Engine.Content;

namespace Herculan.Engine.Terrain;

/// <summary>
/// One record from <c>dat\mat0</c>, the shared terrain material table DBSIM loads with every zone
/// heightmap, into <c>HeightGrid+0x121</c>: two little-endian <see cref="int"/>s. See
/// docs/retail/formats/zone-terrain.md#datmat0--the-material-table.
/// </summary>
/// <param name="Index">
/// The frame of the theater's terrain texture bank this material draws. It runs 0, 1, 2, … in file
/// order, so it reads like a self-index, but it is a frame index — see
/// docs/retail/rendering/terrain-texturing.md#mat0s-two-fields.
/// </param>
/// <param name="BlockShift">
/// How the frame is placed on a cell: zero stretches the whole frame across the quad, non-zero tiles
/// it at <c>1 &lt;&lt; (CellSizeShift + BlockShift - 13)</c> texels per cell, wrapping at 256 — see
/// docs/retail/rendering/terrain-texturing.md#mat0s-two-fields. Record 0's copy also sizes the
/// block of cells that shares one material roll at zone load
/// (docs/retail/simulation/terrain-heightmap.md#per-cell-record-16-bytes).
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
