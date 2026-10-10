using Herculan.Engine.Numerics;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Render;

/// <summary>
/// The type tag at an object's <c>+0x04</c>, as far as the draw table reads it: tag 9 is drawn on the
/// spot in its cell, and tags 0, 5 and 8 pick their own draw distance — see
/// docs/retail/rendering/terrain-drawing.md, "Objects in the walk".
/// </summary>
public enum ObjectTypeTag : short {
	/// <summary>A drop pod. <c>Meteor_Construct</c> never writes the tag, so it keeps the zero its pool starts with.</summary>
	DropPod = 0,

	/// <summary>A piece of wreckage.</summary>
	Debris = 1,

	/// <summary>An impact effect whose <c>EXPLOS.DAT</c> row's <c>+0x26</c> is 0.</summary>
	Effect = 2,

	/// <summary>A bullet, a launcher round or a beam tracer.</summary>
	Projectile = 3,

	/// <summary>A smoke ball or a fire.</summary>
	Fire = 4,

	/// <summary>A structure, ground vehicles included.</summary>
	Structure = 5,

	/// <summary>A flyer.</summary>
	Flyer = 6,

	/// <summary>A HERC.</summary>
	Herc = 7,

	/// <summary>An impact effect whose row's <c>+0x26</c> is set.</summary>
	EffectFar = 8,

	/// <summary>A ground shape.</summary>
	GroundShape = 9,
}

/// <summary>
/// One object as <c>Scene_SubmitFrameObjects</c> (<c>0042841c</c>) submits it: what files it under a
/// terrain cell, and what <c>ObjList_IsBeyondDrawDistance</c> (<c>00428c08</c>) measures. Everything an
/// object draws as shares its entry — a HERC's guns are parts of its shape in the original.
/// <see cref="Drawn"/> is the answer, settled per pass by <see cref="ObjectDrawTable.File"/>.
/// </summary>
public sealed class DrawEntry {
	public DrawEntry(object subject) {
		Subject = subject;
	}

	/// <summary>
	/// What the entry draws. Another entry's <see cref="FileWith"/> names it by this, and the camera's
	/// attached object is compared against it.
	/// </summary>
	public object Subject { get; }

	/// <summary>The tag the draw-distance pick reads.</summary>
	public ObjectTypeTag Tag { get; set; }

	/// <summary>vtable <c>+0x04</c>, the position the cell is picked from and the distance is measured from.</summary>
	public Vec3i Position { get; set; }

	/// <summary>
	/// The radius <c>HeightGrid_PickDrawCell</c> takes: the body radius (vtable <c>+0x5c</c>) for a
	/// structure, a HERC or a flyer, the shape radius (vtable <c>+0x10</c>) for everything else.
	/// </summary>
	public int FilingRadius { get; set; }

	/// <summary>
	/// The subject whose cell this entry is filed under instead of its own —
	/// <c>Scene_SubmitObjectAtCell</c> (<c>004283b4</c>) with that object's cached cell. Null files by
	/// <see cref="Position"/> and <see cref="FilingRadius"/>.
	/// </summary>
	public object? FileWith { get; set; }

	/// <summary>vtable <c>+0x10</c>, which splits structures between two draw distances.</summary>
	public int ShapeRadius { get; set; }

	/// <summary>
	/// What <c>ObjList_DrawCellObjects</c> (<c>00428c60</c>) adds to <see cref="Position"/>'s Z before
	/// measuring a structure or a HERC: <see cref="SimObject.SightHeight"/>. Zero for every other tag.
	/// </summary>
	public int EntryHeight { get; set; }

	/// <summary>
	/// The object whose riding camera keeps this entry out of the pass altogether: an impact effect's
	/// owner, unless its type is one <c>Explosion_IsHiddenFromOwnerCockpit</c> (<c>00408240</c>) always
	/// draws. Null for everything else.
	/// </summary>
	public object? HiddenWhenRidden { get; set; }

	/// <summary>Whether this pass draws it. True until a pass has filed it.</summary>
	public bool Drawn { get; internal set; } = true;

	/// <summary>The cell this pass filed it under, null for the no-cell bucket.</summary>
	internal (int X, int Y)? Cell { get; set; }
}

/// <summary>
/// The original's per-cell object filing and the terrain walk's culling, reduced to what they decide
/// here: <b>whether</b> each object is drawn. The depth buffer still decides what covers what.
///
/// <para>Each pass, <see cref="File"/> files every entry under a cell as <c>Scene_SubmitFrameObjects</c>
/// (<c>0042841c</c>) does — by <c>HeightGrid_PickDrawCell</c>, or under another object's cell for a
/// HERC standing inside a structure, an owned impact effect and a fire — and then draws an entry only
/// where the original's draw would reach it: in a cell the terrain walk visits
/// (<see cref="TerrainCellWalk"/>), in the viewer's own cell, in a flying player's cell when the camera
/// is not riding it, or off the grid altogether. Within a drawn cell everything but a ground shape is
/// also left out when the camera rides it or when it is farther than its class's draw distance, and an
/// impact effect on the hull of what the camera rides is not submitted at all
/// (<see cref="DrawEntry.HiddenWhenRidden"/>). See docs/retail/rendering/terrain-drawing.md, "Objects in the
/// walk" and "After the walk".</para>
///
/// <para>A filed object's cell is kept between passes, as the original keeps it at <c>+0x1e8</c>, so
/// an entry filed with an object that this pass did not file (one still waiting to deploy) takes the
/// cell it was last filed under. One that was never filed at all goes to the no-cell bucket; this is
/// a divergence from retail for a case no retail path reaches, since a waiting object is neither struck
/// nor stood in.</para>
/// </summary>
public sealed class ObjectDrawTable {
	/// <summary>
	/// The Q10 factors at <c>0049abb0</c> that <c>ObjList_SetDrawDistances</c> (<c>00428bc0</c>) scales the
	/// terrain draw radius by, in the order <c>ObjList_IsBeyondDrawDistance</c> indexes them.
	/// </summary>
	private static readonly int[] DrawDistanceFactors = { 800, 900, 1000, 800, 1200 };

	/// <summary>The shape radius from which a structure takes the far structure distance.</summary>
	private const int LargeStructureRadius = 7000;

	private readonly Dictionary<object, (int X, int Y)?> _cells = new(ReferenceEqualityComparer.Instance);
	private readonly int[] _drawDistances = new int[DrawDistanceFactors.Length];
	private int[] _walkedStamp = Array.Empty<int>();
	private int _pass;
	private HeightGrid? _grid;
	private TerrainVisibleRegion? _region;
	private Vec3i _viewer;
	private bool _walked;
	private (int X, int Y) _viewerCell;
	private (int X, int Y)? _playerCell;

	/// <summary>
	/// Every object to file this frame, in the order the original submits them. A structure, a HERC
	/// and a flyer come before anything filed with one.
	/// </summary>
	public List<DrawEntry> Entries { get; } = new();

	/// <summary>
	/// The object the camera of the pass about to be drawn rides — <c>Cam_IsAttachedTo</c>
	/// (<c>00401078</c>)'s one true answer, or null.
	/// </summary>
	public SimObject? CameraAttachedTo { get; set; }

	/// <summary>The machine the player pilots, which <c>ObjList_DrawAfterTerrain</c> (<c>0042883c</c>) tests.</summary>
	public MechObject? LocalPlayer { get; set; }

	/// <summary>
	/// Files every entry for a view at <paramref name="viewer"/> and settles each one's
	/// <see cref="DrawEntry.Drawn"/>. <paramref name="region"/> must already be this view's, as
	/// <c>Terrain_SetupVisibleRegion</c> builds it before the submit.
	/// </summary>
	public void File(HeightGrid grid, TerrainVisibleRegion region, Vec3i viewer, TerrainPaintOrder order) {
		_grid = grid;
		_region = region;
		_viewer = viewer;
		_viewerCell = (order.CentreX, order.CentreY);
		SetDrawDistances(grid);
		Walk(grid, region, order);

		foreach (var entry in Entries) {
			var cell = entry.FileWith is { } with
				? _cells.TryGetValue(with, out var cached) ? cached : null
				: grid.PickDrawCell(entry.Position, entry.FilingRadius, viewer, region);

			entry.Cell = cell;

			// Only the structure, machine and flyer walks store the cell back on the object (+0x1e8),
			// and those are the only objects anything is filed with.
			if (entry.Tag is ObjectTypeTag.Structure or ObjectTypeTag.Herc or ObjectTypeTag.Flyer) {
				_cells[entry.Subject] = cell;
			}
		}

		// ObjList_DrawAfterTerrain: a flyer's pilot outside its cockpit has its own cell drawn after the
		// walk, whether or not the walk reached it.
		_playerCell = LocalPlayer is { Type.IsFlyer: true } player && !ReferenceEquals(player, CameraAttachedTo)
				&& _cells.TryGetValue(player, out var playerCell)
			? playerCell
			: null;

		foreach (var entry in Entries) {
			entry.Drawn = !(entry.HiddenWhenRidden != null && ReferenceEquals(entry.HiddenWhenRidden, CameraAttachedTo))
				&& Decide(entry.Cell, entry.Tag, entry.Subject, entry.Position, entry.EntryHeight,
					entry.ShapeRadius);
		}
	}

	/// <summary>
	/// Whether an object nothing else is filed with would be drawn this pass — a beam tracer, which
	/// <c>Scene_SubmitObject</c> (<c>004282d8</c>) files by its own position like any other. Answers
	/// true before the first <see cref="File"/>.
	/// </summary>
	public bool WouldDraw(Vec3i position, int radius, ObjectTypeTag tag) {
		if (_grid is not { } grid || _region is not { } region) {
			return true;
		}

		return Decide(grid.PickDrawCell(position, radius, _viewer, region), tag, null, position, 0, 0);
	}

	/// <summary>
	/// Whether this pass's draw reaches the objects filed under a cell: the walk visits it, it is the
	/// viewer's cell, or it is the flying player's. A ground shape's own answer.
	/// </summary>
	public bool CellDrawn((int X, int Y) cell) {
		if (_grid is not { } grid) {
			return true;
		}

		if (cell == _playerCell) {
			return true;
		}

		// Terrain_DrawVisibleCells draws the viewer's cell after the walk when the walk missed it, but
		// only when it walks at all.
		if (!_walked) {
			return false;
		}

		return cell == _viewerCell
			|| (cell.X >= 0 && cell.X < grid.Width && cell.Y >= 0 && cell.Y < grid.Height
				&& _walkedStamp[cell.Y * grid.Width + cell.X] == _pass);
	}

	/// <summary>
	/// The draw rule: the no-cell bucket is drawn with no test at all (<c>ObjList_DrawAfterTerrain</c>
	/// calls each one's draw slot directly); a cell the draw does not reach draws nothing; within one it
	/// does, a ground shape is drawn on the spot, and anything else is skipped when the camera rides it
	/// or when it lies beyond its class's draw distance (<c>ObjList_DrawCellObjects</c>, <c>00428c60</c>).
	/// </summary>
	private bool Decide((int X, int Y)? cell, ObjectTypeTag tag, object? subject, Vec3i position,
			int entryHeight, int shapeRadius) {
		if (cell is not { } filed) {
			return true;
		}

		if (!CellDrawn(filed)) {
			return false;
		}

		if (tag == ObjectTypeTag.GroundShape) {
			return true;
		}

		if (subject != null && ReferenceEquals(subject, CameraAttachedTo)) {
			return false;
		}

		var measured = tag is ObjectTypeTag.Structure or ObjectTypeTag.Herc
			? new Vec3i(position.X, position.Y, position.Z + entryHeight)
			: position;

		return !IsBeyondDrawDistance(tag, shapeRadius, measured.ApproxDistanceTo(_viewer));
	}

	/// <summary>
	/// <c>ObjList_IsBeyondDrawDistance</c> (<c>00428c08</c>): tag 0 takes the second distance, a structure
	/// the fourth or, at a shape radius of <see cref="LargeStructureRadius"/> or more, the fifth, tag 8
	/// the third, and everything else the first.
	/// </summary>
	private bool IsBeyondDrawDistance(ObjectTypeTag tag, int shapeRadius, int distance) {
		int index = tag switch {
			ObjectTypeTag.DropPod => 1,
			ObjectTypeTag.Structure => shapeRadius >= LargeStructureRadius ? 4 : 3,
			ObjectTypeTag.EffectFar => 2,
			_ => 0,
		};

		return _drawDistances[index] < distance;
	}

	/// <summary>
	/// <c>ObjList_SetDrawDistances</c> (<c>00428bc0</c>), run from <c>Terrain_SetupVisibleRegion</c>: the
	/// terrain draw radius scaled by each factor.
	/// </summary>
	private void SetDrawDistances(HeightGrid grid) {
		int radius = (int)grid.VisibilityRange;
		for (int i = 0; i < DrawDistanceFactors.Length; i++) {
			_drawDistances[i] = SimMath.Q10Multiply(radius, DrawDistanceFactors[i]);
		}
	}

	/// <summary>
	/// Marks the cells this pass's walk visits. <c>Terrain_DrawVisibleCells</c> does nothing while the
	/// region flag (<c>grid+0x11c</c>) is clear, and then neither the walk nor the viewer's cell draws.
	/// </summary>
	private void Walk(HeightGrid grid, TerrainVisibleRegion region, TerrainPaintOrder order) {
		_pass++;
		_walked = region.Visible;
		if (!_walked) {
			return;
		}

		int width = grid.Width;
		int height = grid.Height;
		if (_walkedStamp.Length != width * height) {
			_walkedStamp = new int[width * height];
		}

		int pass = _pass;
		var stamps = _walkedStamp;
		TerrainCellWalk.Visit(region.Cells, order.CentreX, order.CentreY, order.ByColumn, (x, y) => {
			if (x >= 0 && x < width && y >= 0 && y < height) {
				stamps[y * width + x] = pass;
			}
		});
	}
}
