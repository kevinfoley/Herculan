using Herculan.Engine.Numerics;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Render;

/// <summary>
/// One ground shape to draw this frame (<see cref="Sim.GroundShape"/>): its geometry, and the
/// position and radius <c>HeightGrid_PickDrawCell</c> files it by. The position is the one the
/// shape had <b>before</b> this frame's draw conformed it, which is the one the original's submit
/// reads: <c>Scene_SubmitFrameObjects</c> files every shape before any is drawn.
/// </summary>
public readonly record struct GroundShapeDraw(SceneItem Item, Vec3i Position, int Radius);

/// <summary>
/// The ground and what is painted with it: the terrain item, its grid, the ground shapes to draw in
/// the terrain's paint order, and the table every other object is filed in by terrain cell — see
/// <see cref="SceneRenderer.Render(Camera, IEnumerable{SceneItem}, GroundShapeLayer?, int, int, int, int)"/>.
/// One per loaded zone, holding the zone's <see cref="TerrainVisibleRegion"/> between frames as the
/// original's grid holds its own.
/// </summary>
public sealed class GroundShapeLayer {
	public GroundShapeLayer(SceneItem terrain, HeightGrid grid) {
		Terrain = terrain;
		Grid = grid;
	}

	/// <summary>The terrain mesh's item. The renderer draws it before the shapes and skips it among the other items.</summary>
	public SceneItem Terrain { get; }

	/// <summary>The zone's grid, which the walk and the shapes' cells are measured on.</summary>
	public HeightGrid Grid { get; }

	/// <summary>The zone's visible region, rebuilt per pass from that pass's view.</summary>
	public TerrainVisibleRegion Region { get; } = new();

	/// <summary>The shapes to draw this frame, in the order the original submits them: oldest first.</summary>
	public List<GroundShapeDraw> Shapes { get; } = new();

	/// <summary>
	/// Whether a shape filed under a cell this pass's walk does not reach is dropped, as the original
	/// drops it. On for the simulator. The mission editor turns it off: it files nothing in
	/// <see cref="Objects"/> and draws every object wherever the camera is, so a structure's ground
	/// plane, which it hands over as a shape, has to be drawn wherever its structure is.
	/// </summary>
	public bool ShapesFollowWalk { get; init; } = true;

	/// <summary>
	/// The structures' ground-plane pieces (<see cref="MeshCell.Ground"/>), drawn with the shapes rather
	/// than depth-tested against the terrain they lie in. Each is ranked by the cell its
	/// <see cref="SceneItem.Filing"/> entry was filed under this pass, which is where the original paints
	/// the whole structure: after that cell's ground and its ground shapes, before every later cell. Kept
	/// for the mission; whether each is drawn is its item's own answer.
	/// </summary>
	public List<SceneItem> ObjectGround { get; } = new();

	/// <summary>
	/// Every other object, filed by terrain cell each pass to decide whether it is drawn. An item or a
	/// billboard whose <see cref="DrawEntry"/> is not among <see cref="ObjectDrawTable.Entries"/> is drawn
	/// as it was last filed.
	/// </summary>
	public ObjectDrawTable Objects { get; } = new();
}
