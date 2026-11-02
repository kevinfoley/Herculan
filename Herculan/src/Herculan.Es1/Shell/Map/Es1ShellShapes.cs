using HercWorks.Core.Data.File.Dgs;
using HercWorks.Core.Data.File.Dts;
using HercWorks.Core.Data.File.Dts.Bsp;
using HercWorks.Core.Data.File.Dts.Part;
using HercWorks.Core.Data.File.Dts.Poly;
using Herculan.Engine.Numerics;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Map;

/// <summary>
/// A light of the shell's list, a <c>TSSunLight</c>: a directional light, its type <c>+0x16</c> 1, its intensity
/// <c>+0x1a</c> and its direction <c>+0xc</c>, and the direction's model-space copy <c>+0x2a</c>, which
/// <see cref="Es1ShellShapes.SetModelTransform"/> rebuilds for each object it draws (docs/es1/shell/map.md#the-shapes).
/// The war map's sun is the one the shell builds (docs/es1/shell/map.md#building-it).
/// </summary>
public sealed class Es1ShellLight {
	public Es1ShellLight(int intensity, (short X, short Y, short Z) direction) {
		Intensity = intensity;
		Direction = direction;
	}

	/// <summary>The intensity <c>+0x1a</c>, which <c>maybe_TSLight_SetIntensity</c> (<c>0005361c</c>) stores.</summary>
	public int Intensity { get; }

	/// <summary>The direction <c>+0xc</c>, three <c>int16</c>.</summary>
	public (short X, short Y, short Z) Direction { get; }

	/// <summary>The direction in the drawn object's space <c>+0x2a</c>.</summary>
	public (short X, short Y, short Z) ModelDirection { get; set; }
}

/// <summary>
/// The shell's <c>Light_ActiveList</c> (<c>000ab6b4</c>), ten light pointers counted at <c>Light_ActiveCount</c>
/// (<c>000ab6b0</c>), which <c>Light_ResetSystem</c> (<c>000641ec</c>) empties at the shell's start
/// (docs/es1/rendering/view-transform-and-projection.md).
/// </summary>
public sealed class Es1ShellLights {
	private const int Capacity = 10;

	private readonly List<Es1ShellLight> _active = new(Capacity);

	public IReadOnlyList<Es1ShellLight> Active => _active;

	/// <summary><c>Light_Register</c> (<c>00053800</c>): appends <paramref name="light"/> unless the list is full or holds it; whether it did.</summary>
	public bool Register(Es1ShellLight light) {
		if (_active.Count >= Capacity || _active.Contains(light)) {
			return false;
		}

		_active.Add(light);
		return true;
	}

	/// <summary><c>Light_Unregister</c> (<c>00053850</c>): the list's last light moved into <paramref name="light"/>'s place; whether it was there.</summary>
	public bool Unregister(Es1ShellLight light) {
		int index = _active.IndexOf(light);
		if (index < 0) {
			return false;
		}

		_active[index] = _active[^1];
		_active.RemoveAt(_active.Count - 1);
		return true;
	}
}

/// <summary>
/// The shell's shape renderer, through which <c>ShellMap_PaintShapes</c> (<c>0000aff0</c>) draws the zone's objects
/// (docs/es1/shell/map.md#the-shapes): <c>TSObj_Render</c> and the instance, part and poly renders it reaches, rasterized
/// in palette indices into the display's draw page through the map's viewport, as retail's <c>VGA_8</c> draws them. The
/// renderer's globals (the model transform, the eye in model space, the polygon scratch block, the ramp table) are this
/// object's fields, made for each paint.
///
/// <para><c>SHELL0.VOL</c>'s four theater libraries hold only what this renders: detail, part-list, BSP and
/// cell-animation parts over plain groups of solid, shaded and plain polys, every part's and BSP node's node transform
/// −1. A detail part with fewer thresholds than levels stops at its last threshold, an engine guard. Unported: the
/// other part and poly kinds the shell's renderer can draw (bitmap parts, BSP groups, Gouraud and textured polys), a
/// part or BSP node with a node transform, and a pixel or line whose colour is not a plain palette index; each draws
/// nothing here and is reported once.</para>
/// </summary>
public sealed class Es1ShellShapes {
	/// <summary>The near plane <c>Raster_NearPlane</c>, <c>0x80</c>, which <c>Map_Ctor</c>'s camera passes <c>TSCamera_Ctor</c>.</summary>
	public const int NearPlane = 0x80;

	/// <summary>
	/// <c>DAT_000884e0</c>, 1 in the image: the instance render tests the shape's bounds before drawing
	/// (<see cref="ScreenVisibility"/>).
	/// </summary>
	private static readonly bool TestsBounds = true;

	/// <summary><c>TSDetailPart_SizeScaleQ10</c> (<c>00088686</c>), <c>0x400</c> in the image.</summary>
	private static readonly short DetailSizeScale = 0x400;

	/// <summary><c>TSDetailPart_SizeCapQ10</c> (<c>00088688</c>), 0 in the image: no cap.</summary>
	private static readonly short DetailSizeCap = 0;

	/// <summary>A surface entry's top byte that draws nothing, <c>0x14</c>.</summary>
	private const int Hidden = 0x14;

	/// <summary>Brush mode 4, the outline.</summary>
	private const int OutlineMode = 4;

	private static readonly HashSet<string> Reported = [];

	private readonly Es1ShellLights _lights;
	private readonly Transform3 _worldToView;
	private readonly (int X, int Y) _centre;
	private readonly Es1PanelCanvas _clipped, _whole;
	private readonly Dictionary<int, short[]> _ramps = [];

	private Transform3 _modelToView;
	private Vec3i _eye;
	private int _clipMode = 1;
	private short[] _frames = [];

	// The polygon scratch block for the group being drawn: the points' state bytes, view points and screen points.
	private TSGroup _group = null!;
	private byte[] _state = [];
	private Vec3i[] _view = [];
	private (int X, int Y)[] _screen = [];
	private readonly List<(int X, int Y)> _ring = [];

	/// <summary>
	/// The renderer for one paint of <paramref name="map"/>, under the camera <see cref="Es1ShellMap.InstallCamera"/>
	/// installed: the projection centre the viewport's rect's top-left less its centre, the clip rect the viewport's,
	/// clip mode 1 and the translation flag clear, so points are the screen's.
	/// </summary>
	public Es1ShellShapes(Es1ShellMap map, Es1VideoMemory display) {
		_lights = map.Lights;
		_worldToView = map.WorldToView;
		_centre = (map.Rect.X0 - map.ViewportCentre.X, map.Rect.Y0 - map.ViewportCentre.Y);
		_clipped = new Es1PanelCanvas(display, map.Rect, (map.Rect.X0, map.Rect.Y0));
		_whole = new Es1PanelCanvas(display, new Es1Rect(0, 0, Es1VideoMemory.ShellWidth - 1, Es1CockpitViews.ScreenHeight - 1));
		Rect = map.Rect;
		SetModelTransform(Transform3.Identity);
	}

	/// <summary>The viewport's rect, the clip rect <c>Raster_ViewClipRect</c> and the bounds test's box.</summary>
	private Es1Rect Rect { get; }

	/// <summary><c>Raster_SetColourRamps</c>(<paramref name="ramp"/>, 1, {<paramref name="entries"/>}): one ramp of the raster's table.</summary>
	public void SetColourRamp(int ramp, short[] entries) => _ramps[ramp] = entries;

	/// <summary>
	/// <c>TSObj_Render</c> (<c>0000e7c9</c>) of a zone object: <c>SimObject_InstallModelTransform</c> (<c>0000e116</c>),
	/// <see cref="SetModelTransform"/> of the object's rotation (<see cref="Es1ShellTrig.Rotation"/> of its angles at
	/// <c>+4</c>, which <c>Context_GetTransform</c> (<c>0000ddc8</c>) builds once and keeps) at its position, then its
	/// instance's slot <c>+0x20</c>, <c>TSShapeInstance_Render</c> (<c>000588f8</c>), with the instance's cell frames
	/// <paramref name="frames"/>.
	/// </summary>
	public void RenderObject(GridShape shape, Es1MapObject obj, short[] frames) {
		var model = Es1ShellTrig.Rotation(obj.Angles.A, obj.Angles.B, obj.Angles.C);
		model.X = obj.Position.X;
		model.Y = obj.Position.Y;
		model.Z = obj.Position.Z;
		SetModelTransform(model);

		_frames = frames;
		int visibility = TestsBounds ? ScreenVisibility(shape.BoundingRadius, shape.Center.X, shape.Center.Y, shape.Center.Z) : 1;
		if (visibility == 0) {
			return;
		}

		// A shape wholly inside the viewport draws in clip mode 0, unclipped.
		if (visibility == 2 && _clipMode == 1) {
			_clipMode = 0;
			RenderShapeParts(shape);
			_clipMode = 1;
		} else {
			RenderShapeParts(shape);
		}
	}

	/// <summary>
	/// <c>Raster_SetModelTransform</c> (<c>00053ebc</c>): the model-to-view transform the model's then the camera's; the
	/// eye <c>g_EyeInModelSpace</c> the view origin in the model's space; and, <c>Light_ModelSpaceEnabled</c> being set by
	/// the shell's <c>Light_ResetSystem</c>, each registered light's direction turned into the model's space by the
	/// model's inverse (<c>Light_TransformToModelSpace</c>, <c>00053630</c>, through <c>RotateVectorByMatrixQ14</c>,
	/// <c>0006780c</c>).
	/// </summary>
	public void SetModelTransform(Transform3 model) {
		_modelToView = Transform3.Concat(model, _worldToView);
		var viewToModel = _modelToView.Inverted();
		_eye = new Vec3i(viewToModel.X, viewToModel.Y, viewToModel.Z);

		var toModel = model.Inverted();
		foreach (var light in _lights.Active) {
			var (x, y, z) = light.Direction;
			var turned = toModel.RotateVector(x, y, z);
			light.ModelDirection = unchecked(((short)turned.X, (short)turned.Y, (short)turned.Z));
		}
	}

	/// <summary><c>TSPartList_Render</c> (<c>0005980c</c>) of the shape itself: its one part, the record's child.</summary>
	private void RenderShapeParts(GridShape shape) {
		if (shape.Geometry is { } part) {
			RenderPart(part);
		}
	}

	/// <summary>A part's or group's slot <c>+0x20</c>.</summary>
	private void RenderPart(TSObject part) {
		if (part is TSBasePart { Transform: >= 0 }) {
			ReportUnported("a part with a node transform");
			return;
		}

		switch (part) {
			case TSDetailPart detail:
				RenderDetail(detail);
				break;
			case TSBSPPart bsp:
				RenderBspNode(bsp, 0);
				break;
			case TSCellAnimPart cell:
				RenderCellAnim(cell);
				break;
			case TSBSPGroup:
				ReportUnported(nameof(TSBSPGroup));
				break;
			case TSGroup group:
				RenderGroup(group);
				break;
			case TSPartList list:
				// TSPartList_Render (0005980c): each part in turn.
				foreach (var child in list.Parts ?? []) {
					RenderPart(child);
				}

				break;
			default:
				ReportUnported(part.GetType().Name);
				break;
		}
	}

	/// <summary>
	/// The shell's <c>TSDetailPart_Render</c> (<c>00059aa8</c>) (docs/es1/shell/map.md#the-shapes): the projected size,
	/// the part's radius shifted left by the focal shift over the eye's distance from the model's origin
	/// (<c>Math_FastMagnitude3D</c>) less the radius, at least 1, and at least 1 again after the scale
	/// <see cref="DetailSizeScale"/> and the cap; the level the first whose threshold is at least the size, else the last.
	/// </summary>
	private void RenderDetail(TSDetailPart detail) {
		var parts = detail.Parts ?? [];
		if (parts.Length == 0) {
			return;
		}

		short radius = detail.Radius;
		int size = SimMath.FastMagnitude3D(_eye.X, _eye.Y, _eye.Z) - radius;
		if (size < 1) {
			size = 1;
		}

		size = unchecked((radius << Es1ShellMap.FocalShift) / size);
		if (size == 0) {
			size = 1;
		}

		size = Q10Multiply(size, DetailSizeScale);
		if (DetailSizeCap != 0) {
			int cap = Q10Multiply(radius, DetailSizeCap);
			if (cap < size) {
				size = cap;
			}
		}

		var thresholds = detail.Details ?? [];
		int level = 0;
		while (level < parts.Length - 1 && level < thresholds.Length && size > thresholds[level]) {
			level++;
		}

		RenderPart(parts[level]);
	}

	/// <summary><c>Math_Q10Multiply</c> (<c>0005a298</c>): the product plus <c>0x200</c>, shifted right 10.</summary>
	private static int Q10Multiply(int value, short scale) => unchecked((int)(((long)value * scale + 0x200) >> 10));

	/// <summary>
	/// <c>TSBSPPart_RenderNode</c> (<c>00059c44</c>), under <c>TSBSPPart_Render</c> (<c>00059d68</c>) from node 0: the
	/// eye's side of the node's plane, its normal dotted with the eye less its coefficient; behind it, the front child then
	/// the back one, otherwise the back then the front. A child of −1 is none, one with bit <c>0x4000</c> the part it
	/// names, any other the node it names.
	/// </summary>
	private void RenderBspNode(TSBSPPart bsp, int index) {
		var nodes = bsp.Nodes ?? [];
		if (index >= nodes.Length) {
			return;
		}

		if (bsp.Transforms is { } transforms && index < transforms.Length && transforms[index] != -1) {
			ReportUnported("a BSP node with a node transform");
			return;
		}

		var node = nodes[index];
		var normal = node.Normal!;
		int side = unchecked(normal.X * _eye.X + normal.Y * _eye.Y + normal.Z * _eye.Z - node.Coeff);
		var (first, second) = side < 0 ? (node.Front, node.Back) : (node.Back, node.Front);
		RenderBspChild(bsp, first);
		RenderBspChild(bsp, second);
	}

	private void RenderBspChild(TSBSPPart bsp, short child) {
		if (child < 0) {
			return;
		}

		if ((child & 0x4000) == 0) {
			RenderBspNode(bsp, child);
		} else if (bsp.Parts is { } parts && (child & 0x3fff) < parts.Length) {
			RenderPart(parts[child & 0x3fff]);
		}
	}

	/// <summary>
	/// <c>TSCellAnimPart_Render</c> (<c>000599c4</c>): the part the instance's frame of the part's sequence names, taken
	/// modulo the part count, unsigned. A sequence past the instance's frames reads frame 0 here, an engine guard.
	/// </summary>
	private void RenderCellAnim(TSCellAnimPart cell) {
		var parts = cell.Parts ?? [];
		if (parts.Length == 0) {
			return;
		}

		int sequence = cell.AnimSequence;
		ushort frame = sequence >= 0 && sequence < _frames.Length ? (ushort)_frames[sequence] : (ushort)0;
		RenderPart(parts[frame % parts.Length]);
	}

	/// <summary>
	/// <c>TSBounds_ScreenVisibility</c> (<c>00059444</c>), the shell's (docs/es1/shell/map.md#the-shapes): 0 when the
	/// centre's depth plus the radius is short of the near plane or the box of the projected radius about the projected
	/// centre misses the viewport; 2 when in clip mode 1 the box lies strictly inside it; otherwise 1. The projected
	/// radius is ((radius &lt;&lt; focal shift) + depth / 2) / depth, its sign removed, a depth of 0 taken as 1.
	/// </summary>
	private int ScreenVisibility(short radius, short x, short y, short z) {
		var centre = _modelToView.TransformPoint(x, y, z);
		if (NearPlane > unchecked(centre.Y + radius)) {
			return 0;
		}

		int depth = centre.Y == 0 ? 1 : centre.Y;
		int projected = unchecked(((radius << Es1ShellMap.FocalShift) + (depth >> 1)) / depth);
		if (projected < 0) {
			projected = -projected;
		}

		var (sx, sy) = Project(centre.X, depth, centre.Z);
		if (unchecked(Rect.X0 > sx + projected || Rect.Y0 > sy + projected || sx - projected > Rect.X1 || sy - projected > Rect.Y1)) {
			return 0;
		}

		bool inside = unchecked(_clipMode == 1 && sx + projected < Rect.X1 && sy + projected < Rect.Y1
			&& Rect.X0 < sx - projected && Rect.Y0 < sy - projected);
		return inside ? 2 : 1;
	}

	/// <summary><c>Raster_PerspectiveDivide</c> and <c>Raster_ProjectToScreen</c>: (<i>x</i> &lt;&lt; 7) / <i>y</i> right of the centre, (<i>z</i> &lt;&lt; 7) / <i>y</i> above it.</summary>
	private (int X, int Y) Project(int x, int depth, int z) =>
		unchecked(((x << Es1ShellMap.FocalShift) / depth + _centre.X, _centre.Y - (z << Es1ShellMap.FocalShift) / depth));

	/// <summary>
	/// <c>TSGroup_RenderPolys</c> (<c>0007cd3c</c>): the render context's brushes swapped for the poly brushes and its
	/// translation flag cleared for the group; the group's points, indexes and surfaces bound; the points' state bytes
	/// cleared; each poly's slot <c>+0x20</c> in file order.
	/// </summary>
	private void RenderGroup(TSGroup group) {
		var points = group.Points ?? [];
		_group = group;
		_state = new byte[points.Length];
		_view = new Vec3i[points.Length];
		_screen = new (int X, int Y)[points.Length];
		foreach (var poly in group.Polys ?? []) {
			switch (poly) {
				case TSGouraudPoly or TSTexture4Poly:
					ReportUnported(poly.GetType().Name);
					break;
				case TSShadedPoly shaded:
					RenderSolid(shaded, shaded: true);
					break;
				case TSSolidPoly solid:
					RenderSolid(solid, shaded: false);
					break;
				case TSPoly:
					// TSPoly_Render (0007c3fc): returns.
					break;
				default:
					ReportUnported(poly.GetType().Name);
					break;
			}
		}
	}

	/// <summary>
	/// <c>TSSolidPoly_Render</c> (<c>0007c400</c>) and <c>TSShadedPoly_Render</c> (<c>0007c7f8</c>)
	/// (docs/es1/rendering/polygon-shading.md#the-four-poly-types): the front pair of the poly's surface entries (fill,
	/// line) when <c>TSPoly_FrontBackVisibilityTest</c> finds it facing the eye, the back pair otherwise; nothing when
	/// both carry <see cref="Hidden"/> in their top byte. A shaded poly ORs its light into bits 16–23 of both, from its
	/// normal, negated for a back face. Then the corners projected, cut at the near plane when one lies on or before it,
	/// and <c>PolyFill_FillThenOutline</c> under brush mode 0, reversed for a back face.
	/// </summary>
	private void RenderSolid(TSSolidPoly poly, bool shaded) {
		var points = _group.Points!;
		var center = points[poly.Center];
		var normal = points[poly.Normal];
		int entry = (ushort)poly.ColorIndexId;
		bool front = FacesEye(center, normal) >= 1;
		int fill = SurfaceEntry(front ? entry : entry + 2), line = SurfaceEntry(front ? entry + 1 : entry + 3);
		if (fill >> 24 == Hidden && line >> 24 == Hidden) {
			return;
		}

		if (shaded) {
			int light = front
				? ShadeForFace(normal.X, normal.Y, normal.Z)
				: ShadeForFace(unchecked((short)-normal.X), unchecked((short)-normal.Y), unchecked((short)-normal.Z));
			fill |= light << 16;
			line |= light << 16;
		}

		ProjectRing(poly.VertexList, poly.VertexCount);
		FillThenOutline(fill, line, reversed: !front);
	}

	/// <summary>
	/// <c>TSPoly_FrontBackVisibilityTest</c> (<c>000541f8</c>) with the focal shift not 0: the normal dotted with the eye
	/// less the centre, in 32 bits; the poly faces the eye when it is 1 or more.
	/// </summary>
	private int FacesEye(HercWorks.Core.Data.Struct.Vec3Short center, HercWorks.Core.Data.Struct.Vec3Short normal) => unchecked(
		normal.X * (_eye.X - center.X) + normal.Y * (_eye.Y - center.Y) + normal.Z * (_eye.Z - center.Z));

	/// <summary>
	/// Surface entry <paramref name="index"/> of the group, the file's <c>{int16 value, int16 flag}</c> read as one
	/// <c>int32</c>; four entries a record (front fill, front line, back fill, back line). One past the group's surfaces,
	/// which retail reads past its array, is hidden here, an engine guard.
	/// </summary>
	private int SurfaceEntry(int index) {
		var surfaces = _group.Surfaces ?? [];
		if (index >> 2 >= surfaces.Length) {
			return Hidden << 24;
		}

		var s = surfaces[index >> 2];
		var (value, flag) = (index & 3) switch {
			0 => (s.FrontColor, s.FrontFlag),
			1 => (s.FrontLineColor, s.FrontLineFlag),
			2 => (s.BackColor, s.BackFlag),
			_ => (s.BackLineColor, s.BackLineFlag),
		};
		return flag << 16 | (ushort)value;
	}

	/// <summary>
	/// <c>Light_ComputeShadeForFace</c> (<c>000539ac</c>) over the shell's lights, each a directional one
	/// (docs/es1/rendering/polygon-shading.md#a-faces-light): with <i>v</i> = (normal · model direction −
	/// <c>0x400000</c>) &gt;&gt; 1 negative, −(intensity · <i>v</i>) &gt;&gt; 22 added; the sum at most 255.
	/// </summary>
	private int ShadeForFace(short x, short y, short z) {
		int shade = 0;
		foreach (var light in _lights.Active) {
			var d = light.ModelDirection;
			int v = unchecked(x * d.X + y * d.Y + z * d.Z - 0x400000) >> 1;
			if (v < 0) {
				shade = unchecked(shade - (light.Intensity * v >> 22));
			}
		}

		return Math.Min(shade, 0xff) & 0xff;
	}

	/// <summary>
	/// <c>Poly_ProjectShapeVerticesPerspective</c> (<c>00067014</c>), and <c>Poly_ClipRingToNearPlane</c>
	/// (<c>000549c8</c>) when a corner lies on or before the near plane: each corner not yet seen this group taken into
	/// view space (<c>Transform_ApplyToShortPoint</c>) and, beyond the near plane, projected and kept (state 2), otherwise
	/// marked (state 1). The clip walks the corners from the last: an edge between two kept corners adds the second's
	/// screen point; an edge with one kept end adds its crossing of the near plane, (Δ<i>x</i> · (near − <i>y</i>)) /
	/// Δ<i>y</i> + <i>x</i> from the edge's first end, its height likewise and negated, both in 32 bits and moved by the
	/// centre, then the second end's screen point when it is the kept one.
	/// </summary>
	private void ProjectRing(short first, short count) {
		var points = _group.Points!;
		var indexes = _group.Indexes ?? [];
		_ring.Clear();
		bool clip = false;
		for (int k = 0; k < count; k++) {
			int point = indexes[first + k];
			if (_state[point] == 0) {
				var p = points[point];
				var view = _modelToView.TransformPoint(p.X, p.Y, p.Z);
				_view[point] = view;
				if (NearPlane < view.Y) {
					_screen[point] = Project(view.X, view.Y, view.Z);
					_state[point] = 2;
					_ring.Add(_screen[point]);
				} else {
					_state[point] = 1;
					clip = true;
				}
			} else if (_state[point] == 2) {
				_ring.Add(_screen[point]);
			} else {
				clip = true;
			}
		}

		if (!clip) {
			return;
		}

		_ring.Clear();
		int previous = indexes[first + count - 1];
		for (int k = 0; k < count; k++) {
			int current = indexes[first + k];
			if (_state[previous] == 2 && _state[current] == 2) {
				_ring.Add(_screen[current]);
			} else if (_state[previous] == 2 || _state[current] == 2) {
				var a = _view[previous];
				var b = _view[current];
				unchecked {
					int toNear = NearPlane - a.Y, run = b.Y - a.Y;
					int x = (b.X - a.X) * toNear / run + a.X;
					int y = -((b.Z - a.Z) * toNear / run + a.Z);
					_ring.Add((x + _centre.X, y + _centre.Y));
				}

				if (_state[current] == 2) {
					_ring.Add(_screen[current]);
				}
			}

			previous = current;
		}
	}

	/// <summary>
	/// <c>PolyFill_FillThenOutline</c> (<c>0005506c</c>) of the ring: nothing for no points; the ring through
	/// <c>Raster_DrawPolygonDispatch</c> (<c>00055584</c>) under the brush {0, <paramref name="fill"/>}, or reversed
	/// first (<c>Raster_DrawPolygonReversed</c>, <c>000558e7</c>, which reverses a ring of three points or more); then,
	/// when <paramref name="line"/> differs from <paramref name="fill"/>, again under the brush {4, line}.
	/// </summary>
	private void FillThenOutline(int fill, int line, bool reversed) {
		if (_ring.Count == 0) {
			return;
		}

		if (reversed && _ring.Count > 2) {
			_ring.Reverse();
		}

		DrawPolygon(0, fill);
		if (line != fill) {
			DrawPolygon(OutlineMode, line);
		}
	}

	/// <summary>
	/// <c>Raster_DrawPolygonDispatch</c> of the ring under the brush {<paramref name="mode"/>, <paramref name="colour"/>}
	/// (docs/es1/shell/map.md#the-shapes): one point a pixel and two a line, from the second to the first, and in mode 4 the
	/// line loop round the ring, each under the pen {0, colour}; in mode 0, the colour resolved by its top byte (0 the
	/// palette index itself, 4 entry (<i>s</i> · length) &gt;&gt; 8 of ramp <i>r</i>, <i>r</i> its low 16 bits and
	/// <i>s</i> bits 16–23, anything else nothing) and the ring filled, cut to the viewport in clip mode 1 and not in clip
	/// mode 0. In clip mode 0 the pixels, lines and fills are cut to the screen here, an engine guard.
	/// </summary>
	private void DrawPolygon(int mode, int colour) {
		var canvas = _clipMode == 0 ? _whole : _clipped;
		if (_ring.Count < 3 || mode == OutlineMode) {
			if (colour >> 24 != 0) {
				ReportUnported("a pixel or line under a ramp colour");
				return;
			}

			byte pen = (byte)colour;
			if (_ring.Count == 1) {
				canvas.Plot(_ring[0].X, _ring[0].Y, pen);
			} else if (_ring.Count == 2) {
				canvas.DrawLine(_ring[1].X, _ring[1].Y, _ring[0].X, _ring[0].Y, pen);
			} else {
				for (int i = 0; i + 1 < _ring.Count; i++) {
					canvas.DrawLine(_ring[i + 1].X, _ring[i + 1].Y, _ring[i].X, _ring[i].Y, pen);
				}

				canvas.DrawLine(_ring[0].X, _ring[0].Y, _ring[^1].X, _ring[^1].Y, pen);
			}

			return;
		}

		if (ResolveColour(colour) is { } index) {
			canvas.FillPolygon(_ring, index);
		}
	}

	/// <summary>Brush mode 0's colour: the index for top byte 0, the ramp's entry for 4, none for any other. A ramp the table lacks gives none here, an engine guard.</summary>
	private byte? ResolveColour(int colour) {
		switch (colour >> 24) {
			case 0:
				return (byte)colour;
			case 4:
				if (!_ramps.TryGetValue((short)colour, out var entries) || entries.Length == 0) {
					ReportUnported($"ramp {(short)colour}, which the shell's table does not hold");
					return null;
				}

				int shade = colour >> 16 & 0xff;
				return (byte)entries[(shade * entries.Length >> 7 & 0xfffe) >> 1];
			default:
				return null;
		}
	}

	private static void ReportUnported(string what) {
		lock (Reported) {
			if (Reported.Add(what)) {
				Console.Error.WriteLine($"The shell's shape renderer met {what}, which it does not draw (Unported).");
			}
		}
	}
}
