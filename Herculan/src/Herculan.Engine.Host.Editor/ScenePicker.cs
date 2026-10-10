using System.Numerics;
using Herculan.Engine.Gl;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Terrain;

namespace Herculan.Engine.Host.Editor;

/// <summary>A placed, drawable object plus its cached render-space pick sphere.</summary>
internal sealed record Pickable(SceneObject SceneObject, Vector3 CenterRender, float RadiusRender);

/// <summary>
/// Turns a click in the viewport into a selection. Three kinds of target, tried in order: a
/// waypoint (a small mark with no volume, so picked in screen space), then an object (ray against
/// its bounding sphere), then the trigger area under the ground the ray reaches. That order puts
/// the smallest target first, so a waypoint sitting inside an area, beside a building, can still be
/// clicked.
/// </summary>
internal sealed class ScenePicker {
	private readonly Pickable[] _pickables;
	private readonly MissionOverlay _overlay;
	private readonly HeightGrid _terrain;
	private readonly MissionIndex _index;

	public ScenePicker(MissionScene scene, MissionIndex index, MissionOverlay overlay) {
		_index = index;
		_overlay = overlay;
		_terrain = scene.World.Terrain;
		_pickables = BuildPickables(scene);
	}

	/// <summary>
	/// Everything with a model, which is everything that can be drawn — a waiting group's members
	/// included, whether or not the settings draw them.
	/// </summary>
	public IReadOnlyList<Pickable> Pickables => _pickables;

	/// <summary>
	/// What a click at <paramref name="screen"/> (window pixels, +Y down) selects, or null for
	/// nothing.
	/// </summary>
	public EditorSelection? Pick(Camera camera, Vector2 screen, Vector2 viewport, EditorSettings settings) {
		if (_overlay.PickWaypoint(camera, screen, settings) is { } waypoint) {
			return waypoint;
		}

		float ndcX = screen.X / viewport.X * 2f - 1f;
		float ndcY = 1f - screen.Y / viewport.Y * 2f;
		float aspect = viewport.X / MathF.Max(viewport.Y, 1f);
		var (origin, direction) = camera.ViewportPointToRay(new Vector2(ndcX, ndcY), aspect);

		Pickable? best = null;
		float bestDistance = float.MaxValue;
		bool skipWaiting = settings.WaitingGroups == WaitingGroupDisplay.Hidden;
		foreach (var pickable in _pickables) {
			if (skipWaiting && _index.IsWaiting(pickable.SceneObject)) {
				continue;
			}

			if (RaySphere(origin, direction, pickable.CenterRender, pickable.RadiusRender, out float t) && t < bestDistance) {
				bestDistance = t;
				best = pickable;
			}
		}

		if (best != null) {
			return new ObjectSelection(best.SceneObject);
		}

		return GroundPick.Cast(origin, direction, _terrain, camera.FarPlane) is { } ground
			? _overlay.PickArea(ground, settings)
			: null;
	}

	/// <summary>
	/// Deliberately not <see cref="SceneModel.ShapeRadius"/>: that is the shape file's own radius
	/// about the model's origin, which sits near a mech's base, so a sphere of it there need not cover
	/// the torso, head or raised arms. Instead each model's own bounding sphere is computed from its
	/// mesh once (cached per model key, several objects share a model) and its centre transformed by
	/// the object's full rotation and translation — a sphere is rotation-invariant, so the local-space
	/// radius stays correct after that.
	/// </summary>
	private static Pickable[] BuildPickables(MissionScene scene) {
		var modelBounds = new Dictionary<string, (Vector3 LocalCenter, float Radius)>();
		return scene.Objects
			.Where(o => o.Model != null)
			.Select(o => {
				var model = o.Model!;
				if (!modelBounds.TryGetValue(model.Key, out var bounds)) {
					// The whole shape, a structure's ground plane included — see SceneModel.GroundMesh.
					bounds = ComputeBounds(model.GroundMesh is { } ground
						? model.Mesh.Concat(ground.Vertices).ToArray()
						: model.Mesh);
					modelBounds[model.Key] = bounds;
				}

				Vector3 worldCenter = Vector3.Transform(bounds.LocalCenter, MissionScene.TransformOf(o));
				return new Pickable(o, worldCenter, MathF.Max(bounds.Radius, 1f));
			})
			.ToArray();
	}

	private static (Vector3 Center, float Radius) ComputeBounds(MeshVertex[] mesh) {
		if (mesh.Length == 0) {
			return (Vector3.Zero, 1f);
		}

		Vector3 min = mesh[0].Position;
		Vector3 max = mesh[0].Position;
		foreach (var vertex in mesh) {
			min = Vector3.Min(min, vertex.Position);
			max = Vector3.Max(max, vertex.Position);
		}

		return ((min + max) * 0.5f, Vector3.Distance(min, max) * 0.5f);
	}

	private static bool RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float t) {
		Vector3 toCenter = origin - center;
		float b = Vector3.Dot(toCenter, direction);
		float c = Vector3.Dot(toCenter, toCenter) - radius * radius;
		float discriminant = b * b - c;
		if (discriminant < 0f) {
			t = 0f;
			return false;
		}

		float sqrtDiscriminant = MathF.Sqrt(discriminant);
		float near = -b - sqrtDiscriminant;
		t = near >= 0f ? near : -b + sqrtDiscriminant;
		return t >= 0f;
	}
}
