namespace Herculan.Engine.Render;

/// <summary>
/// Which root of a machine's shape is drawn this frame — <c>Shape_DrawAtDetailLevel</c>
/// (<c>004033e4</c>), the selection every <c>Mech_Draw</c> runs before it renders anything.
///
/// <para>A machine's <c>.DTS</c> roots are complete alternate models of one chassis, descending in
/// polygon count, and the original swaps the shape instance's root for one of them every frame from
/// the shape's projected size on screen. See docs/retail/formats/mech-shape-drawing.md for the chain this
/// sits in and for why it is a machine's selection alone: only <c>Mech_Constructor</c>
/// (<c>00415bb0</c>) installs a detail table on an object, so a flyer, a structure and everything
/// else draw the one root they are built with.</para>
///
/// <para>The sibling mechanism inside a shape is <see cref="DtsMeshBuilder"/>'s
/// <c>TSDetailPart</c> handling, which selects between alternate representations of one <i>part</i>
/// on the same projected-size measure but with the table ascending rather than descending — see
/// docs/retail/formats/dts-texture-binding.md.</para>
/// </summary>
public static class ShapeDetail {
	/// <summary>
	/// The threshold table, in projected pixels — <c>g_ShapeDetailThresholds</c> (<c>0049a344</c>),
	/// which <c>MechType_InitOne</c> (<c>004201a8</c>) installs into <i>every</i> mech type record's
	/// detail struct. It is one shared global table, not per-chassis data, and the retail file
	/// carries exactly these seven entries.
	///
	/// <para>Descending, so the walk below advances while the shape is <i>smaller</i> than the entry
	/// and a distant machine ends on a crude root. That is the opposite sense to
	/// <c>TSDetailPart</c>'s ascending table.</para>
	/// </summary>
	public static readonly int[] Thresholds = { 75, 60, 45, 25, 18, 12, 6 };

	/// <summary>
	/// <c>g_ShapeDetailSizeScaleQ10</c> (<c>00497368</c>), the Q10 factor the projected size is
	/// scaled by before the comparison. <c>ShapeDetail_ApplyHercDetailSetting</c> (<c>0045d474</c>)
	/// selects it from the HERC DETAIL setting through a five-entry table whose every entry is 2000,
	/// so the setting does not in fact move it; it is carried because it is a multiplier on the whole
	/// measure and the image's own initialiser leaves it at Q10 one (1024), a different number.
	/// </summary>
	public const int SizeScaleQ10 = 2000;

	/// <summary>
	/// The starting root index each HERC DETAIL setting selects — <c>g_HercDetailBiasValues</c>
	/// (<c>0049f012</c>), <c>{4, 3, 2, 1, 0}</c>, written to <c>g_ShapeDetailBias</c>
	/// (<c>0049736c</c>) by the same <c>ShapeDetail_ApplyHercDetailSetting</c>. Setting 4 is the
	/// finest: <b>a nonzero bias makes root 0 unreachable at any distance</b>, because the walk only
	/// ever advances.
	/// </summary>
	public static readonly int[] BiasForDetailSetting = { 4, 3, 2, 1, 0 };

	/// <summary>
	/// The HERC DETAIL setting to use when none can be read — the finest, matching
	/// <see cref="Terrain.TerrainDetail.DefaultLevel"/>'s reasoning and the install the reference
	/// captures were taken on.
	/// </summary>
	public const int DefaultDetailSetting = 4;

	/// <summary>The bias one HERC DETAIL setting selects, clamped to the table.</summary>
	public static int BiasFor(int detailSetting) =>
		BiasForDetailSetting[Math.Clamp(detailSetting, 0, BiasForDetailSetting.Length - 1)];

	/// <summary>
	/// The shape's projected size, in pixels — <c>(radius &lt;&lt; DAT_006c60ac) / (distance -
	/// radius)</c>, where the shift is the view's focal length and the subtraction measures to the
	/// near face of the bounding sphere rather than to its centre. Floored at 1, as the original
	/// floors it, so a shape at the eye does not come out zero and select the crudest root.
	/// </summary>
	/// <param name="shapeRadius">The shape's own <c>TSBasePart.Radius</c>, in world units.</param>
	/// <param name="viewDistance">Eye to shape origin, in world units.</param>
	/// <param name="focalLengthPixels">
	/// <c>2^DAT_006c60ac</c> — 512 at the original's 640x480, and the window's own focal length
	/// here, so the measure stays a count of pixels on the screen being drawn rather than of pixels
	/// on a 1996 one.
	/// </param>
	public static int ProjectedSize(int shapeRadius, int viewDistance, int focalLengthPixels) {
		int depth = Math.Max(viewDistance - shapeRadius, 1);
		return Math.Max((int)((long)shapeRadius * focalLengthPixels / depth), 1);
	}

	/// <summary>
	/// The root to draw, in <c>[0, rootCount)</c>.
	///
	/// <para><b>The two indices are not the same index</b>, and that is the original's own shape:
	/// the root index starts at <paramref name="bias"/> while the threshold cursor starts at 0, and
	/// both advance together. At the default bias of 0 they coincide and the walk is the plain
	/// descending-table lookup it reads as; at any other setting the machine starts that many roots
	/// down and is compared against the finest thresholds regardless.</para>
	/// </summary>
	public static int SelectRoot(int rootCount, int shapeRadius, int viewDistance,
			int focalLengthPixels, int bias) {
		if (rootCount <= 1) {
			return 0;
		}

		int scaled = Numerics.SimMath.Q10Multiply(
			SizeScaleQ10, ProjectedSize(shapeRadius, viewDistance, focalLengthPixels));
		int root = Math.Min(rootCount - 1, Math.Max(bias, 0));

		// The cursor is bounded by the table as well as by the root count, which retail is not: it
		// would read past a seven-entry table for a shape with more than eight roots. None exists —
		// the deepest retail chain is SAMSON's and APOCA's seven — so the guard changes nothing on
		// this data and keeps a hand-built shape from walking off the end.
		for (int i = 0; root < rootCount - 1 && i < Thresholds.Length && scaled < Thresholds[i]; i++) {
			root++;
		}

		return root;
	}
}
