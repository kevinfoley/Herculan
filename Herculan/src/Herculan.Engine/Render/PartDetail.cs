using System.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// One <c>TSDetailPart</c>'s LODs and the table that picks between them —
/// <c>TSDetailPart_Render</c> (<c>004768bc</c>), which chooses a level every time the part is drawn
/// from its projected size on screen. See docs/retail/rendering/dts-texture-binding.md, "TSDetailPart level
/// selection and STRUCTURE DETAIL".
///
/// <para>The sibling one level up is <see cref="ShapeDetail"/>, which picks a machine's whole root
/// the same way but walks a descending table where this one walks an ascending one.</para>
///
/// <para>One instance per <c>TSDetailPart</c> in a built shape, shared by every piece
/// <see cref="DtsMeshBuilder"/> gates on it — see <see cref="CellGate.Detail"/>.</para>
/// </summary>
/// <param name="radius">The part's own <c>TSBasePart.Radius</c> (<c>part+8</c>), in world units.</param>
/// <param name="thresholds">The part's <c>Details</c>, ascending, one per level.</param>
/// <param name="levelCount">How many levels the part holds — <c>part+0x10</c>.</param>
/// <param name="origin">
/// Where the part's own node sits in the shape at rest, in render units. The original measures the
/// distance to the view-space translation the part's node transform installs
/// (<c>TSGroup_BindNodeTransform</c> ahead of the measure), not to the object's origin.
/// </param>
public sealed class PartDetail(short radius, short[] thresholds, int levelCount, Vector3 origin) {
	/// <summary>
	/// <c>g_TSDetailPartSizeScaleQ10</c> (<c>004a1034</c>) — Q10 one, the image's value. Whether
	/// anything changes it at run time is open; see the doc.
	/// </summary>
	public const int SizeScaleQ10 = 1024;

	/// <summary>
	/// The bias each STRUCTURE DETAIL setting selects — <c>g_StructureDetailValues</c>
	/// (<c>0049f02c</c>) through <c>StructureDetail_ApplySetting</c> (<c>0045d4f0</c>), whose keys are
	/// the identity over the three settings. LOW is 2 and MAXIMUM 0.
	/// </summary>
	private static readonly int[] StructureBiasBySetting = { 2, 1, 0 };

	/// <summary>
	/// The bias <c>Structure_DrawWithDetailBias</c> (<c>004034f4</c>) and <c>Flyer_Draw</c>
	/// (<c>004215cc</c>) push for STRUCTURE DETAIL setting <paramref name="setting"/>. A byte past 2
	/// matches no key in the original's walk and leaves the bias as it was, which from startup is its
	/// image value of 0; that is what it gives here.
	/// </summary>
	public static int StructureBias(int setting) =>
		setting >= 0 && setting < StructureBiasBySetting.Length ? StructureBiasBySetting[setting] : 0;

	/// <summary>
	/// The bias each HERC DETAIL setting selects — <c>g_HercDetailTSDetailBiasValues</c>, written to
	/// <c>g_TSDetailBiasFromHercDetail</c> (<c>004a98ec</c>) by
	/// <c>ShapeDetail_ApplyHercDetailSetting</c> (<c>0045d474</c>) beside the root bias
	/// <see cref="ShapeDetail.BiasFor"/> reads out of the same walk.
	/// </summary>
	private static readonly int[] HercBiasBySetting = { 2, 2, 1, 1, 0 };

	/// <summary>
	/// The bias a weapon mount's draw (<c>WeaponMount_RenderWithDetailBias</c> (<c>0040ded8</c>)) and a gun shot off its mount
	/// (<c>Debris_Draw</c>) push for HERC DETAIL setting <paramref name="setting"/>, clamped to the
	/// table as <see cref="ShapeDetail.BiasFor"/> is.
	/// </summary>
	public static int HercBias(int setting) =>
		HercBiasBySetting[Math.Clamp(setting, 0, HercBiasBySetting.Length - 1)];

	/// <summary>The part's own bounding radius, in world units.</summary>
	public short Radius { get; } = radius;

	/// <summary>The part's ascending size thresholds, in projected pixels.</summary>
	public IReadOnlyList<short> Thresholds { get; } = thresholds;

	/// <summary>How many alternates the part holds; level 0 is the coarsest.</summary>
	public int LevelCount { get; } = levelCount;

	/// <summary>Where the part's own node sits in the shape at rest, in render units.</summary>
	public Vector3 Origin { get; } = origin;

	/// <summary>
	/// The level drawn at <paramref name="viewDistance"/>, in <c>[0, LevelCount)</c>.
	///
	/// <para>The walk starts at <paramref name="bias"/> in the threshold table and advances while the
	/// projected size is past the entry; the level drawn is how far it got <i>less</i> the bias. So a
	/// larger bias both starts the comparison further up the table and subtracts that start back off —
	/// the whole scale moves toward the coarse end, and at bias 0 a close part reaches its finest
	/// level.</para>
	/// </summary>
	public int Select(int viewDistance, int focalLengthPixels, int bias) {
		if (LevelCount <= 1) {
			return 0;
		}

		int size = Numerics.SimMath.Q10Multiply(SizeScaleQ10,
			ShapeDetail.ProjectedSize(Radius, viewDistance, focalLengthPixels));
		int i = Math.Max(bias, 0);

		// Bounded by the table as well as by the level count, which the original is not; every retail
		// part carries exactly one threshold per level, so the guard changes nothing on that data.
		while (i < LevelCount - 1 && i < Thresholds.Count && Thresholds[i] < size) {
			i++;
		}

		return Math.Clamp(i - Math.Max(bias, 0), 0, LevelCount - 1);
	}
}
