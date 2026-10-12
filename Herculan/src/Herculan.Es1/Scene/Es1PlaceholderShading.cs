using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Content;
using Herculan.Engine.Render;

namespace Herculan.Es1.Scene;

/// <summary>
/// PLACEHOLDER: how ES1 colours a shape's polygons is Open. Its poly draw functions are unread, it ships no
/// <c>rmp\world&lt;n&gt;</c>, and its light model is unread (docs/es1/formats/dts-shape.md#open,
/// docs/es1/formats/wld-world.md#open). Until those are read, ES1's shapes go through ES2's poly rules and ES2's sun
/// with a ramp that passes every palette index through unchanged at every shade: <see cref="SurfaceShading"/> with an
/// identity <see cref="ShadeRamp"/>. The colours this gives are unverified against retail ES1.
/// </summary>
public static class Es1PlaceholderShading {
	/// <summary>The identity ramp's rows: as many as a retail ES2 ramp has, so ES2's row arithmetic is unchanged.</summary>
	public const int ShadeLevels = 32;

	/// <summary>One depth slice: the ramp carries no distance fade, since ES1's is Open.</summary>
	public const int DepthSlices = 1;

	/// <summary><paramref name="palette"/> with the identity ramp.</summary>
	public static SurfaceShading For(DynamixPalette? palette) {
		var rows = new byte[ShadeLevels * DepthSlices * ShadeRamp.RowLength];
		for (int i = 0; i < rows.Length; i++) {
			rows[i] = (byte)(i % ShadeRamp.RowLength);
		}

		return new SurfaceShading(ShadeRamp.FromRows(ShadeLevels, DepthSlices, rows), palette);
	}
}
