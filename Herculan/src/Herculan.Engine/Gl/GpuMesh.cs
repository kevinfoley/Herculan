using Silk.NET.OpenGL;

namespace Herculan.Engine.Gl;

/// <summary>
/// A static, non-indexed mesh living on the GPU — one vertex array object over one vertex buffer of
/// <see cref="MeshVertex"/>. Non-indexed because everything drawn so far is flat-shaded: adjacent
/// triangles need distinct normals at shared corners anyway, so an index buffer would share almost
/// nothing.
///
/// <para>The buffer holds three primitive ranges, filled triangles, then outline line-segments, then
/// single points, and <see cref="Draw"/> issues one call for each — see
/// <see cref="Render.MeshBuild"/> for what the outline and the points are and where they come from.
/// One buffer rather than three because the ranges always travel together: they are the passes the
/// original draws one shape in.</para>
/// </summary>
public sealed class GpuMesh : IDisposable {
	private readonly GL _gl;
	private readonly uint _vertexArray;
	private readonly uint _vertexBuffer;

	/// <param name="triangleVertexCount">
	/// How many leading vertices are triangle corners; the rest are line-segment endpoint pairs and
	/// then points. Negative (the default) means the whole buffer is triangles.
	/// </param>
	/// <param name="pointVertexCount">How many trailing vertices are single points.</param>
	public GpuMesh(GL gl, ReadOnlySpan<MeshVertex> vertices, int triangleVertexCount = -1,
			int pointVertexCount = 0) {
		_gl = gl;
		VertexCount = vertices.Length;
		TriangleVertexCount = triangleVertexCount < 0
			? vertices.Length
			: Math.Clamp(triangleVertexCount, 0, vertices.Length);
		PointVertexCount = Math.Clamp(pointVertexCount, 0, VertexCount - TriangleVertexCount);

		_vertexArray = _gl.GenVertexArray();
		_gl.BindVertexArray(_vertexArray);

		_vertexBuffer = _gl.GenBuffer();
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
		_gl.BufferData(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);

		unsafe {
			_gl.EnableVertexAttribArray(0);
			_gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)0);

			_gl.EnableVertexAttribArray(1);
			_gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(3 * sizeof(float)));

			_gl.EnableVertexAttribArray(2);
			_gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(6 * sizeof(float)));

			_gl.EnableVertexAttribArray(3);
			_gl.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(9 * sizeof(float)));

			_gl.EnableVertexAttribArray(4);
			_gl.VertexAttribPointer(4, 1, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(11 * sizeof(float)));

			_gl.EnableVertexAttribArray(5);
			_gl.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(12 * sizeof(float)));

			_gl.EnableVertexAttribArray(6);
			_gl.VertexAttribPointer(6, 1, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(13 * sizeof(float)));

			_gl.EnableVertexAttribArray(7);
			_gl.VertexAttribPointer(7, 1, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(14 * sizeof(float)));

			_gl.EnableVertexAttribArray(8);
			_gl.VertexAttribPointer(8, 3, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(15 * sizeof(float)));

			_gl.EnableVertexAttribArray(9);
			_gl.VertexAttribPointer(9, 1, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(18 * sizeof(float)));

			_gl.EnableVertexAttribArray(10);
			_gl.VertexAttribPointer(10, 1, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(19 * sizeof(float)));

			_gl.EnableVertexAttribArray(11);
			_gl.VertexAttribPointer(11, 3, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(20 * sizeof(float)));

			_gl.EnableVertexAttribArray(12);
			_gl.VertexAttribPointer(12, 1, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(23 * sizeof(float)));

			_gl.EnableVertexAttribArray(13);
			_gl.VertexAttribPointer(13, 1, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(24 * sizeof(float)));

			_gl.EnableVertexAttribArray(14);
			_gl.VertexAttribPointer(14, 4, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(25 * sizeof(float)));

			_gl.EnableVertexAttribArray(15);
			_gl.VertexAttribPointer(15, 1, VertexAttribPointerType.Float, false, MeshVertex.SizeInBytes, (void*)(29 * sizeof(float)));
		}

		_gl.BindVertexArray(0);
	}

	/// <summary>Number of vertices uploaded, across all three ranges.</summary>
	public int VertexCount { get; }

	/// <summary>Where the outline range starts; triangle count is a third of this.</summary>
	public int TriangleVertexCount { get; }

	/// <summary>How many vertices at the end of the buffer are drawn as single points.</summary>
	public int PointVertexCount { get; }

	public void Draw() {
		if (VertexCount == 0) {
			return;
		}

		_gl.BindVertexArray(_vertexArray);

		if (TriangleVertexCount > 0) {
			_gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)TriangleVertexCount);
		}

		int lineVertexCount = VertexCount - TriangleVertexCount - PointVertexCount;
		if (lineVertexCount > 0 || PointVertexCount > 0) {
			// An outline is coplanar with the face it outlines, so at the default GL_LESS it would
			// lose the depth test to the fill it is supposed to cover. GL_LEQUAL for this range makes
			// the later draw win the tie, which is the original's behaviour by construction — it has
			// no depth buffer inside a shape at all, it just paints the second pass over the first.
			// The points are the same pass's one-vertex case.
			_gl.DepthFunc(DepthFunction.Lequal);
			if (lineVertexCount > 0) {
				_gl.DrawArrays(PrimitiveType.Lines, TriangleVertexCount, (uint)lineVertexCount);
			}

			if (PointVertexCount > 0) {
				_gl.DrawArrays(PrimitiveType.Points, VertexCount - PointVertexCount, (uint)PointVertexCount);
			}

			_gl.DepthFunc(DepthFunction.Less);
		}

		_gl.BindVertexArray(0);
	}

	public void Dispose() {
		_gl.DeleteBuffer(_vertexBuffer);
		_gl.DeleteVertexArray(_vertexArray);
	}
}
