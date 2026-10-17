using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Install;
using HercWorks.Core.Io.Transform.Dbsim;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <c>edg\*.EDG</c> are clip-region files, read by <c>CockpitClipRegions_Load</c> (<c>0042dcf0</c>) like
/// the <c>.HD*</c>/<c>.ED*</c> cutouts, not flat scanline rows. A flat reading also walks both files to
/// the byte, so a round trip alone cannot tell the two apart; the header words can
/// (docs/retail/formats/hd-ed-clip-regions.md, "Rejected readings").
///
/// <para>Retail-data cases skip silently without an install, as the rest of the suite does.</para>
/// </summary>
public class ClipRegionFileTests {
	/// <summary>
	/// No rects, one span block from row 0, then the int16 0 the loader does not read; the cockpit
	/// cutout flattened from it has one row per span.
	/// </summary>
	[Theory]
	[InlineData("HDDCLIP.EDG")]
	[InlineData("MFDCLIP.EDG")]
	public void EdgFileIsOneSpanBlock(string name) {
		if (GameInstall.Locate(null) is not { } root
			|| GameContent.MountSimulator(root).Read("edg", name) is not { } bytes) {
			return;
		}

		var transformer = new ClipRegionFileTransformer();
		var file = transformer.Parse(bytes);

		Assert.NotNull(file);
		Assert.Empty(file.Rects);
		var block = Assert.Single(file.Blocks);
		Assert.Equal(0, block.FirstRow);
		Assert.NotEmpty(block.Rows);
		Assert.Equal(new byte[] { 0, 0 }, file.TrailingBytes);
		Assert.Equal(block.Rows.Length, CockpitClipRegions.Parse(bytes)?.RowCount);
		Assert.Equal(bytes, transformer.Write(file));
	}

	/// <summary>
	/// <c>HDDCLIP.EDG</c> is laid over the HDD's screen rect, so its extent is that rect's in every
	/// <c>.GAU</c>: one row per authored scanline and its widest span reaching the rect's right edge
	/// (docs/retail/simulation/heads-down-display.md#hddclip).
	/// </summary>
	[Theory]
	[MemberData(nameof(ThrottleGaugeTests.Cockpits), MemberType = typeof(ThrottleGaugeTests))]
	public void HddClipSpansEveryHercsHddScreen(string herc) {
		if (GameInstall.Locate(null) is not { } root) {
			return;
		}

		var content = GameContent.MountSimulator(root);
		var clip = new ClipRegionFileTransformer().Parse(content.Read("edg", "HDDCLIP.EDG")!);
		var gau = new GauFileTransformer().Parse(content.Read("gau", herc + ".GAU")!);
		var screen = HddLayout.Load(gau!, headsDownCanvasOriginY: 0)!.Screen;

		const int Shift = CockpitViewGeometry.CoordShift;
		var rows = Assert.Single(clip!.Blocks).Rows;
		Assert.Equal((screen.Y1 - screen.Y0) >> Shift, rows.Length - 1);
		Assert.Equal((screen.X1 - screen.X0) >> Shift, rows.Max(span => span.XEnd));
	}

	/// <summary>
	/// A file that ends inside its counts, or carries a negative count, reads as null rather than
	/// throwing, so a missing cutout is never mistaken for an empty one. Each row is caught by a
	/// different check: a rect count whose rects run past the end, a row count of -1, and a second
	/// block whose header is missing after the first block's spans.
	/// </summary>
	[Theory]
	[InlineData(new byte[] { 1, 0, 0, 0 })]
	[InlineData(new byte[] { 0, 0, 1, 0, 0, 0, 0xff, 0xff })]
	[InlineData(new byte[] { 0, 0, 2, 0, 0, 0, 1, 0, 5, 0, 9, 0 })]
	public void MalformedFileReadsAsNull(byte[] bytes) {
		Assert.Null(CockpitClipRegions.Parse(bytes));
	}

	/// <summary>A well-formed empty file (<c>APOCA.HD1</c>'s shape) reads as no spans, not as a missing one.</summary>
	[Fact]
	public void AnEmptyFileReadsAsNoSpans() {
		Assert.Equal(0, CockpitClipRegions.Parse([0, 0, 0, 0, 0, 0])?.SpanCount);
	}
}
