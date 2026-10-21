using System.Numerics;

namespace Herculan.Engine.Render;

/// <summary>
/// A textured face's whole ring as the original hands its bitmap fill: the corners in the mesh's own space and the
/// texture point each carries, in texels of its frame, for Scene.glsl to fill the face row by row in screen space
/// (docs/es1/rendering/polygon-shading.md#bitmap-fills-brush-modes-5-and-7) rather than through the triangles' own
/// mapping. Every triangle fanned out of the face carries the same ring, so all of them fill from the one walk.
/// </summary>
/// <param name="Corners">The ring's three or four corners, in the space the mesh's vertices are written in.</param>
/// <param name="TexturePoints">Each corner's texture point, in texels: the frame's corners (0, 0), (columns − 1, 0),
/// (columns − 1, rows − 1) and (0, rows − 1).</param>
/// <param name="FrameX">The frame's first column in the atlas.</param>
/// <param name="FrameY">The frame's first row in the atlas.</param>
/// <param name="Columns">The frame's columns.</param>
/// <param name="Rows">The frame's rows.</param>
/// <param name="LitAsBack">Whether the copy draws the poly's back value while the poly faces the eye — see
/// <see cref="Gl.MeshVertex.Side"/>. A copy seen from the poly's back draws the back value too, which the shader
/// knows from the facing.</param>
public sealed record TextureWalkFace(Vector3[] Corners, Vector2[] TexturePoints, int FrameX, int FrameY, int Columns,
	int Rows, bool LitAsBack = false);

/// <summary>
/// Lays out a mesh's <see cref="TextureWalkFace"/>s as the buffer texture Scene.glsl reads by <c>gl_PrimitiveID</c>:
/// one record of <see cref="TexelsPerTriangle"/> RGBA texels per triangle, in the mesh's triangle order, which is the
/// order <see cref="Gl.GpuMesh.Draw"/>'s single triangle call numbers them in.
/// </summary>
public static class TextureWalkTable {
	/// <summary>The RGBA texels a triangle's record takes; Scene.glsl's <c>WALK_TEXELS</c>.</summary>
	public const int TexelsPerTriangle = 8;

	/// <summary>The most corners a ring has; Scene.glsl's <c>WALK_CORNERS</c>.</summary>
	public const int MaxCorners = 4;

	/// <summary>
	/// The table for <paramref name="perTriangle"/>, the face each triangle was fanned out of or null for a triangle
	/// filled through its own mapping. A record is: the corner count (0 for null), the frame's first column and row,
	/// 1 for <see cref="TextureWalkFace.LitAsBack"/>;
	/// its columns and rows; the four corners; the four texture points, two to a texel.
	/// </summary>
	public static float[] Pack(IReadOnlyList<TextureWalkFace?> perTriangle) {
		var table = new float[perTriangle.Count * TexelsPerTriangle * 4];
		for (int t = 0; t < perTriangle.Count; t++) {
			if (perTriangle[t] is not { } face) {
				continue;
			}

			int count = Math.Min(face.Corners.Length, MaxCorners);
			int at = t * TexelsPerTriangle * 4;
			table[at] = count;
			table[at + 1] = face.FrameX;
			table[at + 2] = face.FrameY;
			table[at + 3] = face.LitAsBack ? 1f : 0f;
			table[at + 4] = face.Columns;
			table[at + 5] = face.Rows;
			for (int i = 0; i < count; i++) {
				int corner = at + (2 + i) * 4;
				table[corner] = face.Corners[i].X;
				table[corner + 1] = face.Corners[i].Y;
				table[corner + 2] = face.Corners[i].Z;

				int point = at + 6 * 4 + i * 2;
				table[point] = face.TexturePoints[i].X;
				table[point + 1] = face.TexturePoints[i].Y;
			}
		}

		return table;
	}
}
