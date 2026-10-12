namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - ES1's <c>dba\trenches.64k</c> and <c>dba\cracks.64k</c>, in SIMVOL0.VOL: the 256 × 256 pattern the textured
/// ground tiles, one palette index a byte, row by row (docs/es1/rendering/ground-and-sky.md#loading-the-ground-texture).
/// <c>GndTex_LoadTexture</c> (<c>ES1DBSIM 00029830</c>) reads the file's first 65,536 bytes. Read by
/// <see cref="Io.Transform.Dbsim.Es1GroundTextureTransformer"/>.
/// </summary>
public class Es1GroundTexture {
	/// <summary>Texels along a row, and rows.</summary>
	public const int Size = 256;

	/// <summary>
	/// <see cref="Size"/> rows of <see cref="Size"/> palette indices. The draw reads row <c>v</c>, column <c>u</c> at byte
	/// <c>v · 256 + u</c>, the rows running along world y (docs/es1/rendering/ground-and-sky.md#the-textured-ground).
	/// </summary>
	public byte[] Texels { get; set; } = new byte[Size * Size];
}
