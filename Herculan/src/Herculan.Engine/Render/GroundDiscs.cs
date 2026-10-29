using System.Numerics;
using Herculan.Engine.Gl;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// One filled disc painted onto the ground: its centre in render X and Z, its radius in render units, and its colour,
/// whose alpha is how far the ground under it is blended toward that colour.
/// </summary>
public readonly record struct GroundDisc(Vector2 Centre, float Radius, Vector4 Color);

/// <summary>
/// Filled discs painted onto any item whose <see cref="SceneItem.ShowHighlight"/> is set, as <see cref="GroundHighlight"/>'s
/// tint is: over render X and Z alone, so a disc lies on whatever the surface under it does, whatever stands above it.
/// Later discs are blended over earlier ones. Ignored unless the renderer was built with the grid compiled in.
///
/// <para>The discs reach the scene program as a float texture rather than a uniform array, whose size GL bounds well below
/// the units a mission can place: row 0 holds each disc's centre and radius, row 1 its colour, one column per disc. At
/// most <see cref="MaxCount"/> are painted; the rest are left out.</para>
/// </summary>
public sealed class GroundDiscs {
	/// <summary>The most discs painted, the texture's width: the least width GL 3.3 guarantees.</summary>
	public const int MaxCount = 1024;

	private uint _texture;
	private int _capacity;
	private float[] _texels = [];

	/// <summary>The discs painted this frame, filled by the host before the pass.</summary>
	public List<GroundDisc> Items { get; } = new();

	/// <summary>How many of <see cref="Items"/> are painted.</summary>
	public int Count => Math.Min(Items.Count, MaxCount);

	/// <summary>Uploads <see cref="Items"/> and binds them to <paramref name="unit"/>, growing the texture when they outgrow it.</summary>
	internal void Bind(GL gl, ShaderProgram shader, uint unit) {
		int count = Count;
		if (_texture == 0) {
			_texture = gl.GenTexture();
		}

		gl.ActiveTexture(TextureUnit.Texture0 + (int)unit);
		gl.BindTexture(TextureTarget.Texture2D, _texture);
		if (count > _capacity) {
			_capacity = Math.Min(Math.Max(count, _capacity * 2), MaxCount);
			_texels = new float[_capacity * 2 * 4];
			unsafe {
				gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, (uint)_capacity, 2, 0, PixelFormat.Rgba,
					PixelType.Float, null);
			}

			gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
			gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
		}

		if (count > 0) {
			int row = _capacity * 4;
			for (int i = 0; i < count; i++) {
				var disc = Items[i];
				int at = i * 4;
				_texels[at] = disc.Centre.X;
				_texels[at + 1] = disc.Centre.Y;
				_texels[at + 2] = disc.Radius;
				_texels[row + at] = disc.Color.X;
				_texels[row + at + 1] = disc.Color.Y;
				_texels[row + at + 2] = disc.Color.Z;
				_texels[row + at + 3] = disc.Color.W;
			}

			unsafe {
				fixed (float* texels = _texels) {
					gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, (uint)_capacity, 2, PixelFormat.Rgba, PixelType.Float,
						texels);
				}
			}
		}

		shader.SetInt("uDiscs", (int)unit);
		shader.SetInt("uDiscCount", count);
		gl.ActiveTexture(TextureUnit.Texture0);
	}

	/// <summary>Frees the texture; <paramref name="gl"/> is the context it was made in.</summary>
	internal void Free(GL gl) {
		if (_texture != 0) {
			gl.DeleteTexture(_texture);
			_texture = 0;
			_capacity = 0;
		}
	}
}
