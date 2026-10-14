using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;

namespace Herculan.Engine.Editor;

/// <summary>
/// The move handles drawn on the selected object: an arrow along world X (red) and world Y (green), a square in the
/// ground plane between them that moves it both ways at once, and for a flyer an arrow up world Z (blue) — the one
/// height the mission sets (<see cref="ObjectMover.HasOwnHeight"/>). Dragging one previews the move through
/// <see cref="ObjectMover.Preview"/> and commits it, as one undo step, on release. The handles keep one size on
/// screen however far away the object is, and draw through the terrain and other objects.
/// </summary>
internal sealed class MoveGizmo {
	private static readonly Vector3 XColor = new(0.95f, 0.3f, 0.3f);
	private static readonly Vector3 YColor = new(0.35f, 0.9f, 0.35f);
	private static readonly Vector3 ZColor = new(0.35f, 0.55f, 1f);
	private static readonly Vector3 PlaneColor = new(1f, 0.85f, 0.1f);
	private static readonly Vector3 HotColor = new(1f, 1f, 0.6f);

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

	private readonly SelectionState _selection;
	private readonly ObjectMover _mover;

	private Handle _dragging = Handle.None;
	private SceneObject? _target;
	private Vec3i _start;
	private Vector3 _origin;
	private Vector3 _grab;
	private float _grabAlong;
	private Vec3i _current;

	public MoveGizmo(SelectionState selection, ObjectMover mover) {
		_selection = selection;
		_mover = mover;
	}

	private enum Handle { None, X, Y, Z, Plane }

	/// <summary>Whether a drag is under way, which the viewport's click-to-select leaves alone.</summary>
	public bool Dragging => _dragging != Handle.None;

	/// <summary>The selected object, when it can be moved.</summary>
	private SceneObject? Movable =>
		_selection.Current is ObjectSelection { Object: var selected } && _mover.WhyFixed(selected) == null ? selected : null;

	public void Draw(WireframeRenderer wireframe, Camera camera, float aspect, Vector2 pointer, Vector2 viewport) {
		var target = _target ?? Movable;
		if (target == null) {
			return;
		}

		var origin = WorldScale.ToRender(target.Object.Position);
		float length = Length(camera, origin);
		var hot = Dragging ? _dragging : HandleAt(camera, target, pointer, viewport);

		DrawArrow(wireframe, camera, aspect, origin, WorldX, length, hot == Handle.X ? HotColor : XColor);
		DrawArrow(wireframe, camera, aspect, origin, WorldY, length, hot == Handle.Y ? HotColor : YColor);
		if (ObjectMover.HasOwnHeight(target)) {
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

	/// <summary>Starts a drag when <paramref name="pointer"/> is on a handle of the selected object, and says whether it did.</summary>
	public bool TryBegin(Camera camera, Vector2 pointer, Vector2 viewport) {
		if (Movable is not { } target) {
			return false;
		}

		var handle = HandleAt(camera, target, pointer, viewport);
		if (handle == Handle.None) {
			return false;
		}

		_origin = WorldScale.ToRender(target.Object.Position);
		var ray = Ray(camera, pointer, viewport);
		if (handle == Handle.Plane) {
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
		_start = target.Object.Position;
		_current = _start;
		return true;
	}

	/// <summary>Moves the dragged object under <paramref name="pointer"/>, for the eye alone.</summary>
	public void Drag(Camera camera, Vector2 pointer, Vector2 viewport) {
		if (_target == null) {
			return;
		}

		var ray = Ray(camera, pointer, viewport);
		Vector3 delta;
		if (_dragging == Handle.Plane) {
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

	/// <summary>Ends the drag, committing the move when it went anywhere.</summary>
	public void End() {
		if (_target != null && _current != _start) {
			_mover.Commit(_target, _start, _current.X, _current.Y, _current.Z);
		}

		_dragging = Handle.None;
		_target = null;
	}

	private Handle HandleAt(Camera camera, SceneObject target, Vector2 pointer, Vector2 viewport) {
		var origin = WorldScale.ToRender(target.Object.Position);
		float length = Length(camera, origin);
		var project = new Projection(camera, viewport);

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
			if (handle == Handle.Z && !ObjectMover.HasOwnHeight(target)) {
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
