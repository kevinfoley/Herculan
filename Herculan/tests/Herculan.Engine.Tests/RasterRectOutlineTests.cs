using System.Numerics;
using Herculan.Engine.Render;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// <see cref="RasterPrimitives.AddRectOutline"/>: the pixels <c>Raster_FillRect</c> in brush mode 4 sets for an
/// inclusive rect, which the MFD status screen's Targeting Pod highlight draws around a paper-doll region.
/// </summary>
public class RasterRectOutlineTests {
	[Fact]
	public void SetsExactlyTheInclusiveRectsBoundary() {
		// A region at GAU (3, 4)-(6, 8): device (6, 8)-(13, 17) inclusive, grown one pixel to (5, 7)-(14, 18).
		var pixels = new HashSet<(int X, int Y)>();
		RasterPrimitives.AddRectOutline(5, 7, 14, 18, Vector3.One, 100f, 200f, (x0, y0, x1, y1, _) => {
			for (int y = (int)y0; y < (int)y1; y++) {
				for (int x = (int)x0; x < (int)x1; x++) {
					Assert.True(pixels.Add((x - 100, y - 200)), $"({x}, {y}) drawn twice");
				}
			}
		});

		var expected = new HashSet<(int X, int Y)>();
		for (int y = 7; y <= 18; y++) {
			for (int x = 5; x <= 14; x++) {
				if (x == 5 || x == 14 || y == 7 || y == 18) {
					expected.Add((x, y));
				}
			}
		}

		Assert.Equal(expected, pixels);
	}
}
