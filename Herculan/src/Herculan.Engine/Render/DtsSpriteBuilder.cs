using System.Numerics;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Bsp;
using HercWorks.Core.Data.File.Dts.Part;

namespace Herculan.Engine.Render;

/// <summary>
/// One <see cref="TSBitmapPart"/>: a bitmap out of the shape's bound bank, drawn as a flat quad
/// that faces the viewer. Everything here is the part's own on-disk state — where the sizing and
/// placement rules come from is <see cref="SpriteRenderer"/>'s business.
/// </summary>
/// <param name="FrameIndex">The part's <c>BmpTag</c>, a plain index into the bound bank.</param>
/// <param name="Radius">
/// The part's <see cref="TSBasePart.Radius"/>, in world units. It is the sprite's scale: the
/// original's screen size is <c>(radius * 4 &lt;&lt; focalShift) / depth * pixels / 256</c>, so one
/// bitmap pixel spans <c>radius / 64</c> world units — see <see cref="SpriteRenderer"/>.
/// </param>
/// <param name="Center">The part's <see cref="TSBasePart.Center"/>, in render space.</param>
/// <param name="OffsetX">
/// The part's <c>OfsX</c>, <b>signed</b>, in bitmap pixels — where in the sprite the
/// <paramref name="Center"/> lands horizontally.
/// </param>
/// <param name="OffsetY">The part's <c>OfsY</c>, unsigned, the same measure vertically.</param>
public readonly record struct SpriteQuad(int FrameIndex, int Radius, Vector3 Center, int OffsetX, int OffsetY);

/// <summary>
/// Pulls the billboard sprites out of a parsed DTS tree — the half of a shape
/// <see cref="DtsMeshBuilder"/> does not build, because a <see cref="TSBitmapPart"/> carries no
/// geometry at all.
///
/// <para><b>What a sprite shape is.</b> A <see cref="TSCellAnimPart"/> whose children are
/// <see cref="TSBitmapPart"/>s is a flipbook: <c>TSCellAnimPart_Render</c> (<c>004767e4</c>) draws
/// exactly one child per frame, <c>children[cellFrames[AnimSequence] % childCount]</c>, where
/// <c>cellFrames</c> is the drawing shape instance's own per-sequence frame array. So the children
/// are frames, not parts — walking them all the way <see cref="DtsMeshBuilder"/> walks a group
/// would stack the whole animation on top of itself.</para>
///
/// <para>That is what <c>BULLETS.DTS</c> roots 2 and 3 (the three EMP cannons' rounds) and all
/// twenty <c>EXPLOS.DTS</c> roots (every impact effect) are, and it is why neither drew anything
/// before this: they are billboards out of a <c>.DBA</c>, with no polys anywhere in them.</para>
///
/// <para>A bitmap part that is <i>not</i> under a cell animation belongs to every frame — there is
/// no flipbook to index it by, and the original simply draws it whenever the shape is drawn. No
/// retail shape does this, but the rule costs nothing and is what the render walk implies.</para>
/// </summary>
public static class DtsSpriteBuilder {
	/// <summary>Guard against a pathological file claiming a flipbook longer than any bank could feed.</summary>
	private const int MaxFrames = 256;

	/// <summary>
	/// The flipbook for one shape root: entry <c>i</c> is what the shape draws while its frame
	/// counter reads <c>i</c>. Empty when the root holds no bitmap parts at all, which is every
	/// ordinary geometry shape.
	/// </summary>
	public static SpriteQuad[][] Build(TSObject? root) {
		var frames = new List<List<SpriteQuad>>();
		var everyFrame = new List<SpriteQuad>();

		Collect(root, frames, everyFrame, 0);

		// A cell animation over geometry — a rocket's exhaust, the plasma round's two cells — makes
		// frame slots without putting a single sprite in any of them. Reporting those as a flipbook
		// would make an ordinary geometry shape claim billboards it does not have.
		if (everyFrame.Count == 0 && frames.All(frame => frame.Count == 0)) {
			return Array.Empty<SpriteQuad[]>();
		}

		// A shape with only unsequenced sprites still has one frame to draw.
		int count = System.Math.Max(frames.Count, 1);
		var built = new SpriteQuad[count][];
		for (int i = 0; i < count; i++) {
			var quads = new List<SpriteQuad>(everyFrame);
			if (i < frames.Count) {
				quads.AddRange(frames[i]);
			}
			built[i] = quads.ToArray();
		}

		return built;
	}

	/// <summary>
	/// Walks the tree the way <c>TSPartList</c>'s render does, collecting bitmap parts and noting
	/// which flipbook frame each belongs to. Groups and BSP nodes are geometry and are skipped —
	/// <see cref="DtsMeshBuilder"/> has them, and a shape is allowed to hold both (the plasma
	/// round's root 8 is a two-cell animation over real polys).
	/// </summary>
	private static void Collect(TSObject? node, List<List<SpriteQuad>> frames, List<SpriteQuad> everyFrame, int depth) {
		if (node == null || depth > 16) {
			return;
		}

		switch (node) {
			case TSBitmapPart bitmap:
				everyFrame.Add(QuadOf(bitmap));
				break;

			case TSCellAnimPart cellAnim:
				CollectCells(cellAnim, frames, everyFrame, depth);
				break;

			// A child the tree never reaches is never drawn — see DtsMeshBuilder.ReachableParts.
			case TSBSPPart bspPart:
				foreach (var part in DtsMeshBuilder.ReachableParts(bspPart)) {
					Collect(part, frames, everyFrame, depth + 1);
				}

				break;

			case TSPartList partList:
				foreach (var part in partList.Parts ?? Array.Empty<TSObject>()) {
					Collect(part, frames, everyFrame, depth + 1);
				}

				break;
		}
	}

	private static void CollectCells(TSCellAnimPart cellAnim, List<List<SpriteQuad>> frames, List<SpriteQuad> everyFrame, int depth) {
		var cells = cellAnim.Parts ?? Array.Empty<TSObject>();
		int count = System.Math.Min(cells.Length, MaxFrames);

		// Several cell animations in one shape share the frame counter of their own sequence, so a
		// second one lines its cells up with the first's rather than appending to them.
		while (frames.Count < count) {
			frames.Add(new List<SpriteQuad>());
		}

		for (int cell = 0; cell < count; cell++) {
			switch (cells[cell]) {
				case TSBitmapPart bitmap:
					frames[cell].Add(QuadOf(bitmap));
					break;

				// A nested list inside one cell is that cell's whole content, sprites included.
				case TSPartList nested:
					var nestedFrames = new List<List<SpriteQuad>>();
					var nestedEveryFrame = new List<SpriteQuad>();
					Collect(nested, nestedFrames, nestedEveryFrame, depth + 1);
					frames[cell].AddRange(nestedEveryFrame);
					foreach (var inner in nestedFrames) {
						frames[cell].AddRange(inner);
					}

					break;
			}
		}
	}

	/// <summary>
	/// The part's own fields, with its centre taken straight to render space — DTS model units are
	/// world units (see <see cref="WorldScale.WorldUnitsPerDtsUnit"/>).
	///
	/// <para>The node that places the part is not applied. <c>TSBitmapPart_Render</c> installs it
	/// like any other part's (<c>00476014</c> reads <c>TSBasePart.Transform</c>), but every bitmap
	/// part in retail data carries <c>-1</c> there and a centre of the origin, so the composition
	/// would be the identity on every shape that exists.</para>
	/// </summary>
	private static SpriteQuad QuadOf(TSBitmapPart bitmap) {
		var center = bitmap.Center is { } c
			? WorldScale.DtsToRender(c.X, c.Y, c.Z)
			: Vector3.Zero;

		// OfsX is read as a signed byte by the original (`*(char *)(part + 0x12)`) and OfsY as an
		// unsigned one (`*(byte *)(part + 0x13)`) — an asymmetry in the exe, kept literally.
		return new SpriteQuad(bitmap.BmpTag, bitmap.Radius, center, (sbyte)bitmap.OfsX, bitmap.OfsY);
	}
}
