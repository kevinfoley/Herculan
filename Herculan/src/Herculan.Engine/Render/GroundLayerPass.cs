using System.Numerics;
using Herculan.Engine.Settings;
using Herculan.Engine.Terrain;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Render;

/// <summary>
/// The ground of a pass of the world through a <see cref="SceneRenderer"/>: this pass's side of the original's submit,
/// which files a <see cref="GroundShapeLayer"/>'s objects for the view, the terrain's paint ranks
/// (<see cref="TerrainPaintRankBuffer"/>), and the per-pixel depth draw of the terrain, its ground shapes and the
/// structures' ground planes. <see cref="WorldDrawPass"/> draws both of its modes through it, and the mission editor its
/// per-pixel one.
/// </summary>
public sealed class GroundLayerPass : IDisposable {
	private readonly GL _gl;
	private TerrainPaintRankBuffer? _paintRanks;

	public GroundLayerPass(GL gl, SceneRenderer scene) {
		_gl = gl;
		Scene = scene;
	}

	/// <summary>The renderer the pass draws through.</summary>
	public SceneRenderer Scene { get; }

	/// <summary>
	/// One pass with the ground drawn as the original paints it and every object depth-tested per pixel:
	/// <paramref name="ground"/>'s terrain first, then its ground shapes in the terrain's paint order, then everything else
	/// in <paramref name="items"/> (which may hold the terrain item too; it is not drawn twice). This is the mission
	/// editor's draw, and the simulator's when its "Per-pixel depth sorting" tweak is on; the simulator's default, the
	/// original's painter's order, is <see cref="WorldDrawPass"/>.
	///
	/// <para>The original paints each ground shape straight after its own cell's ground, so the
	/// ground of every cell its walk paints later covers it and it covers everything painted
	/// before (docs/retail/simulation/ground-shapes.md#the-draw-pass). Here each shape is filed under the
	/// cell <see cref="HeightGrid.PickDrawCell"/> picks, as the original's submit files it, takes that
	/// cell's <see cref="TerrainPaintOrder.Rank"/>, and is drawn with depth testing off, keeping only
	/// the pixels where the ground showing ranks no later — <see cref="TerrainPaintRankBuffer"/>. Where
	/// no ground shows, the sky, it draws. The shapes are drawn in rank order, so where two overlap
	/// the later cell's is on top, and within one cell in the order given, the original's submit
	/// order. A shape off the grid ranks after all of it, as the original's no-cell bucket
	/// draws.</para>
	///
	/// <para>A structure's ground plane — its shadow and floor plates, <see cref="MeshCell.Ground"/> —
	/// is drawn in the same pass, ranked by the cell its object was filed under, after that cell's
	/// shapes. The original paints the whole structure over its own cell's ground
	/// (docs/retail/rendering/terrain-drawing.md#objects-in-the-walk), and these polys lie in the
	/// terrain's own plane on a level pad, where a depth test could only tie with the ground.</para>
	///
	/// <para>Everything else is drawn afterwards with the depth test, so a machine stands over its
	/// own shadow and a structure over its floor. Two things the original's painter's order does are
	/// not reproduced by this draw: a shape filed under a later cell is painted over an object filed under an earlier one
	/// where the two overlap on screen, where here every object is drawn over every shape; and
	/// one object is painted whole over another (docs/retail/rendering/terrain-drawing.md, "The draw order within a cell"),
	/// where here a surface of one that cuts through the other, such as an impact effect's billboard through the building
	/// it struck, shows only its nearer part.</para>
	///
	/// <para>Before any of it, the pass rebuilds the zone's visible region for its own view, as
	/// <c>Terrain_SetupVisibleRegion</c> does before the submit, and files <paramref name="ground"/>'s
	/// <see cref="GroundShapeLayer.Objects"/> by it; an item whose <see cref="SceneItem.Filing"/> that
	/// leaves undrawn, and a ground shape filed under a cell the draw does not reach, are skipped. The
	/// billboards and beams drawn after this call for the same pass read the same answers.</para>
	/// </summary>
	public void Draw(Camera camera, IEnumerable<SceneItem> items, GroundShapeLayer ground,
			int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		float aspect = (float)viewportWidth / System.Math.Max(viewportHeight, 1);
		var projection = camera.ProjectionMatrix(aspect);
		var order = FileObjects(camera, aspect, ground);

		// Before anything is drawn into the frame, because it draws into a target of its own.
		var groundShapes = ground is { Shapes.Count: > 0 } or { ObjectGround.Count: > 0 }
			? RankGroundShapes(camera, projection, ground, order,
				viewportX, viewportY, viewportWidth, viewportHeight)
			: null;

		Scene.BeginPass(camera, projection, viewportX, viewportY, viewportWidth, viewportHeight);
		Scene.DrawItem(ground.Terrain);

		if (groundShapes != null) {
			BindPaintRanks();
			foreach (var (item, rank) in groundShapes) {
				Scene.BeginRankTest(projection, rank);
				Scene.DrawUndepthed(item);
			}

			Scene.EndRankTest();
		}

		Scene.DrawItems(items, ground.Terrain);
	}

	/// <summary>
	/// The start of a <see cref="WorldDrawPass"/> pass in the original's painter's order: files <paramref name="ground"/>'s
	/// objects for this view as <see cref="Draw"/> does, draws the terrain's paint ranks (<see cref="TerrainPaintRankBuffer"/>)
	/// for every entry to test against, and starts the renderer's pass (<see cref="SceneRenderer.BeginPass"/>). Returns
	/// the walk's order.
	/// </summary>
	public TerrainPaintOrder BeginPainted(Camera camera, GroundShapeLayer ground,
			int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		float aspect = (float)viewportWidth / System.Math.Max(viewportHeight, 1);
		var projection = camera.ProjectionMatrix(aspect);
		var order = FileObjects(camera, aspect, ground);

		// Before anything is drawn into the frame, because it draws into a target of its own.
		_paintRanks ??= new TerrainPaintRankBuffer(_gl);
		_paintRanks.Draw(ground.Terrain, ground.Grid, order, camera.ViewMatrix, projection,
			viewportX, viewportY, viewportWidth, viewportHeight);

		Scene.BeginPass(camera, projection, viewportX, viewportY, viewportWidth, viewportHeight);
		return order;
	}

	/// <summary>Binds the paint ranks this pass drew, where every program's rank test samples them (<see cref="SceneRenderer.BindPaintRanks"/>).</summary>
	public void BindPaintRanks() => Scene.BindPaintRanks(_paintRanks!.RankTexture);

	/// <summary>
	/// This pass's side of the original's submit: rebuilds the zone's visible region for the pass's
	/// view, as <c>Terrain_SetupVisibleRegion</c> does before the submit, and files the layer's objects
	/// by it. Returns the walk's order, which the ground shapes are ranked by. Under the widescreen tweak the filing
	/// also draws a structure the walk misses while it is on screen (<see cref="ObjectDrawTable.File"/>).
	/// </summary>
	private static TerrainPaintOrder FileObjects(Camera camera, float aspect, GroundShapeLayer ground) {
		var grid = ground.Grid;
		var viewer = camera.Position;
		var rotation = camera.ViewRotation;
		var edges = camera.EdgeSlopes(aspect);
		ground.Region.Update(grid, viewer, rotation, edges);
		var order = TerrainPaintOrder.For(grid, viewer, camera.SimHeading);
		ViewSphereTest? reachesView = TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.HighResolutionWidescreen)
			? new ViewSphereTest(viewer, rotation, edges)
			: null;
		ground.Objects.File(grid, ground.Region, viewer, order, reachesView);
		return order;
	}

	/// <summary>
	/// The ground shapes' side of it: files each shape under the cell
	/// <see cref="HeightGrid.PickDrawCell"/> picks, drops it when this pass's draw does not reach that
	/// cell (<see cref="ObjectDrawTable.CellDrawn"/>), takes the cell's rank in this view's walk, and
	/// draws the terrain's ranks for the shapes' fragments to test against. The structures' ground
	/// planes (<see cref="GroundShapeLayer.ObjectGround"/>) take the rank of the cell their object was
	/// filed under. Returns everything in the order to draw it.
	/// </summary>
	private List<(SceneItem Item, uint Rank)> RankGroundShapes(Camera camera, Matrix4x4 projection,
			GroundShapeLayer ground, TerrainPaintOrder order,
			int viewportX, int viewportY, int viewportWidth, int viewportHeight) {
		var grid = ground.Grid;
		var viewer = camera.Position;

		var ranked = new List<(SceneItem Item, uint Rank, int Distance)>(
			ground.Shapes.Count + ground.ObjectGround.Count);
		foreach (var shape in ground.Shapes) {
			var cell = grid.PickDrawCell(shape.Position, shape.Radius, viewer, ground.Region);
			if (ground.ShapesFollowWalk && cell is { } filed && !ground.Objects.CellDrawn(filed)) {
				continue;
			}

			ranked.Add((shape.Item, RankOf(cell), -1));
		}

		// A structure's ground plane, under the cell its object was filed under this pass. Within that
		// cell it comes after the cell's ground shapes, which ObjList_DrawCellObjects (00428c60) draws on
		// the spot before the rest, and with the cell's other structures farthest first, the order
		// ObjList_DrawSorted (00429620) paints them in. An entry this pass does not draw is skipped by the
		// draw itself.
		foreach (var item in ground.ObjectGround) {
			if (item.Filing is not DrawEntry entry) {
				continue;
			}

			ranked.Add((item, RankOf(entry.Cell), entry.Position.ApproxDistanceTo(viewer)));
		}

		// Stable, so shapes sharing a cell keep the order they were given in.
		var result = ranked.OrderBy(entry => entry.Rank).ThenByDescending(entry => entry.Distance < 0)
			.ThenByDescending(entry => entry.Distance)
			.Select(entry => (entry.Item, entry.Rank)).ToList();

		_paintRanks ??= new TerrainPaintRankBuffer(_gl);
		_paintRanks.Draw(ground.Terrain, grid, order, camera.ViewMatrix, projection,
			viewportX, viewportY, viewportWidth, viewportHeight);

		return result;

		// A cell's place in this view's walk; the no-cell bucket is drawn after all of it.
		uint RankOf((int X, int Y)? cell) =>
			cell is { } filed ? order.Rank(filed.X, filed.Y) : TerrainPaintOrder.AfterTerrain;
	}

	public void Dispose() => _paintRanks?.Dispose();
}
