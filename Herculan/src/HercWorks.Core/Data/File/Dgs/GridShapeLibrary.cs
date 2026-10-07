using HercWorks.Core.Data.File.Dts;

namespace HercWorks.Core.Data.File.Dgs;

/// <summary>
/// A structure shape's collision volume: a coarse height field over the shape's footprint, stored
/// as a grid of byte codes into a 256-entry table of heights. It is what makes a building solid —
/// DBSIM's ray-versus-structure query walks this grid rather than the geometry, so a shot passes
/// through a doorway and stops on a wall without ever touching a polygon.
///
/// <para>Read by <c>GridShape_ReadFromStream</c> (<c>0042762c</c>) as five <c>int16</c> scalars, a
/// fixed 1024-byte block and then <see cref="Rows"/> rows of <see cref="Columns"/> bytes; queried by
/// <c>GridShape_HeightAround</c> (<c>00427238</c>, the height under a point) and <c>GridShape_Raycast</c> (<c>004273c8</c>, the ray march).</para>
/// </summary>
/// <param name="Columns">Cells across, the grid's X extent (<c>+0x2a</c>).</param>
/// <param name="Rows">Cells down, the grid's Y extent (<c>+0x2c</c>).</param>
/// <param name="OriginColumn">
/// Which cell the shape's own origin sits in, X (<c>+0x2e</c>). A query point is shifted by this
/// <i>times the cell size</i> before it is divided down, so the grid is centred on the model rather
/// than starting at it.
/// </param>
/// <param name="OriginRow">The same, Y (<c>+0x30</c>).</param>
/// <param name="CellShift">
/// Log2 of the cell size in world units (<c>+0x32</c>) — every conversion between a shape-space
/// coordinate and a cell index is a shift by this, never a divide.
/// </param>
/// <param name="Heights">
/// The 256-entry height table (<c>+0x34</c>, 1024 bytes) each cell's byte indexes. Its last entry
/// is read on its own as the grid's ceiling — see <see cref="MaxHeight"/>.
/// </param>
/// <param name="Cells">
/// <see cref="Rows"/> rows of <see cref="Columns"/> height codes, row-major with Y outermost, as the
/// original allocates and reads them (<c>+0x434</c>, a row-pointer array).
/// </param>
public readonly record struct GridShapeCollision(
	short Columns, short Rows, short OriginColumn, short OriginRow, short CellShift,
	int[] Heights, byte[][] Cells) {

	/// <summary>
	/// The grid's tallest possible column, which is simply <see cref="Heights"/>'s last entry — the
	/// original addresses it directly as <c>grid+0x430</c> and uses it to reject a ray passing
	/// entirely overhead before marching a single step. That works because the table is authored
	/// ascending, so code 255 is the tallest a cell can name.
	/// </summary>
	public int MaxHeight => Heights.Length == 256 ? Heights[255] : 0;

	/// <summary>Whether this shape has a collision volume at all — the original's own <c>grid.Columns != 0</c> test.</summary>
	public bool IsSolid => Columns > 0 && Rows > 0 && Cells.Length > 0;
}

/// <summary>
/// One record of a <c>.DGS</c> shape library: <c>dat\BASES.DAT</c>'s <c>ShapeIndex</c> field
/// indexes into this list. <see cref="Geometry"/> is the embedded DTS subtree the record wraps —
/// see <see cref="Io.Transform.Dbsim.BasesDgsTransformer"/> for how the rest of the record's
/// bytes are walked.
/// </summary>
/// <param name="BoundingRadius">
/// The shape's coarse bounding radius, in world units — the third of the three <c>int16</c> fields
/// at the head of every <c>ClassItem</c> record (<c>shape+8</c>). Two unrelated consumers identify
/// it: the LOD selector (<c>Shape_DrawAtDetailLevel</c>, <c>004033e4</c>) divides it by viewing distance to estimate the
/// shape's size on screen, and the structure hit test (<c>Sim_RaycastShapeVolume</c>, <c>00427da8</c>) adds it to the ray
/// length before rejecting a candidate.
/// </param>
/// <param name="Geometry">
/// The shape's drawable geometry, or null for the rare record with no child object.
/// </param>
/// <param name="SequenceList">
/// Frame count per cell-animation sequence (<c>shape+0x20</c>), the same field
/// <see cref="TSShape.SequenceList"/> carries for a <c>.DTS</c> root: a <c>GridShape</c> is a
/// <c>TSShape</c>, so the record holds it itself rather than its <see cref="Geometry"/> child. Its
/// length is also the size of the cell-frame array a shape instance built on this shape gets.
/// </param>
/// <param name="NodeTransforms">The shape's own node transforms (<c>shape+0x18</c>); empty in every retail record.</param>
/// <param name="Collision">The shape's collision volume — see <see cref="GridShapeCollision"/>.</param>
public readonly record struct GridShape(
	short BoundingRadius, TSObject? Geometry, short[]? SequenceList, TSShapeNodeTransform[]? NodeTransforms,
	GridShapeCollision Collision);

/// <summary>
/// <c>dgs\BASES.DGS</c> / <c>dgs\BHULKS.DGS</c> — the static-structure shape library <c>dat\BASES.DAT</c>
/// selects into by index. See <see cref="Io.Transform.Dbsim.BasesDgsTransformer"/> for the format.
/// </summary>
public class GridShapeLibrary {
	public GridShape[]? Shapes { get; set; }
}
