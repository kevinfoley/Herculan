using System.Numerics;
using Herculan.Engine.Render;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Catches an edit to <see cref="TextureWalkTable.Pack"/> that moves a field off the offset Scene.glsl's
/// <c>walkTexel</c> reads, or files a triangle's face under another triangle's <c>gl_PrimitiveID</c>.
/// </summary>
public class TextureWalkTableTests {
	[Fact]
	public void EachTriangleOwnsTheRecordAtItsPrimitiveId() {
		var face = new TextureWalkFace(
			[new Vector3(1, 2, 3), new Vector3(4, 5, 6), new Vector3(7, 8, 9), new Vector3(10, 11, 12)],
			[new Vector2(0, 0), new Vector2(31, 0), new Vector2(31, 15), new Vector2(0, 15)],
			FrameX: 64, FrameY: 128, Columns: 32, Rows: 16, LitAsBack: true);

		var table = TextureWalkTable.Pack([null, face]);
		const int record = TextureWalkTable.TexelsPerTriangle * 4;
		Assert.Equal(2 * record, table.Length);

		// A triangle with no face reads a corner count of 0, which walkTexel takes as "fill through your own mapping".
		Assert.Equal(0f, table[0]);

		var packed = table.AsSpan(record);
		Assert.Equal([4f, 64f, 128f, 1f], packed[..4].ToArray());
		Assert.Equal([32f, 16f], packed[4..6].ToArray());
		for (int i = 0; i < 4; i++) {
			Assert.Equal(face.Corners[i], new Vector3(packed[(2 + i) * 4], packed[(2 + i) * 4 + 1], packed[(2 + i) * 4 + 2]));
			Assert.Equal(face.TexturePoints[i], new Vector2(packed[24 + i * 2], packed[25 + i * 2]));
		}
	}
}
