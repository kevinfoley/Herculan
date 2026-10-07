using System.Numerics;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;

namespace Herculan.Engine.Content;

/// <summary>
/// What a beam looks like: <c>dat\BEAM.DAT</c> (<see cref="BeamData"/>) and <c>dba\BEAMTEX.DBA</c>,
/// the two resources <c>Beam_LoadResourceTables</c> (<c>0040b6e0</c>) loads once at startup. The
/// layouts and the retail records are in docs/retail/formats/beam-dat.md.
///
/// <para>Retail's one <c>BEAMTEX.DBA</c> frame is a pure cross-section, every row one constant
/// palette index, so this class keeps it as a profile of <see cref="ProfileTexels"/> RGBA samples
/// rather than a 2D image (engine choice). The table is indexed by the firing <c>PROJ.DAT</c>
/// record's subtype id, not by weapon id.</para>
///
/// <para>Only the record's width differs per straight beam. Its colour index reaches the screen
/// through the jagged (ELF) path alone — see <see cref="Color"/> and docs/retail/simulation/beam-visuals.md,
/// "<c>BEAM.DAT</c>'s colour index is the fill brush". A beam has no alpha and is an opaque ribbon
/// over whatever it crosses.</para>
/// </summary>
public sealed class BeamAppearance {
	/// <summary>The <c>dat</c> resource <c>Beam_LoadResourceTables</c> opens by the literal name <c>beam</c>.</summary>
	public const string TableResource = "BEAM.DAT";

	/// <summary>The <c>dba</c> resource it loads next, by the literal name <c>beamtex</c>.</summary>
	public const string TextureResource = "BEAMTEX.DBA";

	private readonly BeamData _table;
	private readonly byte[][] _profiles;
	private readonly DynamixPalette? _palette;

	private BeamAppearance(BeamData table, byte[][] profiles, int profileTexels,
			DynamixPalette? palette) {
		_table = table;
		_profiles = profiles;
		ProfileTexels = profileTexels;
		_palette = palette;
	}

	/// <summary>How many <c>BEAM.DAT</c> records were read.</summary>
	public int Count => _table.Data?.Length ?? 0;

	/// <summary>How many samples wide <see cref="Profile"/> is — the source frame's row count.</summary>
	public int ProfileTexels { get; }

	/// <summary>
	/// Loads both resources. Returns null when either is missing or unreadable, in which case beams
	/// simply are not drawn — nothing else in the simulation depends on this.
	/// </summary>
	/// <param name="content">Mounted archives.</param>
	/// <param name="paletteName">
	/// The theater's palette, <c>dpl\WORLD&lt;n&gt;.DPL</c> — the live display palette, and the one
	/// both the profile's indices and the records' colour indices are in. The cockpit scheme
	/// <see cref="CockpitPalette"/> installs over it touches slots 42-65 only, and no beam colour
	/// lands in that window, so reading the theater palette directly is the same answer.
	/// </param>
	public static BeamAppearance? Load(GameContent content, string? paletteName) {
		byte[]? tableBytes = content.Read("dat", TableResource);
		if (tableBytes == null
			|| new BeamDatFileTransformer().Parse(tableBytes) is not BeamData table
			|| table.Data is not { Length: > 0 }) {
			return null;
		}

		byte[]? textureBytes = content.Read("dba", TextureResource);
		if (textureBytes == null
			|| new DynamixBitmapArrayTransformer().Parse(textureBytes) is not DynamixBitmapArray bank
			|| bank.Images is not { Length: > 0 } frames) {
			return null;
		}

		DynamixPalette? palette = null;
		if (!string.IsNullOrWhiteSpace(paletteName)
			&& content.Read("dpl", paletteName + ".DPL") is { } paletteBytes) {
			palette = new DynamixPaletteTransformer().Parse(paletteBytes) as DynamixPalette;
		}

		var profiles = new byte[frames.Length][];
		int texels = 0;
		for (int i = 0; i < frames.Length; i++) {
			profiles[i] = BuildProfile(frames[i], palette);
			texels = Math.Max(texels, profiles[i].Length / 4);
		}

		return texels == 0 ? null : new BeamAppearance(table, profiles, texels, palette);
	}

	/// <summary>
	/// The record for <paramref name="subtypeId"/>, or null when the id is outside the table — which
	/// no retail beam is, but a hand-edited <c>PROJ.DAT</c> could be.
	/// </summary>
	public BeamData.Entry? Record(int subtypeId) =>
		_table.Data is { } data && subtypeId >= 0 && subtypeId < data.Length ? data[subtypeId] : null;

	/// <summary>
	/// Half the beam's width, in world units — the record's first field, which the original uses as
	/// the perpendicular offset applied to both sides of the centre line. Zero when the id is unknown.
	/// </summary>
	public int HalfWidth(int subtypeId) => Record(subtypeId)?.HalfWidth ?? 0;

	/// <summary>
	/// The record's colour index resolved through the theater palette — the fill colour a
	/// <b>jagged</b> beam is painted in, and the one thing that tells ELF from ELF2 on screen. The
	/// straight path never reaches it; see the class remarks. Black when the id is outside the table.
	/// </summary>
	public Vector3 Color(int subtypeId) => Lookup(_palette, Record(subtypeId)?.ColorId ?? 0);

	/// <summary>
	/// The cross-section for <paramref name="subtypeId"/> as RGBA texels, one per source row, running
	/// from one edge of the beam to the other. Falls back to frame 0 when the record names a frame the
	/// bank does not have.
	/// </summary>
	public ReadOnlySpan<byte> Profile(int subtypeId) {
		int frame = Record(subtypeId)?.DBAFrameNum ?? 0;
		if (frame < 0 || frame >= _profiles.Length) {
			frame = 0;
		}

		return _profiles[frame];
	}

	/// <summary>
	/// One row of the frame, expanded through the palette. Every retail row is a single repeated
	/// index, so the first column is the whole row; the general case takes it anyway rather than
	/// assuming a constant row.
	/// </summary>
	private static byte[] BuildProfile(DynamixBitmap frame, DynamixPalette? palette) {
		byte[] indices = frame.ImageData ?? Array.Empty<byte>();
		int rows = frame.Rows;
		int cols = frame.Cols;
		if (rows <= 0 || cols <= 0 || indices.Length < rows * cols) {
			return Array.Empty<byte>();
		}

		var texels = new byte[rows * 4];
		for (int row = 0; row < rows; row++) {
			var color = Lookup(palette, indices[row * cols]);
			texels[row * 4] = (byte)(color.X * 255f);
			texels[row * 4 + 1] = (byte)(color.Y * 255f);
			texels[row * 4 + 2] = (byte)(color.Z * 255f);

			// Opaque, because the span routine that draws a beam stores every texel it fetches. The
			// edges are a hard band of dark orange against the sky in the original too.
			texels[row * 4 + 3] = 255;
		}

		return texels;
	}

	private static Vector3 Lookup(DynamixPalette? palette, int index) {
		if (palette == null || !palette.Colors.TryGetValue(index, out var entry)) {
			return new Vector3(index / 255f);
		}

		var color = entry.GetColor();
		return new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
	}

}
