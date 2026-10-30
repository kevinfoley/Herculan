using System.Numerics;
using Herculan.Engine.Sim;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>How a pass of the world decides what covers what — see <see cref="WorldDrawPass"/>.</summary>
public enum WorldDrawMode {
	/// <summary>The original's painter's order: a terrain cell at a time, each object painted whole over what came before.</summary>
	PaintersOrder,

	/// <summary>A depth test at every pixel, between every object and the terrain and between objects.</summary>
	PerPixelDepth,
}

/// <summary>
/// One pass of the world into a viewport, terrain, objects, billboards and beams, in either <see cref="WorldDrawMode"/>.
///
/// <para><b>Per-pixel depth</b> is <see cref="SceneRenderer.Render(Camera, IEnumerable{SceneItem}, GroundShapeLayer?, int, int, int, int)"/>,
/// then <see cref="BeamRenderer.Render"/>, then <see cref="SpriteRenderer.Render"/>, each depth-tested against what is
/// already drawn.</para>
///
/// <para><b>Painter's order</b> is the original's, which has no depth buffer: it paints each terrain cell's ground in the
/// walk's order and that cell's objects straight after it, so an object is painted whole over everything before it and
/// under everything after (docs/retail/rendering/terrain-drawing.md, "The cell walk", "Objects in the walk", "The draw
/// order within a cell" and "After the walk"). This draw builds that order from <see cref="ObjectDrawTable"/>'s filing and
/// reproduces it on a depth-buffered GPU:</para>
/// <list type="bullet">
/// <item><description>The terrain is drawn first, depth-tested against itself, with its paint ranks
/// (<see cref="TerrainPaintRankBuffer"/>) drawn beside it. The depth buffer is then cleared, under the caller's scissor or
/// else the viewport, and from there on the ground covers an object only through the rank test: a fragment is kept where
/// the ground showing ranks no later than the cell its entry is filed under. What is drawn after the walk ranks after all
/// of it, and the sky ranks below every cell.</description></item>
/// <item><description>Every item, billboard batch and beam span sharing one <see cref="DrawEntry"/> is one entry, and the
/// entries are drawn one at a time in the original's order: the walk's cells by <see cref="TerrainPaintOrder.Rank"/>, each
/// with its ground shapes first in submit order and then its other entries farthest first by
/// <see cref="DrawEntry.DistanceFrom"/>, where of two at the same distance the one filed later goes first
/// (<c>ObjList_DrawSorted</c>'s tree goes right, drawn later, only for a strictly nearer entry); then the no-cell bucket
/// in submit order; then the flying player's cell, when the walk did not reach it, ordered as a cell
/// is.</description></item>
/// <item><description>Each entry gets its own slice of the depth range (<c>glDepthRange</c>), a later entry a nearer
/// slice, so it covers every entry before it outright while its own polys still depth-test among themselves, and a BSP
/// part's stencil order runs inside it unchanged. Its projection is the camera's with the near and far planes fitted to
/// the view depths the entry spans (its meshes' bounds, its billboards' anchors, its beams' ends), so the slice spends its
/// precision on the entry; x, y and w are the camera's, so nothing moves on screen. A slice is as wide as its entry's
/// far-to-near ratio asks, in proportion to the rest, which gives every entry the same depth resolution relative to its
/// distance. Depth clamping keeps geometry past a fitted plane rather than clipping it, and the camera's own near and far
/// planes stay on as clip distances.</description></item>
/// <item><description>A ground shape and a structure's ground plane are painted with depth testing off, as the per-pixel
/// draw paints them: a shape as an entry of its own, a ground plane at the start of its structure's turn.</description></item>
/// <item><description>Items and billboards with no <see cref="SceneItem.Filing"/>, the terrain aside, are one entry at the
/// end of the no-cell bucket, which is where <see cref="ObjectDrawTable"/> puts an object it has never filed. Nothing
/// the simulator draws is unfiled.</description></item>
/// </list>
/// </summary>
public sealed class WorldDrawPass {
	/// <summary>
	/// Depth-buffer steps (of 2^24) left between two slices, so a fragment clamped to one slice's far plane never ties
	/// with one clamped to the near plane of the slice before it.
	/// </summary>
	private const float SliceGap = 4f / (1 << 24);

	/// <summary>The narrowest slice, in the same steps.</summary>
	private const float MinimumSlice = 64f / (1 << 24);

	private readonly GL _gl;
	private readonly SceneRenderer _scene;
	private readonly SpriteRenderer _sprites;
	private readonly BeamRenderer? _beams;

	// Kept between passes so a pass allocates nothing once the pool has grown to the scene.
	private readonly List<PaintEntry> _pool = new();
	private int _rented;
	private readonly Dictionary<DrawEntry, PaintEntry> _byFiling = new(ReferenceEqualityComparer.Instance);
	private PaintEntry? _unfiled;
	private readonly List<PaintEntry> _order = new();

	public WorldDrawPass(GL gl, SceneRenderer scene, SpriteRenderer sprites, BeamRenderer? beams) {
		_gl = gl;
		_scene = scene;
		_sprites = sprites;
		_beams = beams;
	}

	/// <summary>
	/// Draws the world from <paramref name="camera"/> into the viewport (<paramref name="viewportX"/>,
	/// <paramref name="viewportY"/>, <paramref name="viewportWidth"/>, <paramref name="viewportHeight"/>), under whatever
	/// scissor the caller has set. <paramref name="tracers"/> are what the per-pixel depth draw draws the beams from and
	/// <paramref name="beamSpans"/> what the painter's order does; both are the same shots.
	/// </summary>
	public void Draw(WorldDrawMode mode, Camera camera, IEnumerable<SceneItem> items, GroundShapeLayer ground,
			IReadOnlyList<SpriteBatch> sprites, IReadOnlyList<BeamTracer> tracers, IReadOnlyList<BeamSpanDraw> beamSpans,
			int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		if (mode == WorldDrawMode.PerPixelDepth) {
			_scene.Render(camera, items, ground, viewportX, viewportY, viewportWidth, viewportHeight);
			_beams?.Render(camera, tracers, viewportWidth, viewportHeight, ground.Objects);
			_sprites.Render(camera, sprites, viewportWidth, viewportHeight);
			return;
		}

		Release();
		var order = _scene.BeginPaintedPass(camera, ground, viewportX, viewportY, viewportWidth, viewportHeight);
		var projection = _scene.PassProjection;
		_scene.DrawTerrain(ground.Terrain);
		ClearDepth(viewportX, viewportY, viewportWidth, viewportHeight);

		Collect(camera, items, ground, sprites, beamSpans, order);
		_order.Sort(Compare);
		Fit(camera, projection);

		_gl.Enable(EnableCap.DepthClamp);
		_gl.Enable(EnableCap.ClipDistance0);
		_gl.Enable(EnableCap.ClipDistance1);
		_scene.BindPaintRanks();

		foreach (var entry in _order) {
			DrawEntry(entry, camera, projection, viewportWidth, viewportHeight);
		}

		_gl.DepthRange(0.0, 1.0);
		_gl.Disable(EnableCap.ClipDistance1);
		_gl.Disable(EnableCap.ClipDistance0);
		_gl.Disable(EnableCap.DepthClamp);
		_scene.EndPaintedPass();
		Release();
	}

	// Clears the depth the terrain left, inside the caller's scissor, or the viewport's when none is set: another
	// panel's pixels keep theirs.
	private void ClearDepth(int x, int y, int width, int height) {
		bool scissored = _gl.IsEnabled(EnableCap.ScissorTest);
		if (!scissored) {
			_gl.Enable(EnableCap.ScissorTest);
			_gl.Scissor(x, y, (uint)Math.Max(width, 1), (uint)Math.Max(height, 1));
		}

		_gl.DepthMask(true);
		_gl.Clear(ClearBufferMask.DepthBufferBit);

		if (!scissored) {
			_gl.Disable(EnableCap.ScissorTest);
		}
	}

	// Gathers the pass's entries into _order, each placed in the paint: the ground shapes by the cell
	// HeightGrid.PickDrawCell picks, as the per-pixel draw files them, everything else by its DrawEntry.
	private void Collect(Camera camera, IEnumerable<SceneItem> items, GroundShapeLayer ground,
			IReadOnlyList<SpriteBatch> sprites, IReadOnlyList<BeamSpanDraw> beamSpans, TerrainPaintOrder order) {
		var table = ground.Objects;
		var viewer = camera.Position;

		for (int index = 0; index < ground.Shapes.Count; index++) {
			var shape = ground.Shapes[index];
			var cell = ground.Grid.PickDrawCell(shape.Position, shape.Radius, viewer, ground.Region);
			if ((ground.ShapesFollowWalk && cell is { } filed && !table.CellDrawn(filed))
					|| !SceneRenderer.IsDrawn(shape.Item)) {
				continue;
			}

			var entry = Rent();
			entry.Shape = shape.Item;
			entry.Kind = 0;
			entry.Sequence = index;
			Place(entry, table, order, cell);
		}

		foreach (var item in items) {
			if (!ReferenceEquals(item, ground.Terrain) && SceneRenderer.IsDrawn(item)) {
				EntryFor(item.Filing).Items.Add(item);
			}
		}

		foreach (var item in ground.ObjectGround) {
			if (item.Filing is { } filing && SceneRenderer.IsDrawn(item)) {
				EntryFor(filing).Ground.Add(item);
			}
		}

		foreach (var batch in sprites) {
			if (batch.Filing is not { Drawn: false }) {
				EntryFor(batch.Filing).Sprites.Add(batch);
			}
		}

		if (_beams != null) {
			foreach (var span in beamSpans) {
				if (span.Filing.Drawn) {
					EntryFor(span.Filing).Beams.Add(span);
				}
			}
		}

		PaintEntry EntryFor(DrawEntry? filing) {
			if (filing == null) {
				if (_unfiled == null) {
					_unfiled = Rent();
					_unfiled.Kind = 1;
					_unfiled.Sequence = int.MaxValue;
					Place(_unfiled, table, order, null);
				}

				return _unfiled;
			}

			if (!_byFiling.TryGetValue(filing, out var entry)) {
				entry = Rent();
				entry.Kind = 1;
				entry.Sequence = filing.FilingIndex;
				entry.Distance = filing.DistanceFrom(viewer);
				Place(entry, table, order, filing.Cell);
				_byFiling[filing] = entry;
			}

			return entry;
		}
	}

	// Where `entry`, filed under `cell`, falls in this pass's paint, and the rank its fragments test the ground against.
	// An entry this pass did not file keeps the answer and the cell it was last filed with; one still drawn whose cell
	// this pass's draw does not reach goes in the walk at that cell's rank.
	private static void Place(PaintEntry entry, ObjectDrawTable table, TerrainPaintOrder order, (int X, int Y)? cell) {
		var phase = table.PhaseOf(cell);
		entry.Phase = phase == PaintPhase.NotDrawn ? PaintPhase.Walk : phase;
		entry.Rank = entry.Phase == PaintPhase.Walk && cell is { } filed
			? order.Rank(filed.X, filed.Y)
			: TerrainPaintOrder.AfterTerrain;
	}

	// The paint order: the walk's cells in rank order, then the no-cell bucket, then the flying player's cell. Within a
	// cell the ground shapes in submit order, then the rest farthest first, the later-filed first at equal distance;
	// within the no-cell bucket everything in submit order, the shapes (submitted first) first.
	private static int Compare(PaintEntry a, PaintEntry b) {
		int order = a.Phase.CompareTo(b.Phase);
		if (order != 0) {
			return order;
		}

		if (a.Phase != PaintPhase.NoCell) {
			order = a.Rank.CompareTo(b.Rank);
			if (order != 0) {
				return order;
			}
		}

		order = a.Kind.CompareTo(b.Kind);
		if (order != 0) {
			return order;
		}

		if (a.Kind == 0 || a.Phase == PaintPhase.NoCell) {
			return a.Sequence.CompareTo(b.Sequence);
		}

		order = b.Distance.CompareTo(a.Distance);
		return order != 0 ? order : b.Sequence.CompareTo(a.Sequence);
	}

	// Measures the view depths each entry spans and hands out the depth slices, the first-painted the farthest.
	private void Fit(Camera camera, in Matrix4x4 projection) {
		var view = camera.ViewMatrix;

		// Where the camera projection's near clip plane crosses the view axis: clip z + w = 0 with w = -z.
		float clipNear = projection.M43 / (projection.M33 - 1f);

		int slices = 0;
		float totalWeight = 0f;
		foreach (var entry in _order) {
			float nearest = float.PositiveInfinity;
			float farthest = float.NegativeInfinity;
			foreach (var item in entry.Items) {
				ExtendDepth(item, view, ref nearest, ref farthest);
			}

			foreach (var batch in entry.Sprites) {
				SpriteRenderer.ExtendDepth(batch, view, camera.NearPlane, ref nearest, ref farthest);
			}

			if (_beams != null) {
				foreach (var span in entry.Beams) {
					_beams.ExtendDepth(span, camera, ref nearest, ref farthest);
				}
			}

			// Nothing of it in front of the near plane, or nothing to depth-test at all: it takes no slice.
			entry.Depthed = farthest >= clipNear;
			if (!entry.Depthed) {
				continue;
			}

			entry.Near = MathF.Max(nearest, clipNear);
			entry.Far = MathF.Max(farthest, entry.Near + MathF.Max(entry.Near * 1e-3f, 1e-3f));
			totalWeight += entry.Far / entry.Near;
			slices++;
		}

		// The first slice's far edge sits a gap short of the cleared 1.0, which a fragment on it would tie.
		float available = 1f - SliceGap;
		float spare = available - slices * MinimumSlice;
		float top = available;
		foreach (var entry in _order) {
			if (!entry.Depthed) {
				continue;
			}

			float width = spare > 0f
				? MinimumSlice + spare * (entry.Far / entry.Near) / totalWeight
				: available / slices;
			entry.DepthFar = top;
			entry.DepthNear = top - width + SliceGap;
			top -= width;
		}
	}

	// Widens [nearest, farthest] to the view depths `item`'s mesh can reach: its bounding box through the model-view
	// matrix, whose third column gives a point's view z.
	private static void ExtendDepth(SceneItem item, in Matrix4x4 view, ref float nearest, ref float farthest) {
		var mesh = item.Mesh;
		if (mesh.VertexCount == 0) {
			return;
		}

		var modelView = item.Transform * view;
		var centre = (mesh.BoundsMin + mesh.BoundsMax) * 0.5f;
		var half = (mesh.BoundsMax - mesh.BoundsMin) * 0.5f;
		float z = centre.X * modelView.M13 + centre.Y * modelView.M23 + centre.Z * modelView.M33 + modelView.M43;
		float reach = MathF.Abs(half.X * modelView.M13) + MathF.Abs(half.Y * modelView.M23)
			+ MathF.Abs(half.Z * modelView.M33);
		nearest = MathF.Min(nearest, -z - reach);
		farthest = MathF.Max(farthest, -z + reach);
	}

	// The camera's projection with its depth mapped from `near` to `far` alone, the GL way (-1 at near, +1 at far). Only
	// the column that makes clip z changes, so x, y and w, and so where everything lands on screen, are the camera's.
	private static Matrix4x4 Fitted(Matrix4x4 projection, float near, float far) {
		projection.M13 = 0f;
		projection.M23 = 0f;
		projection.M33 = -(far + near) / (far - near);
		projection.M43 = -2f * far * near / (far - near);
		return projection;
	}

	private void DrawEntry(PaintEntry entry, Camera camera, in Matrix4x4 projection, int viewportWidth, int viewportHeight) {
		var entryProjection = entry.Depthed ? Fitted(projection, entry.Near, entry.Far) : projection;
		if (entry.Depthed) {
			_gl.DepthRange(entry.DepthNear, entry.DepthFar);
		}

		_scene.BeginEntry(entryProjection, entry.Rank);
		if (entry.Shape != null) {
			_scene.DrawUndepthed(entry.Shape);
		}

		foreach (var item in entry.Ground) {
			_scene.DrawUndepthed(item);
		}

		if (!entry.Depthed) {
			return;
		}

		if (entry.Items.Count > 0) {
			_scene.DrawEntryItems(entry.Items);
		}

		foreach (var batch in entry.Sprites) {
			_sprites.DrawEntry(batch, camera, projection, entryProjection, entry.Rank);
		}

		if (_beams != null) {
			foreach (var span in entry.Beams) {
				_beams.DrawSpan(span, camera, projection, entryProjection, entry.Rank, viewportWidth, viewportHeight);
			}
		}
	}

	private PaintEntry Rent() {
		if (_rented == _pool.Count) {
			_pool.Add(new PaintEntry());
		}

		var entry = _pool[_rented++];
		_order.Add(entry);
		return entry;
	}

	// Lets go of the last pass's items, so nothing is held between frames.
	private void Release() {
		for (int i = 0; i < _rented; i++) {
			_pool[i].Clear();
		}

		_rented = 0;
		_byFiling.Clear();
		_unfiled = null;
		_order.Clear();
	}

	/// <summary>One entry of the paint and where it falls.</summary>
	private sealed class PaintEntry {
		/// <summary>A ground shape, which is an entry on its own; null for everything else.</summary>
		public SceneItem? Shape;

		/// <summary>The entry's structure ground-plane pieces, painted first.</summary>
		public readonly List<SceneItem> Ground = new();

		public readonly List<SceneItem> Items = new();
		public readonly List<SpriteBatch> Sprites = new();
		public readonly List<BeamSpanDraw> Beams = new();

		public PaintPhase Phase;

		/// <summary>The rank its fragments test the ground against, and within the walk where its cell falls.</summary>
		public uint Rank;

		/// <summary>0 for a ground shape, 1 for everything else: within a cell the shapes come first.</summary>
		public int Kind;

		/// <summary><see cref="DrawEntry.DistanceFrom"/> the viewer.</summary>
		public int Distance;

		/// <summary>Its submit order: the shape's index, or <see cref="DrawEntry.FilingIndex"/>.</summary>
		public int Sequence;

		/// <summary>Whether it draws anything depth-tested in front of the near plane, and so takes a slice.</summary>
		public bool Depthed;

		/// <summary>The view depths its projection is fitted to.</summary>
		public float Near, Far;

		/// <summary>Its slice of the depth range.</summary>
		public double DepthNear, DepthFar;

		public void Clear() {
			Shape = null;
			Ground.Clear();
			Items.Clear();
			Sprites.Clear();
			Beams.Clear();
			Depthed = false;
			Distance = 0;
		}
	}
}
