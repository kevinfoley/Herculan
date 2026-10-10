using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HercWorks.UI;

/// <summary>One loaded root mesh plus a user-toggleable visibility flag.</summary>
public sealed class ViewerMesh {
	public DtsRootMesh Mesh { get; }
	public bool Visible { get; set; } = true;

	public ViewerMesh(DtsRootMesh mesh) {
		Mesh = mesh;
	}
}

public enum Model3DRenderMode {
	Shaded,
	Wireframe,
	ShadedWireframe
}

/// <summary>
/// Software-rasterized orbit-camera viewer for parsed DTS geometry (see DtsGeometryBuilder). No
/// GPU/extra dependency needed — plenty fast in plain C# at ES2's low poly counts.
///
/// Fill uses a real per-pixel depth (Z-)buffer, not a triangle-level painter's-algorithm sort.
/// Sorting whole triangles by average depth looks fine on a single isolated convex shape but
/// flickers wildly on real meshes: a single global order cannot be correct for triangles that
/// overlap or interpenetrate in screen space — any such pair has pixels where either order is
/// wrong, and which one "wins" the sort flips with tiny camera changes. A
/// depth buffer sidesteps that entirely by testing per pixel instead of per triangle. The ground
/// grid and wireframe edge overlay still go through GDI+ directly, since they're not competing for
/// overlapping fill pixels the way triangle interiors are.
///
/// Front or back is decided per triangle per frame from its poly's stored normal against the eye,
/// as <c>TSPoly_FrontBackVisibilityTest</c> does, and the chosen side is drawn unless it is the
/// format's hidden pair (see DtsGeometryBuilder). Colours arrive already resolved; only a side
/// built without a palette (<see cref="DtsFace.Lit"/> false) gets this control's own headlight
/// shading as a placeholder.
/// </summary>
public sealed class Model3DViewerControl : Control {
	private readonly List<ViewerMesh> _roots = new();
	private Vector3 _baseCenter = Vector3.Zero;
	private float _boundingRadius = 1f;
	private Vector3 _panOffset = Vector3.Zero;
	private float _yaw;
	private float _pitch;
	private float _distance = 5f;

	private bool _leftDown;
	private bool _middleDown;
	private Point _lastMouse;

	private const float RotateSpeed = 0.008f;
	private const float MaxPitch = MathF.PI * 0.49f;
	private const float PanClampFactor = 2f;
	private const float MinZoomFactor = 0.15f;
	private const float MaxZoomFactor = 20f;
	private const float DefaultYaw = -MathF.PI * 0.25f;
	private const float DefaultPitch = MathF.PI * 0.18f;

	public Model3DRenderMode RenderMode { get; set; } = Model3DRenderMode.ShadedWireframe;

	public IReadOnlyList<ViewerMesh> Roots => _roots;

	public Model3DViewerControl() {
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
			ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
		TabStop = true;
		BackColor = Color.FromArgb(30, 30, 34);
	}

	public void LoadMeshes(IEnumerable<DtsRootMesh> meshes) {
		_roots.Clear();
		foreach (var mesh in meshes) {
			_roots.Add(new ViewerMesh(mesh));
		}

		var allTriangles = _roots.SelectMany(r => r.Mesh.Triangles);
		(_baseCenter, _boundingRadius) = DtsGeometryBuilder.ComputeBounds(allTriangles);

		ResetView();
	}

	public void SetRootVisible(int index, bool visible) {
		if (index >= 0 && index < _roots.Count) {
			_roots[index].Visible = visible;
			Invalidate();
		}
	}

	/// <summary>
	/// Swaps in newly-rebuilt geometry for one root in place — e.g. after the user picks a
	/// different Detail Level for the currently-selected part. Preserves that root's visibility
	/// flag and deliberately does not recompute the camera's bounding sphere/framing, so switching
	/// detail levels (which shouldn't change the object's overall size much) doesn't jar the view.
	/// </summary>
	public void ReplaceRoot(int index, DtsRootMesh newMesh) {
		if (index < 0 || index >= _roots.Count) {
			return;
		}

		_roots[index] = new ViewerMesh(newMesh) { Visible = _roots[index].Visible };
		Invalidate();
	}

	public void ResetView() {
		_yaw = DefaultYaw;
		_pitch = DefaultPitch;
		_distance = _boundingRadius * 2.5f;
		_panOffset = Vector3.Zero;
		Invalidate();
	}

	protected override void OnMouseEnter(EventArgs e) {
		base.OnMouseEnter(e);
		// MouseWheel only reaches the focused control by default — grab focus on hover so
		// scrolling works without requiring a click first.
		if (CanFocus) {
			Focus();
		}
	}

	protected override void OnMouseDown(MouseEventArgs e) {
		base.OnMouseDown(e);
		_lastMouse = e.Location;
		if (e.Button == MouseButtons.Left) {
			_leftDown = true;
		} else if (e.Button == MouseButtons.Middle) {
			_middleDown = true;
		}
	}

	protected override void OnMouseUp(MouseEventArgs e) {
		base.OnMouseUp(e);
		if (e.Button == MouseButtons.Left) {
			_leftDown = false;
		} else if (e.Button == MouseButtons.Middle) {
			_middleDown = false;
		}
	}

	protected override void OnMouseMove(MouseEventArgs e) {
		base.OnMouseMove(e);
		int dx = e.X - _lastMouse.X;
		int dy = e.Y - _lastMouse.Y;
		_lastMouse = e.Location;

		if (_leftDown) {
			_yaw -= dx * RotateSpeed;
			_pitch = Math.Clamp(_pitch + dy * RotateSpeed, -MaxPitch, MaxPitch);
			Invalidate();
		} else if (_middleDown) {
			Pan(dx, dy);
			Invalidate();
		}
	}

	protected override void OnMouseWheel(MouseEventArgs e) {
		base.OnMouseWheel(e);
		float factor = MathF.Pow(1.0012f, -e.Delta);
		_distance = Math.Clamp(_distance * factor, _boundingRadius * MinZoomFactor, _boundingRadius * MaxZoomFactor);
		Invalidate();
	}

	protected override void OnDoubleClick(EventArgs e) {
		base.OnDoubleClick(e);
		ResetView();
	}

	private Vector3 OrbitDirection() {
		float cp = MathF.Cos(_pitch);
		return new Vector3(cp * MathF.Sin(_yaw), MathF.Sin(_pitch), cp * MathF.Cos(_yaw));
	}

	private void Pan(int dx, int dy) {
		Vector3 dir = OrbitDirection();
		Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
		if (float.IsNaN(right.X)) {
			right = Vector3.UnitX;
		}
		Vector3 up = Vector3.Normalize(Vector3.Cross(dir, right));

		float panScale = _distance * 0.0025f;
		Vector3 candidate = _panOffset + (-dx * right + dy * up) * panScale;

		float maxPan = _boundingRadius * PanClampFactor;
		if (candidate.Length() > maxPan) {
			candidate = Vector3.Normalize(candidate) * maxPan;
		}

		_panOffset = candidate;
	}

	protected override void OnPaint(PaintEventArgs e) {
		var g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.Clear(BackColor);

		int width = Width, height = Height;
		if (width <= 0 || height <= 0 || _roots.Count == 0) {
			return;
		}

		Vector3 target = _baseCenter + _panOffset;
		Vector3 dir = OrbitDirection();
		Vector3 eye = target + dir * _distance;
		Matrix4x4 view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);

		float near = MathF.Max(_boundingRadius * 0.01f, 0.01f);
		float far = _boundingRadius * 40f + _distance * 2f;
		float aspect = MathF.Max((float)width / height, 0.01f);
		Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, aspect, near, far);

		Vector3 lightDir = Vector3.Normalize(eye - target);

		DrawGroundGrid(g, view, proj, near);

		bool drawFill = RenderMode != Model3DRenderMode.Wireframe;
		bool drawEdges = RenderMode != Model3DRenderMode.Shaded;

		// depthBuffer holds 1/w per pixel (larger = closer; matches TryProject's invW) — 0 means
		// "nothing drawn here yet", since every surviving vertex has a strictly positive w.
		float[]? depthBuffer = null;

		if (drawFill) {
			var pixels = new int[width * height];
			depthBuffer = new float[width * height];

			foreach (var root in _roots) {
				if (!root.Visible) {
					continue;
				}

				foreach (var tri in root.Mesh.Triangles) {
					RasterizeTriangle(tri, eye, view, proj, lightDir, near, pixels, depthBuffer, width, height);
				}
			}

			foreach (var root in _roots) {
				if (!root.Visible) {
					continue;
				}

				foreach (var line in root.Mesh.Lines) {
					RasterizeLine(line, eye, view, proj, near, pixels, depthBuffer, width, height);
				}
			}

			using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
			var bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
			bmp.UnlockBits(bmpData);

			g.DrawImageUnscaled(bmp, 0, 0);
		}

		if (drawEdges) {
			DrawWireframeOverlay(g, eye, view, proj, near, width, height, depthBuffer);
		}
	}

	/// <summary>
	/// Scan-converts one triangle directly into the pixel/depth buffers via a standard
	/// edge-function rasterizer, testing every candidate pixel's interpolated 1/w against the
	/// buffer instead of relying on a whole-triangle draw order.
	/// </summary>
	private static void RasterizeTriangle(DtsTriangle tri, Vector3 eye, Matrix4x4 view, Matrix4x4 proj, Vector3 lightDir,
			float near, int[] pixels, float[] depthBuffer, int width, int height) {
		bool facingEye = Vector3.Dot(tri.FaceNormal, eye - tri.FacingPoint) >= 0f;
		DtsFace face = facingEye ? tri.Front : tri.Back;
		if (!face.Draw) {
			return;
		}

		Vector3 ca = Vector3.Transform(tri.A, view);
		Vector3 cb = Vector3.Transform(tri.B, view);
		Vector3 cc = Vector3.Transform(tri.C, view);

		if (ca.Z >= -near || cb.Z >= -near || cc.Z >= -near) {
			return;
		}

		Vector4 pa = Vector4.Transform(ca, proj);
		Vector4 pb = Vector4.Transform(cb, proj);
		Vector4 pc = Vector4.Transform(cc, proj);
		if (pa.W <= 1e-6f || pb.W <= 1e-6f || pc.W <= 1e-6f) {
			return;
		}

		float invWa = 1f / pa.W, invWb = 1f / pb.W, invWc = 1f / pc.W;

		float sxA = (pa.X * invWa * 0.5f + 0.5f) * width, syA = (1f - (pa.Y * invWa * 0.5f + 0.5f)) * height;
		float sxB = (pb.X * invWb * 0.5f + 0.5f) * width, syB = (1f - (pb.Y * invWb * 0.5f + 0.5f)) * height;
		float sxC = (pc.X * invWc * 0.5f + 0.5f) * width, syC = (1f - (pc.Y * invWc * 0.5f + 0.5f)) * height;

		int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(sxA, MathF.Min(sxB, sxC))));
		int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(sxA, MathF.Max(sxB, sxC))));
		int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(syA, MathF.Min(syB, syC))));
		int maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(syA, MathF.Max(syB, syC))));
		if (minX > maxX || minY > maxY) {
			return;
		}

		float area = EdgeFunction(sxA, syA, sxB, syB, sxC, syC);
		if (MathF.Abs(area) < 1e-6f) {
			return;
		}
		float invArea = 1f / area;

		// Placeholder light for a side the builder could not resolve (no palette loaded).
		float intensity = face.Lit ? 1f : 0.35f + 0.65f * MathF.Abs(Vector3.Dot(tri.FaceNormal, lightDir));

		bool textured = face.Texture is { } tex;
		DtsTexture texture = default;
		// Homogeneous UV weights (one projective map across a quad), folded into the 1/w terms.
		float qa = tri.UvWeights.X * invWa, qb = tri.UvWeights.Y * invWb, qc = tri.UvWeights.Z * invWc;
		float uAw = 0, uBw = 0, uCw = 0, vAw = 0, vBw = 0, vCw = 0;
		DtsContour contour = default;
		bool contoured = !textured && face.Contour.HasValue;
		int argb = 0;
		if (textured) {
			texture = face.Texture!.Value;
			uAw = tri.UvA.X * qa; uBw = tri.UvB.X * qb; uCw = tri.UvC.X * qc;
			vAw = tri.UvA.Y * qa; vBw = tri.UvB.Y * qb; vCw = tri.UvC.Y * qc;
		} else if (contoured) {
			contour = face.Contour!.Value;
		} else {
			argb = Scale(face.Color, intensity).ToArgb();
		}

		for (int y = minY; y <= maxY; y++) {
			float py = y + 0.5f;
			int rowOffset = y * width;

			for (int x = minX; x <= maxX; x++) {
				float px = x + 0.5f;

				float w0 = EdgeFunction(sxB, syB, sxC, syC, px, py) * invArea;
				float w1 = EdgeFunction(sxC, syC, sxA, syA, px, py) * invArea;
				float w2 = EdgeFunction(sxA, syA, sxB, syB, px, py) * invArea;

				// Winding is unknown (see class doc comment), so accept either all-positive or
				// all-negative barycentric weights as "inside".
				bool inside = (w0 >= 0f && w1 >= 0f && w2 >= 0f) || (w0 <= 0f && w1 <= 0f && w2 <= 0f);
				if (!inside) {
					continue;
				}

				float pixelInvW = w0 * invWa + w1 * invWb + w2 * invWc;
				int idx = rowOffset + x;
				if (pixelInvW <= depthBuffer[idx]) {
					continue;
				}

				if (textured) {
					float q = w0 * qa + w1 * qb + w2 * qc;
					float u = (w0 * uAw + w1 * uBw + w2 * uCw) / q;
					float v = (w0 * vAw + w1 * vBw + w2 * vCw) / q;
					int tx = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
					int ty = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
					int texel = texture.Pixels[ty * texture.Width + tx];
					// A cutout texel (palette index 0 in a transparent bank) draws nothing at all.
					if ((texel >>> 24) == 0) {
						continue;
					}
					pixels[idx] = face.Lit ? texel : Scale(Color.FromArgb(texel), intensity).ToArgb();
				} else if (contoured) {
					// A TSGouraudPoly's contour fill (docs/retail/rendering/dts-texture-binding.md,
					// "TSGouraudPoly — same ramp number, per-vertex light, no .RMP row"): the corners'
					// palette indices interpolated linearly in screen space — these weights are
					// screen-space, with no 1/w correction — and each pixel drawn in the ceiling of the
					// result, so every band carries its upper index. The engine's Scene.glsl does the
					// same with the same slack; both place a band's edge within a pixel of the
					// original's.
					float index = w0 * contour.IndexA + w1 * contour.IndexB + w2 * contour.IndexC;
					pixels[idx] = contour.Palette[Math.Clamp((int)MathF.Ceiling(index - BandSlack), 0, 255)];
				} else {
					pixels[idx] = argb;
				}
				depthBuffer[idx] = pixelInvW;
			}
		}
	}

	/// <summary>
	/// A solid poly's outline edge, or a line poly, in the side's line colour — drawn after every fill
	/// against the depth buffer with a little slack, so an outline shows on its own face but not
	/// through a face in front of it. The original's second pass is the same edge loop over the
	/// filled polygon.
	/// </summary>
	private static void RasterizeLine(DtsLine line, Vector3 eye, Matrix4x4 view, Matrix4x4 proj, float near,
			int[] pixels, float[] depthBuffer, int width, int height) {
		bool facingEye = Vector3.Dot(line.FaceNormal, eye - line.A) >= 0f;
		if ((facingEye ? line.Front : line.Back) is not { } color) {
			return;
		}

		Vector3 ca = Vector3.Transform(line.A, view);
		Vector3 cb = Vector3.Transform(line.B, view);
		if (ca.Z >= -near || cb.Z >= -near) {
			return;
		}

		Vector4 pa = Vector4.Transform(ca, proj);
		Vector4 pb = Vector4.Transform(cb, proj);
		if (pa.W <= 1e-6f || pb.W <= 1e-6f) {
			return;
		}

		float invWa = 1f / pa.W, invWb = 1f / pb.W;
		float xa = (pa.X * invWa * 0.5f + 0.5f) * width, ya = (1f - (pa.Y * invWa * 0.5f + 0.5f)) * height;
		float xb = (pb.X * invWb * 0.5f + 0.5f) * width, yb = (1f - (pb.Y * invWb * 0.5f + 0.5f)) * height;

		int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(xb - xa), MathF.Abs(yb - ya)));
		if (steps > 4 * (width + height)) {
			return;
		}

		int argb = color.ToArgb();
		for (int s = 0; s <= steps; s++) {
			float t = steps == 0 ? 0f : (float)s / steps;
			int x = (int)(xa + (xb - xa) * t);
			int y = (int)(ya + (yb - ya) * t);
			if (x < 0 || x >= width || y < 0 || y >= height) {
				continue;
			}

			float invW = invWa + (invWb - invWa) * t;
			int idx = y * width + x;
			if (invW >= depthBuffer[idx] * 0.995f) {
				pixels[idx] = argb;
			}
		}
	}

	/// <summary>
	/// How far below a whole palette index an interpolated one may land and still take that index —
	/// keeps a contour face whose corners all carry one index from rounding up to the next where the
	/// interpolation lands a hair above it.
	/// </summary>
	private const float BandSlack = 1f / 1024f;

	private static float EdgeFunction(float ax, float ay, float bx, float by, float cx, float cy) =>
		(cx - ax) * (by - ay) - (cy - ay) * (bx - ax);

	/// <summary>
	/// Draws triangle edges via GDI+ on top of the rasterized fill. When a depth buffer is
	/// available (Shaded+Wireframe mode), skips edges for triangles whose centroid is clearly
	/// behind whatever's already in the depth buffer there, so hidden geometry's edges don't bleed
	/// through the visible surface. Pure Wireframe mode has no depth buffer (fill is skipped
	/// entirely for it) and intentionally draws every edge — an "X-ray" view of the whole mesh.
	/// </summary>
	private void DrawWireframeOverlay(Graphics g, Vector3 eye, Matrix4x4 view, Matrix4x4 proj, float near,
			int width, int height, float[]? depthBuffer) {
		using var edgePen = new Pen(Color.FromArgb(160, 0, 0, 0), 1f);
		using var wireframePen = new Pen(Color.FromArgb(220, 200, 220, 255), 1f);
		Pen pen = depthBuffer != null ? edgePen : wireframePen;

		foreach (var root in _roots) {
			if (!root.Visible) {
				continue;
			}

			foreach (var tri in root.Mesh.Triangles) {
				// A one-sided copy shows its edges only from the side it draws, so a poly drawn as
				// front and back copies shows one fan, not both.
				if (tri.SideCopy != 0
						&& (Vector3.Dot(tri.FaceNormal, eye - tri.FacingPoint) >= 0f) != (tri.SideCopy > 0)) {
					continue;
				}

				Vector3 ca = Vector3.Transform(tri.A, view);
				Vector3 cb = Vector3.Transform(tri.B, view);
				Vector3 cc = Vector3.Transform(tri.C, view);

				if (ca.Z >= -near || cb.Z >= -near || cc.Z >= -near) {
					continue;
				}

				if (!TryProject(ca, proj, out var pa) ||
					!TryProject(cb, proj, out var pb) ||
					!TryProject(cc, proj, out var pc)) {
					continue;
				}

				if (depthBuffer != null) {
					float centroidInvW = (1f / Vector4.Transform(ca, proj).W +
						1f / Vector4.Transform(cb, proj).W + 1f / Vector4.Transform(cc, proj).W) / 3f;
					int cx = Math.Clamp((int)((pa.X + pb.X + pc.X) / 3f), 0, width - 1);
					int cy = Math.Clamp((int)((pa.Y + pb.Y + pc.Y) / 3f), 0, height - 1);
					if (centroidInvW < depthBuffer[cy * width + cx] * 0.98f) {
						continue;
					}
				}

				g.DrawPolygon(pen, new[] { pa, pb, pc });
			}
		}
	}

	private void DrawGroundGrid(Graphics g, Matrix4x4 view, Matrix4x4 proj, float near) {
		float half = _boundingRadius * 1.5f;
		float y = _baseCenter.Y - _boundingRadius;
		const int divisions = 10;

		using var gridPen = new Pen(Color.FromArgb(50, 255, 255, 255), 1f);

		for (int i = 0; i <= divisions; i++) {
			float t = -half + 2 * half * i / divisions;

			DrawGridLine(g, view, proj, near, gridPen, new Vector3(t, y, -half), new Vector3(t, y, half));
			DrawGridLine(g, view, proj, near, gridPen, new Vector3(-half, y, t), new Vector3(half, y, t));
		}
	}

	private void DrawGridLine(Graphics g, Matrix4x4 view, Matrix4x4 proj, float near, Pen pen, Vector3 worldA, Vector3 worldB) {
		Vector3 ca = Vector3.Transform(worldA, view);
		Vector3 cb = Vector3.Transform(worldB, view);
		if (ca.Z >= -near || cb.Z >= -near) {
			return;
		}

		if (TryProject(ca, proj, out var pa) && TryProject(cb, proj, out var pb)) {
			g.DrawLine(pen, pa, pb);
		}
	}

	private bool TryProject(Vector3 camSpace, Matrix4x4 proj, out PointF screenPoint) {
		Vector4 clip = Vector4.Transform(camSpace, proj);
		if (clip.W <= 1e-6f) {
			screenPoint = default;
			return false;
		}

		float invW = 1f / clip.W;
		float ndcX = clip.X * invW;
		float ndcY = clip.Y * invW;

		screenPoint = new PointF(
			(ndcX * 0.5f + 0.5f) * Width,
			(1f - (ndcY * 0.5f + 0.5f)) * Height);
		return true;
	}

	private static Color Scale(Color c, float intensity) {
		intensity = Math.Clamp(intensity, 0f, 1.15f);
		return Color.FromArgb(255,
			(int)Math.Clamp(c.R * intensity, 0, 255),
			(int)Math.Clamp(c.G * intensity, 0, 255),
			(int)Math.Clamp(c.B * intensity, 0, 255));
	}
}
