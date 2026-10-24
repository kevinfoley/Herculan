using System.Buffers.Binary;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform.Dbsim;

namespace HercWorks.Query;

/// <summary>An <see cref="Es1GauFile"/> member and the file bytes the reader takes it from.</summary>
/// <param name="OriginInts">
/// How many of its <c>int32</c> <see cref="Es1GauFile.Loaded"/> adds the origin to, in a file with the origin set and every
/// weapon row in use.
/// </param>
internal sealed record GauBlock(string Name, int Offset, int Length, int OriginInts) {
	public bool Contains(int offset) => offset >= Offset && offset < Offset + Length;
}

/// <summary>One chassis' <c>int32</c> of the queried window; null when the install has no layout for it.</summary>
internal sealed record GauRow(int Index, string Name, IReadOnlyList<int>? Values);

/// <param name="Block">The member the window starts, null when it starts inside one.</param>
/// <param name="Located">Where a window that starts inside a member starts: which member, and how far in.</param>
/// <param name="Offsets">Each <c>int32</c>'s file offset.</param>
/// <param name="Indices">Each <c>int32</c>'s index in the member it falls in, as the doc's tables number them.</param>
/// <param name="Loaded">Whether the values are the layout after <c>Gau_Load</c>'s origin add rather than the file as read.</param>
internal sealed record GauQueryResult(
	GauBlock? Block, string? Located, IReadOnlyList<int> Offsets, IReadOnlyList<int> Indices, bool Loaded, IReadOnlyList<GauRow> Rows) {
	public int Layouts => Rows.Count(r => r.Values != null);
}

/// <summary>
/// <c>gau</c>: a window of <c>int32</c> of every ES1 chassis' <c>gau\&lt;name&gt;.GAU</c> cockpit layout
/// (docs/es1/formats/gau-cockpit-layout.md), as <see cref="Es1GauFileTransformer"/> reads it and, by default, as
/// <see cref="Es1GauFile.Loaded"/> leaves it.
///
/// <para>Which member sits at which offset is not tabled here: <see cref="Blocks"/> finds it with
/// <see cref="RecordLayout.Probe"/>, so it follows the reader. The values are the <c>int32</c> of the layout written
/// back by the same reader.</para>
/// </summary>
internal static class GauQuery {
	/// <summary>The layout's members, in file order, each with how many of its <c>int32</c> the load's origin add changes.</summary>
	public static IReadOnlyList<GauBlock> Blocks() {
		var transformer = new Es1GauFileTransformer();
		var probe = transformer.Parse(new byte[Es1GauFile.Length])!;
		probe.Origin = new PixelPoint(1, 1);
		probe.WeaponRowCount = Es1GauFile.WeaponRowSlots;
		byte[] read = transformer.Write(probe)!, loaded = transformer.Write(probe.Loaded())!;
		return RecordLayout.Probe(typeof(Es1GauFile), b => transformer.Parse(b), Es1GauFile.Length)
			.Select(member => {
				var (p, offsets) = member;
				int origin = Enumerable.Range(0, offsets.Count / 4)
					.Count(i => Int(read, offsets[0] + i * 4) != Int(loaded, offsets[0] + i * 4));
				return new GauBlock(p.Name, offsets[0], offsets.Count, origin);
			})
			.OrderBy(b => b.Offset)
			.ToList();
	}

	/// <summary>
	/// The window <paramref name="spec"/> and <paramref name="ints"/> name. <paramref name="spec"/> is a member name, matched
	/// whole and then as a prefix, ignoring case, or a decimal or <c>0x</c> hex file offset that is a multiple of 4.
	/// <paramref name="ints"/> is a count of <c>int32</c> from there, or a range <c>LO-HI</c> of <c>int32</c> indices from
	/// there; null runs to the end of the member the window starts in. Sets <paramref name="error"/> when either names
	/// nothing, or the window runs past the file.
	/// </summary>
	public static IReadOnlyList<int>? ResolveWindow(IReadOnlyList<GauBlock> blocks, string spec, string? ints, out GauBlock? block, out string? error) {
		block = null;
		error = null;
		int start;
		if (RecordLayout.TryParseNumber(spec, out start)) {
			if (start < 0 || start % 4 != 0 || start + 4 > Es1GauFile.Length) {
				error = $"A file offset is a multiple of 4 from 0 to {Es1GauFile.Length - 4}; the file is {Es1GauFile.Length} bytes.";
				return null;
			}

			block = blocks.FirstOrDefault(b => b.Offset == start);
		} else if (RecordLayout.MatchName(blocks, b => b.Name, spec, $"No layout member is named {spec}; `gau` with no --block lists them.", out error) is { } named) {
			block = named;
			start = named.Offset;
		} else {
			return null;
		}

		int first = 0, count;
		if (ints == null) {
			var within = block ?? blocks.FirstOrDefault(b => b.Contains(start));
			count = within == null ? 1 : (within.Offset + within.Length - start) / 4;
		} else if (ints.Split('-') is [var low, var high]) {
			if (!RecordLayout.TryParseNumber(low, out first) || !RecordLayout.TryParseNumber(high, out int last) || first < 0 || last < first) {
				error = "--ints takes a count or a range of int32 indices, LO-HI.";
				return null;
			}

			count = last - first + 1;
		} else if (!RecordLayout.TryParseNumber(ints, out count) || count < 1) {
			error = "--ints takes a count or a range of int32 indices, LO-HI.";
			return null;
		}

		start += first * 4;
		if (start + count * 4 > Es1GauFile.Length) {
			error = $"The window runs to offset {start + count * 4}, past the file's {Es1GauFile.Length} bytes.";
			return null;
		}

		if (first > 0) {
			block = null;
		}

		return Enumerable.Range(0, count).Select(i => start + i * 4).ToList();
	}

	/// <summary>Every chassis' <c>int32</c> at <paramref name="offsets"/>, after the load's origin add when <paramref name="loaded"/>.</summary>
	public static GauQueryResult Run(IReadOnlyList<CockpitLayout> layouts, IReadOnlyList<GauBlock> blocks, IReadOnlyList<int> offsets, GauBlock? block, bool loaded) {
		string? located = null;
		if (block == null) {
			located = blocks.FirstOrDefault(b => b.Contains(offsets[0])) is { } inside
				? $"{offsets[0] - inside.Offset} bytes into {inside.Name} (offsets {inside.Offset}-{inside.Offset + inside.Length - 1})"
				: "in no member";
		}

		var transformer = new Es1GauFileTransformer();
		var rows = layouts.Select(l => {
			if (l.File is not { } file) {
				return new GauRow(l.Index, l.Name, null);
			}

			byte[] bytes = transformer.Write(loaded ? file.Loaded() : file)!;
			return new GauRow(l.Index, l.Name, offsets.Select(o => Int(bytes, o)).ToList());
		}).ToList();
		var indices = offsets.Select(o => (o - (blocks.FirstOrDefault(b => b.Contains(o))?.Offset ?? o)) / 4).ToList();
		return new GauQueryResult(block, located, offsets, indices, loaded, rows);
	}

	private static int Int(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));

	/// <summary>The layout's members, in file order.</summary>
	public static void WriteBlocks(TextWriter output, IReadOnlyList<GauBlock> blocks) {
		output.WriteLine($"{"Offset",6}  {"Hex",-5}  {"Bytes",5}  {"Int32",5}  {"Origin",6}  Member");
		foreach (var b in blocks) {
			output.WriteLine($"{b.Offset,6}  0x{b.Offset:x3}  {b.Length,5}  {b.Length / 4,5}  {(b.OriginInts == 0 ? "" : b.OriginInts.ToString()),6}  {b.Name}");
		}

		output.WriteLine("Origin: how many of the member's int32 the load's origin add changes when the origin is set and every weapon row");
		output.WriteLine("is in use; the add covers the first n weapon rows, n the int32 at 16.");
	}

	public static void Write(TextWriter output, GauQueryResult result) {
		int start = result.Offsets[0], end = result.Offsets[^1] + 3;
		string window = $"offsets {start}-{end} (0x{start:x}-0x{end:x}), {result.Offsets.Count} int32";
		string state = result.Loaded ? "as loaded, after Gau_Load's origin add" : "as read";
		output.WriteLine(result.Block is { } block ? $"{block.Name}: {window}, {state}" : $"{window}, from {result.Located}, {state}");

		int nameWidth = Math.Max(4, result.Rows.Max(r => r.Name.Length));
		var indices = result.Indices.Select(i => $"[0x{i:x}]").ToList();
		var widths = result.Offsets.Select((o, i) => Math.Max(Math.Max(o.ToString().Length, indices[i].Length),
			result.Rows.Max(r => r.Values?[i].ToString().Length ?? 0))).ToList();
		int label = 3 + 2 + nameWidth;
		output.WriteLine("Int".PadRight(label) + string.Concat(indices.Select((index, i) => "  " + index.PadLeft(widths[i]))));
		output.WriteLine("Offset".PadRight(label) + string.Concat(result.Offsets.Select((o, i) => "  " + o.ToString().PadLeft(widths[i]))));
		foreach (var row in result.Rows) {
			string values = row.Values == null ? "  no layout" : string.Concat(row.Values.Select((v, i) => "  " + v.ToString().PadLeft(widths[i])));
			output.WriteLine($"{row.Index,3}  {row.Name.PadRight(nameWidth)}{values}");
		}

		var read = result.Rows.Where(r => r.Values != null).Select(r => r.Values!).ToList();
		int differ = Enumerable.Range(0, result.Offsets.Count).Count(i => read.Select(v => v[i]).Distinct().Count() > 1);
		output.WriteLine($"{result.Layouts} of {result.Rows.Count} chassis have a layout; {differ} of {result.Offsets.Count} int32 differ between them.");
	}
}
