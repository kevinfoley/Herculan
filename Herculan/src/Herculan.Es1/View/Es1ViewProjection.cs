using Herculan.Engine.Numerics;

namespace Herculan.Es1.View;

/// <summary>
/// The installed view as <c>Raster_InstallViewProjection</c> (<c>ES1DBSIM 0005bf68</c>) leaves it for a camera, ES2's
/// (docs/es1/rendering/view-transform-and-projection.md, docs/retail/rendering/view-transform-and-projection.md#installing-a-camera):
/// the world-to-view transform <c>Raster_WorldToView</c> (<c>000b5e04</c>), the projection centre
/// <c>Raster_ProjectionCentre</c> (<c>000b5ee0</c>), the render context's rect's top-left less its pair, and the main
/// camera's focal shift 8. With the identity model transform installed, <see cref="ModelToView"/>,
/// <see cref="PerspectiveDivide"/> and <see cref="ProjectToScreen"/> take a world point to the screen in retail's integer
/// arithmetic, as the 2D draws over the scene project their points; the 3D pass draws the world itself on the GPU from
/// the same camera and window (<see cref="Es1Camera.ApplyTo"/>).
/// </summary>
public sealed class Es1ViewProjection {
	/// <summary><c>Raster_FocalShift</c>, the main camera's 8: the focal length is 2⁸ pixels.</summary>
	public const int FocalShift = 8;

	private Es1ViewProjection(Transform3 worldToView, int centreX, int centreY) {
		WorldToView = worldToView;
		CentreX = centreX;
		CentreY = centreY;
	}

	/// <summary><c>Raster_WorldToView</c>: the camera's transform inverted.</summary>
	public Transform3 WorldToView { get; }

	/// <summary><c>Raster_ProjectionCentre</c>'s <i>x</i>.</summary>
	public int CentreX { get; }

	/// <summary>Its <i>y</i>.</summary>
	public int CentreY { get; }

	/// <summary>
	/// <c>Raster_InstallViewProjection</c>(<paramref name="camera"/>) through the render context's
	/// <paramref name="window"/>: the camera's transform, <c>BuildEulerRotationMatrixQ14</c> of its angles <c>+0xc</c> at
	/// its position, inverted inline (the rotation transposed, the translation negated and rotated through it), and the
	/// window's projection centre.
	/// </summary>
	public static Es1ViewProjection Install(Es1Camera camera, Es1ViewWindow window) {
		var (x, y, z) = camera.View;
		var frame = Transform3.FromEuler(x, y, z);
		frame.X = camera.Position.X;
		frame.Y = camera.Position.Y;
		frame.Z = camera.Position.Z;
		return new Es1ViewProjection(frame.Inverted(), window.CentreX, window.CentreY);
	}

	/// <summary>
	/// <c>Raster_ModelToView</c> (<c>0005c290</c>) under the identity model transform (<c>Raster_SetModelTransform</c>(0)),
	/// where <c>g_ModelToView</c> is the world-to-view transform: x across, y the depth, z up.
	/// </summary>
	public Vec3i ModelToView(Vec3i point) => WorldToView.TransformPoint(point.X, point.Y, point.Z);

	/// <summary>
	/// <c>Raster_PerspectiveDivide</c> (<c>0005c318</c>): {(<i>x</i> &lt;&lt; 8) / <i>y</i>, (<i>z</i> &lt;&lt; 8) /
	/// <i>y</i>}, 32-bit, the division truncating.
	/// </summary>
	public static (int X, int Y) PerspectiveDivide(Vec3i view) =>
		(unchecked(view.X << FocalShift) / view.Y, unchecked(view.Z << FocalShift) / view.Y);

	/// <summary><c>Raster_ProjectToScreen</c> (<c>0005c410</c>): {<i>x</i> + <i>cx</i>, <i>cy</i> − <i>y</i>}.</summary>
	public (int X, int Y) ProjectToScreen((int X, int Y) divided) => (divided.X + CentreX, CentreY - divided.Y);
}
