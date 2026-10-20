using System.Buffers.Binary;
using HercWorks.Core.Data.File.Gau;
using HercWorks.Core.Data.Struct;
using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Dbsim;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="Es1GauFileTransformer"/> and <see cref="Es1GauFile.Loaded"/> (docs/es1/formats/gau-cockpit-layout.md). The tests
/// over ES1's <c>SIMVOL0.VOL</c> pass vacuously when <c>ES1/VOL</c> is absent, so the suite does not depend on the install.
/// </summary>
public class Es1GauFileTests {
	private static readonly Lazy<IReadOnlyDictionary<string, VolEntry>?> Layouts = new(Load);

	/// <summary>
	/// Every <c>gau\</c> file goes through the reader ES1's table binds to it and back to its bytes, and ES2's table keeps its own
	/// reader for the same entries. Catches a field read or written out of order or at the wrong size, and the ES1 rebinding
	/// dropped or bound to ES2's reader.
	/// </summary>
	[Fact]
	public void EveryLayoutRoundTripsThroughEs1sReader() {
		if (Layouts.Value is not { } layouts) {
			return;
		}

		Assert.Equal(8, layouts.Count);
		foreach (var (name, entry) in layouts) {
			var transformer = Assert.IsType<Es1GauFileTransformer>(TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege1));
			Assert.IsType<GauFileTransformer>(TransformerRegistry.FindTransformer(entry, RetailGame.Earthsiege2));
			Assert.True(entry.RawBytes.AsSpan().SequenceEqual(transformer.Write(transformer.Parse(entry.RawBytes)!)), $"{name} does not round-trip");
		}
	}

	/// <summary>
	/// The doc's check over every file: the in-use rows fit the nine rects from 20 to 163, and the ammunition and energy blocks'
	/// part rects at 180, 196, 260 and 276, relative to a row's top-left, lie within every in-use row's height. Catches the
	/// count or the rows read from the wrong offset, the row slots miscounted, and the part rects read from the wrong place in
	/// their blocks (the scratch rect in a part's place, say).
	/// </summary>
	[Fact]
	public void TheRowsAndTheirPartsFitTheLayout() {
		if (Layouts.Value is not { } layouts) {
			return;
		}

		foreach (var (name, entry) in layouts) {
			var gau = new Es1GauFileTransformer().Parse(entry.RawBytes)!;
			Assert.InRange(gau.WeaponRowCount, 0, gau.WeaponRows.Length);
			// The in-use rects end at or before 164, where the ammunition row's block starts.
			Assert.True(Es1GauFile.WeaponRowsOffset + 16 * gau.WeaponRowCount <= 164, $"{name}: {gau.WeaponRowCount} rows");

			var parts = new[] {
				gau.AmmunitionRow.SelectButton, gau.AmmunitionRow.RoundCount, gau.EnergyRow.SelectButton, gau.EnergyRow.ChargeBar,
			};
			foreach (var row in gau.WeaponRows.Take(gau.WeaponRowCount)) {
				int height = row.Y1 - row.Y0;
				Assert.All(parts, part => Assert.True(part.Y0 >= 0 && part.Y0 <= part.Y1 && part.Y1 <= height,
					$"{name}: part {part} outside row {row}'s height {height}"));
			}
		}
	}

	/// <summary>
	/// <c>Gau_Load</c>'s add on a file whose every <c>int32</c> differs, with a non-zero origin and two rows in use: the result is
	/// the file with the origin added to both corners of rows 0 and 1 and of the first 16 bytes of the seven blocks, nothing
	/// else, and the file as read is unchanged. Catches the add reaching rows past the count, a block missed, added to or
	/// offset wrongly, a corner left out, and the add done in place.
	/// </summary>
	[Fact]
	public void LoadingAddsTheOriginToTheInUseRowsAndTheBlockHeads() {
		byte[] bytes = new byte[Es1GauFile.Length];
		for (int at = 0; at < bytes.Length; at += 4) {
			BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), 1000 + at);
		}

		var transformer = new Es1GauFileTransformer();
		var file = transformer.Parse(bytes)!;
		file.Origin = new PixelPoint(3, -5);
		file.WeaponRowCount = 2;
		byte[] asRead = transformer.Write(file)!;

		byte[] expected = (byte[])asRead.Clone();
		foreach (int rect in new[] { 20, 36, 452, 548, 616, 728, 1000, 1104, 1212 }) {
			for (int corner = 0; corner < 2; corner++) {
				var x = expected.AsSpan(rect + corner * 8);
				BinaryPrimitives.WriteInt32LittleEndian(x, BinaryPrimitives.ReadInt32LittleEndian(x) + 3);
				var y = expected.AsSpan(rect + corner * 8 + 4);
				BinaryPrimitives.WriteInt32LittleEndian(y, BinaryPrimitives.ReadInt32LittleEndian(y) - 5);
			}
		}

		var loaded = file.Loaded();
		Assert.Equal(expected, transformer.Write(loaded));
		Assert.Equal(asRead, transformer.Write(file));
	}

	private static IReadOnlyDictionary<string, VolEntry>? Load() {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", "SIMVOL0.VOL");
			if (!File.Exists(vol)) {
				continue;
			}

			var archive = VolFileReader.ParseVolFile(vol);
			string Folder(VolEntry e) => archive.Folders.TryGetValue(e.DirIdx, out var dir) ? dir.Label : "";
			return archive.FilesSet
				.Where(e => Folder(e).Equals("gau", StringComparison.OrdinalIgnoreCase)
					&& e.FileName!.Trim().EndsWith(".GAU", StringComparison.OrdinalIgnoreCase))
				.ToDictionary(e => e.FileName!.Trim().ToUpperInvariant(), e => e);
		}

		return null;
	}
}
