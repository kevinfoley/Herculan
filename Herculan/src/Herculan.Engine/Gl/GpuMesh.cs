using System.Numerics;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Gl;

/// <summary>
/// A static, non-indexed mesh living on the GPU — one vertex array object over one vertex buffer of
/// <see cref="MeshVertex"/>. Non-indexed because everything drawn so far is flat-shaded: adjacent
/// triangles need distinct normals at shared corners anyway, so an index buffer would share almost
/// nothing.
///
/// <para>The buffer holds the primitive ranges <see cref="Render.MeshBuild"/> lays out: filled
/// triangles, then line segments, the face outlines and after them the line polys, then single
/// points, and <see cref="Draw()"/> issues one call for each — see <see cref="Render.MeshBuild"/>
/// for what the outlines, the line polys and the points are and where they come from.
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
	/// <param name="standaloneLineVertexCount">How many vertices just before the points are line polys.</param>
	public GpuMesh(GL gl, ReadOnlySpan<MeshVertex> vertices, int triangleVertexCount = -1,
			int pointVertexCount = 0, int standaloneLineVertexCount = 0) {
		_gl = gl;
		VertexCount = vertices.Length;
		TriangleVertexCount = triangleVertexCount < 0
			? vertices.Length
			: Math.Clamp(triangleVertexCount, 0, vertices.Length);
		PointVertexCount = Math.Clamp(pointVertexCount, 0, VertexCount - TriangleVertexCount);
		StandaloneLineVertexCount = Math.Clamp(standaloneLineVertexCount, 0,
			VertexCount - TriangleVertexCount - PointVertexCount);
		if (!vertices.IsEmpty) {
			BoundsMin = BoundsMax = vertices[0].Position;
			Grow(vertices);
		}

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

	/// <summary>Uploads a mesh's vertices with its ranges.</summary>
	/// <param name="gl">The GL context.</param>
	/// <param name="ranges">The mesh's vertices and ranges.</param>
	public GpuMesh(GL gl, Render.IMeshRanges ranges)
		: this(gl, ranges.Vertices, ranges.TriangleVertexCount, ranges.PointVertexCount, ranges.StandaloneLineVertexCount) {
	}

	/// <summary>Number of vertices uploaded, across all the ranges.</summary>
	public int VertexCount { get; }

	/// <summary>Where the outline range starts; triangle count is a third of this.</summary>
	public int TriangleVertexCount { get; }

	/// <summary>How many vertices at the end of the buffer are drawn as single points.</summary>
	public int PointVertexCount { get; }

	/// <summary>How many vertices just before the points are line polys, drawn as segments.</summary>
	public int StandaloneLineVertexCount { get; }

	/// <summary>
	/// The least corner of a model-space box holding every vertex uploaded, which is how far along the view axis the
	/// mesh can reach — what the painter's-order draw fits each entry's depth range to.
	/// <see cref="Update"/> grows it and nothing shrinks it. Zero for an empty mesh.
	/// </summary>
	public Vector3 BoundsMin { get; private set; }

	/// <summary>The greatest corner of that box.</summary>
	public Vector3 BoundsMax { get; private set; }

	/// <summary>
	/// Replaces the uploaded vertices from a first vertex on, for a
	/// mesh whose geometry has changed in place; the ranges and the count stay as built.
	/// </summary>
	/// <param name="firstVertex">The index of the first vertex replaced.</param>
	/// <param name="vertices">The new vertices.</param>
	public void Update(int firstVertex, ReadOnlySpan<MeshVertex> vertices) {
		if (vertices.IsEmpty || firstVertex < 0 || firstVertex + vertices.Length > VertexCount) {
			return;
		}

		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
		_gl.BufferSubData(BufferTargetARB.ArrayBuffer, (nint)(firstVertex * MeshVertex.SizeInBytes), vertices);
		_gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
		Grow(vertices);
	}

	private void Grow(ReadOnlySpan<MeshVertex> vertices) {
		var min = BoundsMin;
		var max = BoundsMax;
		foreach (var vertex in vertices) {
			min = Vector3.Min(min, vertex.Position);
			max = Vector3.Max(max, vertex.Position);
		}

		BoundsMin = min;
		BoundsMax = max;
	}

	/// <summary>Draws every range once, each line and point one pixel wide.</summary>
	public void Draw() => Draw(1, null);

	/// <summary>
	/// Draws every range, the line polys and the points the pixel scale wide: the
	/// points at that point size, and the line polys once at each of scale × scale
	/// whole-pixel offsets centred on the line, each set through the offset callback
	/// before its draw and zero put back after the last. The face
	/// outlines stay one pixel wide. A core-profile context draws no line wider than one pixel, which
	/// is why the line polys are drawn several times rather than through <c>glLineWidth</c>.
	/// </summary>
	/// <param name="pixelScale">The width of a point and the side of the square of line offsets, in pixels.</param>
	/// <param name="setPixelOffset">Sets the pixel offset before a draw; null for none.</param>
	public void Draw(int pixelScale, Action<Vector2>? setPixelOffset) {
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
			bool wide = pixelScale > 1 && setPixelOffset != null;
			int outlineVertexCount = lineVertexCount - StandaloneLineVertexCount;
			if (!wide && lineVertexCount > 0) {
				_gl.DrawArrays(PrimitiveType.Lines, TriangleVertexCount, (uint)lineVertexCount);
			} else if (wide) {
				if (outlineVertexCount > 0) {
					_gl.DrawArrays(PrimitiveType.Lines, TriangleVertexCount, (uint)outlineVertexCount);
				}

				if (StandaloneLineVertexCount > 0) {
					DrawWideLines(TriangleVertexCount + outlineVertexCount, pixelScale, setPixelOffset!);
				}
			}

			if (PointVertexCount > 0) {
				if (wide) {
					_gl.PointSize(pixelScale);
				}

				_gl.DrawArrays(PrimitiveType.Points, VertexCount - PointVertexCount, (uint)PointVertexCount);
				if (wide) {
					_gl.PointSize(1f);
				}
			}

			_gl.DepthFunc(DepthFunction.Less);
		}

		_gl.BindVertexArray(0);
	}

	/// <summary>The line polys from a first vertex, once at each offset of a square of pixels.</summary>
	/// <param name="first">The first vertex.</param>
	/// <param name="pixelScale">The side of the square of offsets, in pixels.</param>
	/// <param name="setPixelOffset">Sets the pixel offset before a draw.</param>
	private void DrawWideLines(int first, int pixelScale, Action<Vector2> setPixelOffset) {
		float centre = (pixelScale - 1) / 2f;
		for (int row = 0; row < pixelScale; row++) {
			for (int column = 0; column < pixelScale; column++) {
				setPixelOffset(new Vector2(column - centre, row - centre));
				_gl.DrawArrays(PrimitiveType.Lines, first, (uint)StandaloneLineVertexCount);
			}
		}

		setPixelOffset(Vector2.Zero);
	}

	public void Dispose() {
		_gl.DeleteBuffer(_vertexBuffer);
		_gl.DeleteVertexArray(_vertexArray);
	}
}
