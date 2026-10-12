using HercWorks.Core.Data.File.Dbsim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Numerics;

namespace Herculan.Es1.Scene;

/// <summary>
/// What <c>GndTex_Load</c> (<c>ES1DBSIM 00028de8</c>) sets up for the textured ground and the state <c>GndTex_Draw</c>
/// (<c>00029120</c>) keeps between frames: the 256 × 256 pattern, the fade distances, the 15 fade tables and the span
/// width (docs/es1/rendering/ground-and-sky.md#loading-the-ground-texture, #the-textured-ground).
/// </summary>
public sealed class Es1TexturedGround {
	/// <summary>The fade tables <c>GndTex_Load</c> builds, <c>000b0d80</c>.</summary>
	public const int TableCount = 15;

	/// <summary>Entries in a fade table: one per texel value from 240.</summary>
	public const int TableLength = 16;

	/// <summary>The texel value a fade table's entry 0 is for: the draw looks texel <c>v</c> up at entry <c>v − 240</c>.</summary>
	public const int FirstTexel = 240;

	/// <summary>How many of the distances the draw compares a row's distance against.</summary>
	public const int ComparedDistances = TableCount - 1;

	/// <summary>The span width <c>GndTex_Load</c> stores at <c>00092c80</c>, the view's 320 columns.</summary>
	public const int LoadedSpanWidth = 320;

	/// <summary>The view's rows, which the rolled span width's formula takes as <c>0x320000</c> = 200 in Q14.</summary>
	private const int ViewRowsQ14 = 0x320000;

	private Es1TexturedGround(byte[] texels, int[] distances, byte[,] tables, int fullFadeRows) {
		Texels = texels;
		Distances = distances;
		Tables = tables;
		FullFadeRows = fullFadeRows;
	}

	/// <inheritdoc cref="Es1GroundTexture.Texels"/>
	public byte[] Texels { get; }

	/// <summary>The fade file's distances, <c>000b0d78</c>; the draw compares a row against the first <see cref="ComparedDistances"/>.</summary>
	public int[] Distances { get; }

	/// <summary>
	/// The fade tables, <c>000b0d70</c>: <c>[i, j]</c> is the palette index texel <c>240 + j</c> draws in at table
	/// <c>i</c>. Table 0 is the 16-entry palette row holding the ground colour; each later one moves every entry
	/// further toward <i>top</i>, short 4 + short 6 − 1.
	/// </summary>
	public byte[,] Tables { get; }

	/// <summary>Half of short 0, <c>000b0d84</c>: the rows from the ground's first that take the last table whatever their distance.</summary>
	public int FullFadeRows { get; }

	/// <summary>
	/// The span width, <c>00092c80</c>: 320 at load, rewritten by every rolled frame and kept through later level
	/// ones (<see cref="UpdateSpanWidth"/>).
	/// </summary>
	public int SpanWidth { get; private set; } = LoadedSpanWidth;

	/// <summary>
	/// Builds the ground from the world file and the two files its fourth and fifth strings name, or returns null
	/// when either is missing.
	/// </summary>
	public static Es1TexturedGround? From(Es1WorldData world, Es1GroundTexture? texture, Es1GroundFade? fade) {
		if (texture == null || fade == null) {
			return null;
		}

		int ground = world.GroundColor;
		int rowStart = ground & 0xf0;
		int top = ground + world.GroundBandCount - 1;
		var tables = new byte[TableCount, TableLength];
		for (int i = 0; i < TableCount; i++) {
			for (int j = 0; j < TableLength; j++) {
				int s = top - (rowStart + j);
				if (s < 0) {
					s--;
				}

				// C's division, truncating toward zero, as the retail build's is.
				tables[i, j] = unchecked((byte)(rowStart + j + s * i / (TableCount - 1)));
			}
		}

		return new Es1TexturedGround(texture.Texels, fade.Distances, tables, world.HorizonGap / 2);
	}

	/// <summary>
	/// The table a row draws with, from its number <paramref name="row"/> counted from the ground's first and its
	/// forward distance <paramref name="distance"/>: the last for the first <see cref="FullFadeRows"/>, otherwise how
	/// many of the first <see cref="ComparedDistances"/> distances it reaches, stopping at the first it does not.
	/// The fragment shader runs the same rule.
	/// </summary>
	public int TableFor(int row, float distance) {
		if (row < FullFadeRows) {
			return TableCount - 1;
		}

		int table = 0;
		int compared = Math.Min(ComparedDistances, Distances.Length);
		while (table < compared && distance >= Distances[table]) {
			table++;
		}

		return table;
	}

	/// <summary>The row table's key from its last build: <i>z</i>, the pitch and the rows (<c>00092c44</c>, <c>00092c48</c>, <c>000b0d68</c>).</summary>
	private (int Z, short Pitch, int Rows)? _tableKey;

	/// <summary>The first table row the row table was last built from.</summary>
	private int _tableFirst;

	/// <summary>
	/// The first row <c>GndTex_BuildRowTable</c> (<c>00028fbc</c>) last built the row table from, which is what this
	/// frame's rows read: the table is rebuilt only when the camera's height, its pitch or the canvas origin's x
	/// differs from the last build's, or the frame has more rows than it holds, and <paramref name="first"/> does
	/// not enter the test. The canvas origin is always 0 here.
	/// </summary>
	/// <param name="z">The camera's height, <i>z</i>.</param>
	/// <param name="pitch">The camera's pitch.</param>
	/// <param name="rows">The frame's ground rows.</param>
	/// <param name="first">The frame's first table row, <i>first</i>.</param>
	public int RowTableFirst(int z, short pitch, int rows, int first) {
		if (_tableKey is not { } key || key.Z != z || key.Pitch != pitch || rows > key.Rows) {
			_tableKey = (z, pitch, rows);
			_tableFirst = first;
		}

		return _tableFirst;
	}

	/// <summary>
	/// A rolled frame's span width, which <c>GndTex_Draw</c> stores at <c>00092c80</c> and so keeps for later frames,
	/// and the roll-free row <i>d</i> the ground starts at (docs/es1/rendering/ground-and-sky.md#the-textured-ground).
	/// A level frame leaves the width as it is and starts at <paramref name="startBelowCentre"/>.
	/// </summary>
	/// <param name="roll">The camera's roll, as a binary angle.</param>
	/// <param name="startBelowCentre"><i>start</i> − <i>cy</i>: the ground's first screen row below the projection centre.</param>
	public int UpdateSpanWidth(short roll, int startBelowCentre) {
		if (roll == 0) {
			return startBelowCentre;
		}

		int c = SimTrig.Cos(roll);
		if (c == 0) {
			c = 1;
		}

		int d = (startBelowCentre << 14) / c;
		int a = d == short.MinValue ? short.MaxValue : Math.Abs(unchecked((short)d));
		int sinTerm = SimTrig.Cos(unchecked((short)(roll + 0xc000)));
		if (roll > 0x16a8) {
			SpanWidth = ViewRowsQ14 / SimTrig.Cos(unchecked((short)(0x4000 - roll))) + a * c / sinTerm;
		} else if (roll < -0x16a8) {
			SpanWidth = ViewRowsQ14 / SimTrig.Cos(unchecked((short)(0xc000 - roll))) + a * c / sinTerm;
		} else {
			SpanWidth = 0x500000 / c + (a * c != 0 ? sinTerm / (a * c) : 0);
		}

		return d;
	}

	/// <summary>The palette's colour for every table entry, RGBA8, <see cref="TableLength"/> across and <see cref="TableCount"/> down.</summary>
	public byte[] TableColors(DynamixPalette palette) {
		var pixels = new byte[TableLength * TableCount * 4];
		for (int i = 0; i < TableCount; i++) {
			for (int j = 0; j < TableLength; j++) {
				int at = (i * TableLength + j) * 4;
				if (palette.Colors.TryGetValue(Tables[i, j], out var entry)) {
					var color = entry.GetColor();
					pixels[at] = color.R;
					pixels[at + 1] = color.G;
					pixels[at + 2] = color.B;
				}

				pixels[at + 3] = 255;
			}
		}

		return pixels;
	}
}
