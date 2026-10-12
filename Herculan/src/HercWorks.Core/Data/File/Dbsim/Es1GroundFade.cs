namespace HercWorks.Core.Data.File.Dbsim;

/// <summary>
/// FILE - ES1's <c>dat\tex</c>, <c>dat\tex_lo</c> and <c>dat\tex_lo2</c>, in SIMVOL0.VOL: the distances at which the
/// textured ground steps to its next fade table, the file a world file's fifth string names
/// (docs/es1/rendering/ground-and-sky.md#loading-the-ground-texture). An <c>int32</c> count and that many <c>int32</c>.
/// Read by <see cref="Io.Transform.Dbsim.Es1GroundFadeTransformer"/>.
/// </summary>
public class Es1GroundFade {
	/// <summary>The distances in file order, in world units: in retail 15, ascending, 20000 to 112400 in <c>TEX.DAT</c>.</summary>
	public int[] Distances { get; set; } = [];
}
