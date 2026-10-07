using Silk.NET.OpenGL;

namespace Herculan.Engine.Gl;

/// <summary>
/// A dynamic, non-indexed triangle mesh of <see cref="Overlay2DVertex"/>, re-uploaded every draw —
/// the 2D-overlay counterpart to <see cref="GpuMesh"/>, which is static. Dynamic because
/// <see cref="Render.Overlay2DRenderer"/> submits a different vertex list per panel (mirrored vs. not,
/// widgets vs. none), and the vertex counts involved (one quad plus a couple dozen widget outlines)
/// are far too small for re-uploading each frame to matter.
/// </summary>
public sealed class GpuOverlayMesh : IDisposable {
	private readonly GL _gl;
	private readonly uint _vertexArray;
	private readonly uint _vertexBuffer;
	private int _vertexCount;

	public GpuOverlayMesh(GL gl) {
		_gl = gl;

		_vertexArray = _gl.GenVertexArray();
		_gl.BindVertexArray(_vertexArray);

		_vertexBuffer = _gl.GenBuffer();
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);

		unsafe {
			_gl.EnableVertexAttribArray(0);
			_gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Overlay2DVertex.SizeInBytes, (void*)0);

			_gl.EnableVertexAttribArray(1);
			_gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Overlay2DVertex.SizeInBytes, (void*)(2 * sizeof(float)));

			_gl.EnableVertexAttribArray(2);
			_gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, Overlay2DVertex.SizeInBytes, (void*)(4 * sizeof(float)));

			_gl.EnableVertexAttribArray(3);
			_gl.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, Overlay2DVertex.SizeInBytes, (void*)(7 * sizeof(float)));
		}

		_gl.BindVertexArray(0);
	}

	/// <summary>Replaces the buffer's contents and draws it immediately.</summary>
	public void SubmitAndDraw(ReadOnlySpan<Overlay2DVertex> vertices) {
		_vertexCount = vertices.Length;
		if (_vertexCount == 0) {
			return;
		}

		_gl.BindVertexArray(_vertexArray);
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
		_gl.BufferData(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.DynamicDraw);

		_gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
		_gl.BindVertexArray(0);
	}

	public void Dispose() {
		_gl.DeleteBuffer(_vertexBuffer);
		_gl.DeleteVertexArray(_vertexArray);
	}
}
