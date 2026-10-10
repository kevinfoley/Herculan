using System.Buffers.Binary;

namespace Herculan.Engine.Content;

/// <summary>
/// One horizontal run of pixels on a single scanline: <paramref name="Start"/> is the leftmost column
/// and <paramref name="Length"/> the number of columns, matching the original's own
/// <c>{int start, int length}</c> span pairs.
/// </summary>
public readonly record struct ClipSpan(int Start, int Length);

/// <summary>
/// A herc's per-view 3D-viewport cutout, read from its own <c>.HD0</c>-<c>.HD3</c> (640-wide) or
/// <c>.ED0</c>-<c>.ED3</c> (320-wide) file. These say, per scanline, exactly which columns the live
/// 3D scene shows through the canopy.
///
/// <para>On-disk layout and the parse verification against the retail files are in
/// docs/retail/formats/hd-ed-clip-regions.md; why this is the viewport cutout mechanism (DBSIM never
/// colour-keys the canopy art) is docs/retail/simulation/cockpit-views.md, "The viewport cutout". The 9-byte VOL entry prefix that layout is written against has already been stripped by
/// <see cref="GameContent"/>. Blocks may overlap and repeat; this parser accumulates every source
/// region per row, as the original's flattening step does.</para>
///
/// <para><b>An empty file is meaningful, not a failure.</b> <c>APOCA.HD1</c> is 16 bytes — both counts
/// zero — because APOCA's heads-down display, view 1, shows no 3D at all, and its <c>.VUE</c>
/// record agrees with a zero-size viewport rect. Only RAZOR's has spans. So a region list that produces no spans is a valid
/// parse and must not be reported as one that failed.</para>
///
/// <para><b>One deliberate divergence.</b> A rect's fourth field is an inclusive <c>x1</c> here.
/// DBSIM's own flattening step feeds it to the rasterizer as a span <i>length</i> instead
/// (<c>piVar4[1] = piVar1[3]</c>, against <c>end - start + 1</c> for the span-block path), which would
/// make it one column short. Every rect in every retail file has <c>x0 == 0</c>, so the two readings
/// differ by exactly one pixel at the right edge and nothing else; the inclusive reading is taken
/// because the loader's own coordinate doubling for the 320-wide art (<c>x1 = (x1 &lt;&lt; shift) +
/// (1 &lt;&lt; shift) - 1</c>) is inclusive-end arithmetic.</para>
///
/// <para>With all three panels shown at once, each carries its own view's rect: the forward panel
/// view 0, the unmirrored side panel view 2 and the mirrored side panel view 3, whose rect is
/// reflected about the view width as its art is. All three render one camera into one viewport
/// (<see cref="Render.CockpitScreenLayout.World"/>) and the rects are the scissors that divide it.
/// Pairing the mirrored panel with view 2's rect would clip a band off its outer edge that retail
/// does not (docs/retail/formats/vue-view-geometry.md, "Retail files").</para>
/// </summary>
public sealed class CockpitClipRegions {
	/// <summary>Resource folder stem for the 640-wide clip files, per <see cref="HudSpriteSheet.ResourceFolder"/>'s reasoning.</summary>
	public const string HiResFolderStem = "hd";

	/// <summary>Resource folder stem for the 320-wide clip files.</summary>
	public const string LoResFolderStem = "ed";

	private readonly List<ClipSpan>[] _rows;

	private CockpitClipRegions(List<ClipSpan>[] rows) {
		_rows = rows;
	}

	/// <summary>Highest row index the file describes, plus one. Rows past this are fully opaque canopy.</summary>
	public int RowCount => _rows.Length;

	/// <summary>Total span count across every row — zero for a view that shows no 3D (e.g. the heads-down view).</summary>
	public int SpanCount => _rows.Sum(r => r.Count);

	/// <summary>The visible spans on one scanline, empty when that row is entirely canopy.</summary>
	public IReadOnlyList<ClipSpan> Row(int y) =>
		y >= 0 && y < _rows.Length ? _rows[y] : Array.Empty<ClipSpan>();

	/// <summary>
	/// Loads one view's cutout for <paramref name="hercName"/>. <paramref name="viewIndex"/> is the
	/// original's own view number: 0 forward, 1 heads-down, 2 and 3 the two sideways glances (2 and 3
	/// share a canopy bitmap but have their own clip files). Returns null when the file is missing or
	/// malformed — the caller decides what to do about it rather than getting a silently empty mask,
	/// since "no spans" and "no file" mean opposite things here.
	/// </summary>
	public static CockpitClipRegions? Load(GameContent content, string hercName, int viewIndex, bool hiRes = true) {
		string stem = hiRes ? HiResFolderStem : LoResFolderStem;
		string folder = stem + viewIndex.ToString();
		byte[]? bytes = content.Read(folder, hercName + "." + folder.ToUpperInvariant());
		return bytes == null ? null : Parse(bytes);
	}

	/// <summary>Parses the region arrays and flattens them to per-scanline spans in one pass.</summary>
	public static CockpitClipRegions? Parse(byte[] bytes) {
		int at = 0;
		if (!TryReadInt16(bytes, ref at, out int rectCount) || rectCount < 0) {
			return null;
		}

		var rows = new Dictionary<int, List<ClipSpan>>();
		int highestRow = -1;

		void Add(int y, int start, int length) {
			if (length <= 0) {
				return;
			}

			if (!rows.TryGetValue(y, out var list)) {
				rows[y] = list = new List<ClipSpan>();
			}

			list.Add(new ClipSpan(start, length));
			if (y > highestRow) {
				highestRow = y;
			}
		}

		for (int r = 0; r < rectCount; r++) {
			if (!TryReadInt16(bytes, ref at, out int y0) || !TryReadInt16(bytes, ref at, out int y1)
				|| !TryReadInt16(bytes, ref at, out int x0) || !TryReadInt16(bytes, ref at, out int x1)) {
				return null;
			}

			for (int y = y0; y <= y1; y++) {
				Add(y, x0, x1 - x0 + 1);
			}
		}

		if (!TryReadInt16(bytes, ref at, out int blockCount) || blockCount < 0) {
			return null;
		}

		for (int b = 0; b < blockCount; b++) {
			if (!TryReadInt16(bytes, ref at, out int firstRow) || !TryReadInt16(bytes, ref at, out int rowCount)
				|| rowCount < 0) {
				return null;
			}

			for (int i = 0; i < rowCount; i++) {
				if (!TryReadInt16(bytes, ref at, out int xStart) || !TryReadInt16(bytes, ref at, out int xEnd)) {
					return null;
				}

				Add(firstRow + i, xStart, xEnd - xStart + 1);
			}
		}

		var table = new List<ClipSpan>[highestRow + 1];
		for (int y = 0; y < table.Length; y++) {
			table[y] = rows.TryGetValue(y, out var list) ? list : new List<ClipSpan>();
			// Sorted by start, as the original's own insertion sort leaves it. Nothing here depends on
			// the order, but keeping it makes a dumped mask comparable against the original's table.
			table[y].Sort((a, b) => a.Start.CompareTo(b.Start));
		}

		return new CockpitClipRegions(table);
	}

	private static bool TryReadInt16(byte[] bytes, ref int at, out int value) {
		if (at + 2 > bytes.Length) {
			value = 0;
			return false;
		}

		value = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at, 2));
		at += 2;
		return true;
	}
}
