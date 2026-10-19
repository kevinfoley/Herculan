using Silk.NET.OpenGL;

namespace Herculan.Engine.Gl;

/// <summary>
/// A mesh's <see cref="Render.TextureWalkTable"/> on the GPU: an RGBA32F buffer texture, read by
/// <c>gl_PrimitiveID</c> through <c>texelFetch</c>. A buffer texture because the scene program's sixteen vertex
/// attributes are all taken, and a face's whole ring cannot ride on the three corners of each of its triangles.
/// </summary>
public sealed class GpuTextureWalkTable : IDisposable {
	private readonly GL _gl;
	private readonly uint _buffer;

	public GpuTextureWalkTable(GL gl, ReadOnlySpan<float> table) {
		_gl = gl;
		_buffer = gl.GenBuffer();
		gl.BindBuffer(BufferTargetARB.TextureBuffer, _buffer);
		gl.BufferData(BufferTargetARB.TextureBuffer, table, BufferUsageARB.StaticDraw);
		gl.BindBuffer(BufferTargetARB.TextureBuffer, 0);

		Handle = gl.GenTexture();
		gl.BindTexture(TextureTarget.TextureBuffer, Handle);
		gl.TexBuffer(TextureTarget.TextureBuffer, SizedInternalFormat.Rgba32f, _buffer);
		gl.BindTexture(TextureTarget.TextureBuffer, 0);
	}

	/// <summary>The buffer texture, bound to <see cref="TextureTarget.TextureBuffer"/>.</summary>
	public uint Handle { get; }

	public void Dispose() {
		_gl.DeleteTexture(Handle);
		_gl.DeleteBuffer(_buffer);
	}
}
