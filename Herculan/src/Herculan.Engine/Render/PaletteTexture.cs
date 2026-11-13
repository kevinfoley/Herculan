using HercWorks.Core.Data.File.Dyn;
using Herculan.Engine.Gl;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// A palette on the GPU, 256 texels in one row, entry <i>n</i> at column <i>n</i>: what a pass that draws palette
/// indices looks its colors up in, so showing another palette (<see cref="Show"/>) recolors everything drawn through it
/// from the next draw, as a DAC does an 8-bit screen. An entry the palette lacks is black.
/// </summary>
public sealed class PaletteTexture : IDisposable {
	private readonly GpuTexture _texture;

	public PaletteTexture(GL gl, DynamixPalette palette) {
		_texture = new GpuTexture(gl, Bytes(palette), 256, 1);
	}

	/// <summary>The GL texture name, for <see cref="SpriteBatch.PaletteHandle"/>.</summary>
	public uint Handle => _texture.Handle;

	/// <summary>Shows <paramref name="palette"/> from the next draw.</summary>
	public void Show(DynamixPalette palette) => _texture.Update(Bytes(palette), 256, 1);

	private static byte[] Bytes(DynamixPalette palette) {
		var rgba = new byte[256 * 4];
		for (int index = 0; index < 256; index++) {
			if (palette.Colors.TryGetValue(index, out var entry)) {
				var color = entry.GetColor();
				rgba[index * 4] = color.R;
				rgba[index * 4 + 1] = color.G;
				rgba[index * 4 + 2] = color.B;
			}

			rgba[index * 4 + 3] = 255;
		}

		return rgba;
	}

	public void Dispose() => _texture.Dispose();
}
