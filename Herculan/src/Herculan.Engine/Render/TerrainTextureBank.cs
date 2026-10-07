using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Terrain;
using Herculan.Engine.World;

namespace Herculan.Engine.Render;

/// <summary>
/// The terrain side of the texturing chain in docs/retail/formats/terrain-texturing.md: a theater's packed
/// <c>.DBA</c> plus the shared <c>dat\mat0</c> table, together able to answer "which rect of the
/// atlas does this cell wear".
///
/// <para>The chain, end to end: the <c>world&lt;N&gt;</c> descriptor names the bank
/// (<see cref="TheaterDescriptor.TerrainBankName"/>), a cell's own flag byte carries a material index
/// (<see cref="HeightGrid.MaterialIndexAt"/>), <c>mat0[material].Index</c> is a frame index into that
/// bank, and <c>mat0[material].BlockShift</c> says how the frame is placed on the cell.</para>
/// </summary>
public sealed class TerrainTextureBank {
	/// <summary>
	/// The UV space one terrain frame spans, in texels. This is the modulus in the original's
	/// <c>&amp; 0xff</c> tiling wrap, which is why every terrain bank ships 256x256 frames and why
	/// they have to be edge-tileable.
	/// </summary>
	private const float UvSpaceTexels = 256f;

	private TerrainTextureBank(TextureAtlas atlas, TerrainMaterialTable materials, string bankName) {
		Atlas = atlas;
		Materials = materials;
		BankName = bankName;
	}

	/// <summary>The whole bank packed into one texture.</summary>
	public TextureAtlas Atlas { get; }

	/// <summary>The shared material table cells index into.</summary>
	public TerrainMaterialTable Materials { get; }

	/// <summary>Base name of the <c>.DBA</c> this came from, for logging.</summary>
	public string BankName { get; }

	/// <summary>
	/// Loads and packs the theater's terrain bank, using the theater's own palette — the original
	/// loads <c>dpl\world&lt;N&gt;.dpl</c> as the first act of <c>maybe_World_LoadTheater</c>, before
	/// anything it colours. Returns null when either resource is missing or the bank holds no usable
	/// frames, in which case terrain draws flat-shaded exactly as it did before.
	/// </summary>
	public static TerrainTextureBank? Load(GameContent content, TheaterDescriptor theater,
			TerrainMaterialTable materials) {
		byte[]? bankBytes = content.Read("dba", theater.TerrainBankName + ".DBA");
		if (bankBytes == null
			|| new DynamixBitmapArrayTransformer().Parse(bankBytes) is not DynamixBitmapArray bank) {
			return null;
		}

		byte[]? paletteBytes = content.Read("dpl", theater.PaletteName + ".DPL");
		var palette = paletteBytes != null
			? new DynamixPaletteTransformer().Parse(paletteBytes) as DynamixPalette
			: null;

		var atlas = TextureAtlas.Build(bank, palette);
		return atlas != null ? new TerrainTextureBank(atlas, materials, theater.TerrainBankName) : null;
	}

	/// <summary>
	/// The atlas rect cell (<paramref name="cellX"/>, <paramref name="cellY"/>) samples, or null when
	/// its material or frame does not resolve — in which case the caller falls back to flat shading
	/// for that cell rather than sampling an arbitrary part of the atlas.
	///
	/// <para>A literal transcription of <c>Terrain_ResolveCellTexture</c> (<c>0046bcf4</c>):</para>
	/// <code>
	/// BlockShift == 0 : the frame's own corners, i.e. one frame stretched over the quad
	/// BlockShift != 0 : shift = cellShift + BlockShift - 13
	///                   u0 = (cellX &lt;&lt; shift) &amp; 0xff, spanning 1 &lt;&lt; shift texels
	///                   v0 = (cellY &lt;&lt; shift) &amp; 0xff, likewise, with V negated
	/// </code>
	///
	/// <para>Note the wrap can never split a cell's rect: <c>u0</c> is always a multiple of the span,
	/// so <c>u0 + span</c> lands at or below 256. That is what lets the bank be packed into an atlas
	/// at all — no cell needs texture-space repeat, only its own sub-rect.</para>
	///
	/// <para><b>Corner order is the original's too, and V1 is the smaller value.</b> <c>u</c> rises
	/// with <c>cellX</c> but <c>v</c> falls with <c>cellY</c>, and <see cref="AtlasRect.V1"/> is the V
	/// at corner <c>cellY + 1</c>. Handing the pair over the other way mirrors every cell against the
	/// row below it. See docs/retail/formats/terrain-texturing.md, "Which quad corner takes which UV", for
	/// how the mapping is read off <c>Terrain_DrawCellQuad</c>.</para>
	/// </summary>
	public AtlasRect? CellRect(HeightGrid grid, int cellX, int cellY) {
		int material = grid.MaterialIndexAt(cellX, cellY);
		if (material < 0 || material >= Materials.Count) {
			return null;
		}

		var record = Materials[material];
		if (Atlas.Frame(record.Index) is not { } frame) {
			return null;
		}

		if (record.BlockShift == 0) {
			return frame;
		}

		int shift = grid.CellShift + record.BlockShift - 13;
		if (shift < 0 || shift > 8) {
			// Outside this range the "one rect per cell inside one frame" reading stops holding: a
			// negative shift is not expressible as an integer texel step and a shift past 8 makes a
			// cell larger than the 256-texel wrap. No retail combination reaches either.
			return null;
		}

		int span = 1 << shift;
		int u0 = (cellX << shift) & 0xff;
		int v0 = (cellY << shift) & 0xff;

		// V negated, wrapped back into the frame: corner cellY samples at 256 - v0 and corner
		// cellY + 1 at 256 - v0 - span, so V1 < V0 and consecutive rows join instead of mirroring.
		return SubRect(frame,
			u0 / UvSpaceTexels, (UvSpaceTexels - v0) / UvSpaceTexels,
			(u0 + span) / UvSpaceTexels, (UvSpaceTexels - v0 - span) / UvSpaceTexels);
	}

	/// <summary>
	/// Maps a rect expressed in a frame's own 0..1 space into the atlas. The frame's whole extent is
	/// taken to be the 256-texel UV space the wrap implies, which is exactly true for retail terrain
	/// banks and the only consistent reading for anything else.
	/// </summary>
	private static AtlasRect SubRect(AtlasRect frame, float u0, float v0, float u1, float v1) {
		float width = frame.U1 - frame.U0;
		float height = frame.V1 - frame.V0;

		return new AtlasRect(
			frame.U0 + u0 * width, frame.V0 + v0 * height,
			frame.U0 + u1 * width, frame.V0 + v1 * height);
	}
}
