using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;

namespace Herculan.Engine.Editor;

/// <summary>
/// The move handles drawn on the selected object, waypoint or group, or on each corner of the selected trigger area
/// (<see cref="PointMover.TargetsOf"/>): an arrow along world X (red) and world Y (green), a square in the ground plane
/// between them that moves it both ways at once, and for a flyer or a flyer group an arrow up world Z (blue) — the one
/// height the mission sets (<see cref="PointMover.HasOwnHeight(MoveTarget)"/>). A circle's radius has a handle of its own,
/// a diamond on its edge, dragged in the ground plane.
/// Dragging one previews the move through <see cref="PointMover.Preview"/> and commits it, as one undo step, on release.
/// The handles keep one size on screen however far away the target is, and draw through the terrain and other objects.
/// </summary>
internal sealed class MoveGizmo {
	private static readonly Vector3 XColor = new(0.95f, 0.3f, 0.3f);
	private static readonly Vector3 YColor = new(0.35f, 0.9f, 0.35f);
	private static readonly Vector3 ZColor = new(0.35f, 0.55f, 1f);
	private static readonly Vector3 PlaneColor = new(1f, 0.85f, 0.1f);
	private static readonly Vector3 HotColor = new(1f, 1f, 0.6f);
	private static readonly Vector3 RadiusColor = new(0.3f, 0.9f, 0.95f);

	// Render-space directions of world X, Y and Z (WorldScale.ToRender).
	private static readonly Vector3 WorldX = Vector3.UnitX;
	private static readonly Vector3 WorldY = -Vector3.UnitZ;
	private static readonly Vector3 WorldZ = Vector3.UnitY;

	/// <summary>An arrow's length as a share of the camera's distance from the object, which keeps it one size on screen.</summary>
	private const float LengthPerDistance = 0.15f;

	/// <summary>Where the ground-plane square starts and ends along each arrow, as a share of its length.</summary>
	private const float PlaneNear = 0.3f;
	private const float PlaneFar = 0.55f;

	/// <summary>How close, in window pixels, the pointer must be to an arrow to take it.</summary>
	private const float GrabPixels = 8f;

	/// <summary>The radius handle's half-width, as a share of an arrow's length.</summary>
	private const float DiamondSize = 0.08f;

	private readonly SelectionState _selection;
	private readonly PointMover _mover;

	private Handle _dragging = Handle.None;
	private MoveTarget? _target;
	private Vec3i _start;
	private Vector3 _origin;
	private Vector3 _grab;
	private float _grabAlong;
	private Vec3i _current;

	public MoveGizmo(SelectionState selection, PointMover mover) {
		_selection = selection;
		_mover = mover;
	}

	private enum Handle { None, X, Y, Z, Plane, Radius }

	/// <summary>Whether a drag is under way, which the viewport's click-to-select leaves alone.</summary>
	public bool Dragging => _dragging != Handle.None;

	/// <summary>What of the selection can be moved: none, one target, or a trigger area's corners and radius.</summary>
	private IReadOnlyList<MoveTarget> Movables => _mover.TargetsOf(_selection.Current);

	/// <summary>Draws the dragged target's handles, or every movable target's, the one under the pointer lit.</summary>
	public void Draw(WireframeRenderer wireframe, Camera camera, float aspect, Vector2 pointer, Vector2 viewport) {
		if (_target != null) {
			DrawTarget(wireframe, camera, aspect, _target, _dragging);
			return;
		}

		var movables = Movables;
		var (hotTarget, hotHandle) = HandleUnder(camera, movables, pointer, viewport);
		foreach (var target in movables) {
			DrawTarget(wireframe, camera, aspect, target, ReferenceEquals(target, hotTarget) ? hotHandle : Handle.None);
		}
	}

	private void DrawTarget(WireframeRenderer wireframe, Camera camera, float aspect, MoveTarget target, Handle hot) {
		var origin = _mover.HandleOrigin(target);
		float length = Length(camera, origin);
		if (target is AreaRadiusTarget) {
			DrawDiamond(wireframe, camera, aspect, origin, length, hot == Handle.Radius ? HotColor : RadiusColor);
			return;
		}

		DrawArrow(wireframe, camera, aspect, origin, WorldX, length, hot == Handle.X ? HotColor : XColor);
		DrawArrow(wireframe, camera, aspect, origin, WorldY, length, hot == Handle.Y ? HotColor : YColor);
		if (_mover.HasOwnHeight(target)) {
			DrawArrow(wireframe, camera, aspect, origin, WorldZ, length, hot == Handle.Z ? HotColor : ZColor);
		}

		var square = new List<Vector3>();
		var corners = PlaneCorners(origin, length);
		for (int i = 0; i < corners.Length; i++) {
			square.Add(corners[i]);
			square.Add(corners[(i + 1) % corners.Length]);
		}

		wireframe.DrawLines(camera, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(square),
			hot == Handle.Plane ? HotColor : PlaneColor, aspect);
	}

	/// <summary>
	/// Starts a drag when <paramref name="pointer"/> is on a handle of the selection, and says whether it did.
	/// <paramref name="detach"/> gives a dragged waypoint, group or area corner a point of its own rather than moving the one
	/// it stands on. <paramref name="carry"/> takes a structure group's members standing on points of their own along
	/// (<see cref="GroupTarget.Carry"/>).
	/// </summary>
	public bool TryBegin(Camera camera, Vector2 pointer, Vector2 viewport, bool detach, bool carry) {
		var (target, handle) = HandleUnder(camera, Movables, pointer, viewport);
		if (target == null || handle == Handle.None) {
			return false;
		}

		target = target switch {
			WaypointTarget waypoint when detach => waypoint with { Detach = true },
			GroupTarget group => group with { Detach = detach, Carry = carry },
			AreaCornerTarget corner when detach => corner with { Detach = true },
			_ => target
		};

		_origin = _mover.HandleOrigin(target);
		var ray = Ray(camera, pointer, viewport);
		if (handle is Handle.Plane or Handle.Radius) {
			if (OnPlane(ray, _origin.Y) is not { } grab) {
				return false;
			}

			_grab = grab;
		} else if (Along(ray, _origin, Axis(handle)) is { } along) {
			_grabAlong = along;
		} else {
			return false;
		}

		_dragging = handle;
		_target = target;
		_start = _mover.PositionOf(target);
		_current = _start;
		return true;
	}

	/// <summary>Moves the dragged target under <paramref name="pointer"/>, for the eye alone.</summary>
	public void Drag(Camera camera, Vector2 pointer, Vector2 viewport) {
		if (_target == null) {
			return;
		}

		var ray = Ray(camera, pointer, viewport);
		Vector3 delta;
		if (_dragging is Handle.Plane or Handle.Radius) {
			if (OnPlane(ray, _origin.Y) is not { } at) {
				return;
			}

			delta = at - _grab;
		} else if (Along(ray, _origin, Axis(_dragging)) is { } along) {
			delta = Axis(_dragging) * (along - _grabAlong);
		} else {
			return;
		}

		// Back from render space to world units: render (x, y, z) is world (x, -z, y) over WorldUnitsPerMeter.
		int x = _start.X + (int)MathF.Round(delta.X * WorldScale.WorldUnitsPerMeter);
		int y = _start.Y + (int)MathF.Round(-delta.Z * WorldScale.WorldUnitsPerMeter);
		int z = _start.Z + (int)MathF.Round(delta.Y * WorldScale.WorldUnitsPerMeter);
		_current = _mover.Standing(_target, x, y, z);
		_mover.Preview(_target, _current);
	}

	/// <summary>Ends the drag, committing the move when it went anywhere, and says whether it did.</summary>
	public bool End() {
		bool moved = _target != null && _current != _start;
		if (moved) {
			_mover.Commit(_target!, _start, _current.X, _current.Y, _current.Z);
		} else if (_target != null) {
			_mover.Cancel(_target, _start);
		}

		_dragging = Handle.None;
		_target = null;
		return moved;
	}

	// The first of <targets> with a handle under the pointer, and that handle; (null, None) when none has.
	private (MoveTarget? Target, Handle Handle) HandleUnder(Camera camera, IReadOnlyList<MoveTarget> targets, Vector2 pointer,
			Vector2 viewport) {
		foreach (var target in targets) {
			var handle = HandleAt(camera, target, pointer, viewport);
			if (handle != Handle.None) {
				return (target, handle);
			}
		}

		return (null, Handle.None);
	}

	private Handle HandleAt(Camera camera, MoveTarget target, Vector2 pointer, Vector2 viewport) {
		var origin = _mover.HandleOrigin(target);
		float length = Length(camera, origin);
		var project = new Projection(camera, viewport);
		if (target is AreaRadiusTarget) {
			// The diamond, picked in screen space within its own size or the grab distance, whichever is larger.
			if (project.ToScreen(origin) is not { } centre || project.ToScreen(origin + WorldX * length * DiamondSize) is not { } tip) {
				return Handle.None;
			}

			float reach = MathF.Max(Vector2.Distance(centre, tip), GrabPixels);
			return Vector2.Distance(pointer, centre) <= reach ? Handle.Radius : Handle.None;
		}

		// The square first: it sits between the arrows, where a near miss on either would otherwise take it.
		if (OnPlane(Ray(camera, pointer, viewport), origin.Y) is { } onPlane) {
			float alongX = Vector3.Dot(onPlane - origin, WorldX);
			float alongY = Vector3.Dot(onPlane - origin, WorldY);
			if (alongX >= length * PlaneNear && alongX <= length * PlaneFar
					&& alongY >= length * PlaneNear && alongY <= length * PlaneFar) {
				return Handle.Plane;
			}
		}

		var best = Handle.None;
		float bestDistance = GrabPixels;
		foreach (var handle in new[] { Handle.X, Handle.Y, Handle.Z }) {
			if (handle == Handle.Z && !_mover.HasOwnHeight(target)) {
				continue;
			}

			if (project.ToScreen(origin) is not { } from || project.ToScreen(origin + Axis(handle) * length) is not { } to) {
				continue;
			}

			float distance = DistanceToSegment(pointer, from, to);
			if (distance < bestDistance) {
				bestDistance = distance;
				best = handle;
			}
		}

		return best;
	}

	private static Vector3 Axis(Handle handle) => handle switch {
		Handle.X => WorldX,
		Handle.Y => WorldY,
		_ => WorldZ
	};

	private static float Length(Camera camera, Vector3 origin) =>
		Vector3.Distance(WorldScale.ToRender(camera.Position), origin) * LengthPerDistance;

	private static Vector3[] PlaneCorners(Vector3 origin, float length) => new[] {
		origin + (WorldX * PlaneNear + WorldY * PlaneNear) * length,
		origin + (WorldX * PlaneFar + WorldY * PlaneNear) * length,
		origin + (WorldX * PlaneFar + WorldY * PlaneFar) * length,
		origin + (WorldX * PlaneNear + WorldY * PlaneFar) * length,
	};

	// A diamond in the ground plane about <origin>.
	private static void DrawDiamond(WireframeRenderer wireframe, Camera camera, float aspect, Vector3 origin, float length,
			Vector3 color) {
		float size = length * DiamondSize;
		Vector3 east = origin + WorldX * size, north = origin + WorldY * size, west = origin - WorldX * size, south = origin - WorldY * size;
		Span<Vector3> lines = stackalloc Vector3[] { east, north, north, west, west, south, south, east };
		wireframe.DrawLines(camera, lines, color, aspect);
	}

	private static void DrawArrow(WireframeRenderer wireframe, Camera camera, float aspect, Vector3 origin,
			Vector3 axis, float length, Vector3 color) {
		var tip = origin + axis * length;

		// The head's two strokes lie in the plane of the axis and whichever other axis is least along the view, so the
		// head reads from any side.
		var side = MathF.Abs(axis.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
		var back = tip - axis * length * 0.15f;
		Span<Vector3> lines = stackalloc Vector3[] {
			origin, tip,
			tip, back + side * length * 0.06f,
			tip, back - side * length * 0.06f,
		};
		wireframe.DrawLines(camera, lines, color, aspect);
	}

	private static (Vector3 Origin, Vector3 Direction) Ray(Camera camera, Vector2 pointer, Vector2 viewport) {
		float ndcX = pointer.X / viewport.X * 2f - 1f;
		float ndcY = 1f - pointer.Y / viewport.Y * 2f;
		return camera.ViewportPointToRay(new Vector2(ndcX, ndcY), viewport.X / MathF.Max(viewport.Y, 1f));
	}

	/// <summary>Where a ray meets the level plane at render height <paramref name="height"/>, or null when it never does.</summary>
	private static Vector3? OnPlane((Vector3 Origin, Vector3 Direction) ray, float height) {
		if (MathF.Abs(ray.Direction.Y) < 1e-5f) {
			return null;
		}

		float t = (height - ray.Origin.Y) / ray.Direction.Y;
		return t > 0f ? ray.Origin + ray.Direction * t : null;
	}

	/// <summary>
	/// How far along the line through <paramref name="origin"/> in <paramref name="axis"/> the point nearest the ray
	/// lies, or null when the ray runs along the line and every point is as near.
	/// </summary>
	private static float? Along((Vector3 Origin, Vector3 Direction) ray, Vector3 origin, Vector3 axis) {
		var offset = origin - ray.Origin;
		float b = Vector3.Dot(axis, ray.Direction);
		float denominator = 1f - b * b;
		if (denominator < 1e-4f) {
			return null;
		}

		float d = Vector3.Dot(axis, offset);
		float e = Vector3.Dot(ray.Direction, offset);
		return (b * e - d) / denominator;
	}

	private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to) {
		var segment = to - from;
		float lengthSquared = segment.LengthSquared();
		float t = lengthSquared > 0f ? Math.Clamp(Vector2.Dot(point - from, segment) / lengthSquared, 0f, 1f) : 0f;
		return Vector2.Distance(point, from + segment * t);
	}

	/// <summary>Render space to window pixels through one camera, over the viewport the pointer reports in.</summary>
	private readonly struct Projection {
		private readonly Matrix4x4 _viewProjection;
		private readonly Vector2 _viewport;

		public Projection(Camera camera, Vector2 viewport) {
			_viewport = viewport;
			_viewProjection = camera.ViewMatrix * camera.ProjectionMatrix(viewport.X / MathF.Max(viewport.Y, 1f));
		}

		public Vector2? ToScreen(Vector3 render) {
			var clip = Vector4.Transform(new Vector4(render, 1f), _viewProjection);
			if (clip.W <= 0f) {
				return null;
			}

			return new Vector2((clip.X / clip.W * 0.5f + 0.5f) * _viewport.X, (0.5f - clip.Y / clip.W * 0.5f) * _viewport.Y);
		}
	}
}
