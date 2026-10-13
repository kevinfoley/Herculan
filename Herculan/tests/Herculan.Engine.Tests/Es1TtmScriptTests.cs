using HercWorks.Core.Data.File;
using HercWorks.Core.Io.Transform;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Vol;
using HercWorks.Vol.Io;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// ES1's <c>.TTM</c> animation scripts (docs/es1/formats/ttm-scripts.md) through the Core reader ES1's table binds to them.
/// Each test on the archives passes vacuously when they are absent.
/// </summary>
public class Es1TtmScriptTests {
	private static readonly Lazy<IReadOnlyList<VolEntry>?> Shell = new(() => Load("SHELL0.VOL"));
	private static readonly Lazy<IReadOnlyList<VolEntry>?> Sim = new(() => Load("SIMVOL0.VOL"));

	/// <summary>
	/// All 123 round-trip as <c>VER:</c>, <c>PAG:</c>, <c>TT3:</c> and a <c>TTI:</c> container holding <c>TAG:</c>; each script
	/// is packed with method 1, unpacks to its header's length from exactly its chunk's packed bytes, divides into its page
	/// count's pages as the loaders' walk divides it, and its tag table fills its chunk.
	/// </summary>
	[Fact]
	public void EveryScriptRoundTripsAndUnpacksToItsHeaderLength() {
		if (Shell.Value is not { } shell || Sim.Value is not { } sim) {
			return;
		}

		var files = shell.Concat(sim).Where(e => HasExtension(e, ".TTM")).ToList();
		Assert.Equal((113, 10), (shell.Count(e => HasExtension(e, ".TTM")), sim.Count(e => HasExtension(e, ".TTM"))));
		foreach (var e in files) {
			var reader = Assert.IsType<Es1TtmScriptTransformer>(TransformerRegistry.FindTransformer(e, RetailGame.Earthsiege1));
			var ttm = reader.Parse(e.RawBytes)!;
			Assert.Equal(e.RawBytes, reader.Write(ttm));
			Assert.Equal(["VER:", "PAG:", "TT3:", "TTI:"], ttm.Chunks.Select(c => c.Tag));
			Assert.Equal("TAG:", Assert.Single(ttm.Chunks[3].Children).Tag);
			Assert.Equal("4.14\0"u8.ToArray(), ttm.Chunks[0].Content);
			Assert.Equal((2, (byte)1), (ttm.Chunks[1].Content.Length, ttm.PackMethod!.Value));
			Assert.Equal((ttm.Chunks[2].Content.Length - 5, (int)ttm.UnpackedLength!.Value), (ttm.PackedBytesRead, ttm.Script!.Length));
			Assert.NotNull(Es1TtmScriptTransformer.IndexPages(ttm.Script, ttm.PageCount!.Value));
			Assert.NotNull(Es1TtmScriptTransformer.ReadTags(ttm.Chunks[3].Children[0].Content));
		}
	}

	/// <summary><c>C0_BRD.TTM</c>, walked by hand: four pages in 128 unpacked bytes, and three tags.</summary>
	[Fact]
	public void TheBriefingRoomScriptReadsAsWalkedByHand() {
		if (Shell.Value is not { } shell) {
			return;
		}

		var e = shell.Single(x => x.FileName!.Trim().Equals("C0_BRD.TTM", StringComparison.OrdinalIgnoreCase));
		var ttm = new Es1TtmScriptTransformer().Parse(e.RawBytes)!;
		Assert.Equal(((ushort)4, 128u), (ttm.PageCount!.Value, ttm.UnpackedLength!.Value));
		Assert.Equal([(1, "pos 1"), (2, "pos 2"), (3, "nothing")],
			Es1TtmScriptTransformer.ReadTags(ttm.Chunks[3].Children[0].Content)!.Select(t => ((int)t.Id, t.Name)));
	}

	/// <summary>A run control of <c>0x80</c> still takes its value byte, and a literal control of 0 takes none.</summary>
	[Fact]
	public void AnEmptyRunTakesItsValueByte() {
		byte[] script = Es1TtmScriptTransformer.Unpack([0x80, 0x55, 0x00, 0x02, 0x41, 0x42, 0x83, 0x43], 5, out int read);
		Assert.Equal("ABCCC"u8.ToArray(), script);
		Assert.Equal(8, read);
	}

	/// <summary>The page walk takes up to seven argument words and fails on an opcode whose low nibble is 8.</summary>
	[Fact]
	public void ThePageWalkFailsOnEightArguments() {
		byte[] seven = [0x17, 0x10, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 0xf0, 0x0f, 0];
		Assert.Equal([0], Es1TtmScriptTransformer.IndexPages(seven, 1)!);
		byte[] eight = [0x18, 0x10, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, 7, 0, 8, 0, 0xf0, 0x0f, 0];
		Assert.Null(Es1TtmScriptTransformer.IndexPages(eight, 1));
	}

	private static bool HasExtension(VolEntry e, string extension) =>
		e.FileName != null && e.FileName.Trim().EndsWith(extension, StringComparison.OrdinalIgnoreCase);

	private static IReadOnlyList<VolEntry>? Load(string archive) {
		for (var at = new DirectoryInfo(AppContext.BaseDirectory); at != null; at = at.Parent) {
			string vol = Path.Combine(at.FullName, "ES1", "VOL", archive);
			if (File.Exists(vol)) {
				return VolFileReader.ParseVolFile(vol).FilesSet.ToList();
			}
		}

		return null;
	}
}
