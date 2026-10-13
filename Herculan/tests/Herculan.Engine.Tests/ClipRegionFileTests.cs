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
	/// No rects, one span block from row 0 whose row count is the file's scanline count, then the
	/// int16 0 the loader does not read. The engine's independent parser agrees on the row count.
	/// </summary>
	[Theory]
	[InlineData("HDDCLIP.EDG", 101)]
	[InlineData("MFDCLIP.EDG", 61)]
	public void EdgFileIsOneSpanBlock(string name, int rows) {
		if (GameInstall.Locate(null) is not { } root
			|| GameContent.MountSimulator(root).Read("edg", name) is not { } bytes) {
			return;
		}

		var transformer = new ClipRegionFileTransformer();
		var file = transformer.Parse(bytes);

		Assert.NotNull(file);
		Assert.Empty(file.Rects);
		var block = Assert.Single(file.Blocks);
		Assert.Equal((0, rows), (block.FirstRow, block.Rows.Length));
		Assert.Equal(new byte[] { 0, 0 }, file.TrailingBytes);
		Assert.Equal(rows, CockpitClipRegions.Parse(bytes)?.RowCount);
		Assert.Equal(bytes, transformer.Write(file));
	}
}
