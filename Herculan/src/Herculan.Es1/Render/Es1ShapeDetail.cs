using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Es1.Sim;

namespace Herculan.Es1.Render;

/// <summary>
/// What preferences option 6 sets, as <c>ShapeDetail_ApplyDetailSetting</c> (<c>ES1DBSIM 00057c08</c>) copies it from the
/// normal tables (docs/es1/simulation/object-classes.md#the-detail-setting--shapedetail_applydetailsetting-00057c08): the
/// LOD scale, the first LOD root and the fitted weapons' detail-part bias. The low-memory tables are not taken: the
/// engine runs as a machine with the memory <c>g_LowMemory</c> asks for (see <see cref="Es1MechType"/>). The structures'
/// and flyers' bias is not carried: neither class is built yet.
/// </summary>
/// <param name="SizeScaleQ10"><c>g_ShapeDetailSizeScaleQ10</c> (<c>000903e0</c>).</param>
/// <param name="RootBias"><c>g_ShapeDetailBias</c> (<c>000903e2</c>).</param>
/// <param name="MountBias"><c>g_TSDetailBiasForMounts</c> (<c>000bc2b8</c>).</param>
public readonly record struct Es1DetailSetting(int SizeScaleQ10, int RootBias, int MountBias) {
	/// <summary><c>ShapeDetail_ScaleKeys</c> (<c>00096bc4</c>) and <c>ShapeDetail_BiasKeys</c> (<c>00096bdc</c>), which hold the same five.</summary>
	private static readonly int[] Keys = [50, 100, 150, 200, 300];

	/// <summary><c>ShapeDetail_ScaleValues</c> (<c>00096bd0</c>).</summary>
	private static readonly int[] ScaleValues = [2000, 2000, 2000, 2000, 2000];

	/// <summary><c>ShapeDetail_RootBiasValues</c> (<c>00096be8</c>).</summary>
	private static readonly int[] RootBiasValues = [4, 3, 2, 1, 0];

	/// <summary><c>ShapeDetail_MountBiasValues</c> (<c>00096bf4</c>).</summary>
	private static readonly int[] MountBiasValues = [2, 2, 1, 1, 0];

	/// <summary>
	/// The outputs for option value <paramref name="value"/>: the first key it is at most picks the slot. A byte is never
	/// above the last key, 300, so every value picks one.
	///
	/// <para>With no <c>data\prefs.cfg</c> (<paramref name="value"/> null) the option is 0: <c>Prefs_Load</c>
	/// (<c>0004a744</c>) zeroes all 17 options when the file does not open
	/// (docs/es1/simulation/mech-locomotion.md#the-throttle--mech_applythrottleinput-00011afc).</para>
	/// </summary>
	public static Es1DetailSetting FromOption(byte? value) {
		byte option = value ?? 0;
		int slot = Array.FindIndex(Keys, key => option <= key);
		return new Es1DetailSetting(ScaleValues[slot], RootBiasValues[slot], MountBiasValues[slot]);
	}
}

/// <summary>
/// <c>Shape_DrawAtDetailLevel</c>'s (<c>ES1DBSIM 00002b30</c>) root choice, by the steps of
/// docs/es1/simulation/object-classes.md#the-lod-root--shape_drawatdetaillevel-00002b30.
/// </summary>
public static class Es1ShapeDetail {
	/// <summary>The main view's <c>Raster_FocalShift</c>.</summary>
	public const int FocalShift = 8;

	/// <summary>
	/// The root drawn for a shape of root-0 radius <paramref name="radius"/> whose eye is <paramref name="distance"/>
	/// away (<see cref="EyeDistance"/> in the model's frame): the projected size, ES1's rounding multiply by the scale, and
	/// the walk from the bias down the thresholds. The threshold cursor is also bounded by the table, which retail's is
	/// not; no retail root count reaches past it.
	/// </summary>
	public static int SelectRoot(int rootCount, int radius, int distance, Es1DetailSetting setting,
			IReadOnlyList<int> thresholds) {
		if (rootCount <= 0) {
			return 0;
		}

		int size = ShapeDetail.ProjectedSize(radius, distance, 1 << FocalShift);
		int scaled = SimMath.Q10MultiplyRounded(size, (short)setting.SizeScaleQ10); // g_ShapeDetailSizeScaleQ10 is a word.
		int root = Math.Min(setting.RootBias, rootCount - 1);
		for (int j = 0; root < rootCount - 1 && j < thresholds.Count && scaled < thresholds[j]; j++) {
			root++;
		}

		return root;
	}

	/// <summary>
	/// <c>Math_FastMagnitude3D</c> of the eye in the space <paramref name="node"/> places in the world: what the LOD root
	/// measures in the model's frame, and a <c>TSDetailPart</c> in its own node's
	/// (docs/retail/rendering/dts-texture-binding.md#tsdetailpart-level-selection-and-structure-detail, which ES1's
	/// <c>TSDetailPart_Render</c>, <c>000530b0</c>, follows).
	/// </summary>
	public static int EyeDistance(in Transform3 node, Vec3i eye) {
		var local = node.Inverted().TransformPoint(eye.X, eye.Y, eye.Z);
		return SimMath.FastMagnitude3D(local.X, local.Y, local.Z);
	}
}
