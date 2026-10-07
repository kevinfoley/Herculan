using HercWorks.Core.Data.File.Cfg;
using System.Numerics;
using Herculan.Engine.Content;
using Herculan.Engine.Gl;
using Herculan.Engine.Numerics;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Scene;

/// <summary>
/// The draw items rebuilt every frame, because what they draw churns from tick to tick: rounds in flight, the
/// guns on every machine at their muzzle-flash cell, wreckage in the air, drop pods, the ground shapes near
/// the camera, and the billboards of shots, impacts and fires.
/// </summary>
public sealed class TransientDrawItems(MissionScene scene, SceneUploads uploads, DrawFiling filing, WorldDrawItems world) {
	// AddAtDetail's scratch, for the one transient shape it is making items for.
	private readonly Dictionary<PartDetail, int> _levelChoice = new();

	// And the BSP groups it makes for that shape's parts.
	private readonly Dictionary<BspTree, BspDrawGroup> _bspGroups = new();

	private Camera? _camera;
	private int _focalPixels;

	public List<SceneItem> Projectiles { get; } = new();

	/// <summary>
	/// The guns bolted to the machines on the field, rebuilt every frame for the same reason a rocket's is:
	/// which mesh a mount draws is its muzzle-flash cell, and that moves tick to tick.
	/// </summary>
	public List<SceneItem> Weapons { get; } = new();

	/// <summary>
	/// And the wreckage in the air. Same deal: a piece of debris tumbles every tick and the pool churns as pieces
	/// settle and burst, so the list is rebuilt each frame rather than kept.
	/// </summary>
	public List<SceneItem> Debris { get; } = new();

	public List<SceneItem> DropPods { get; } = new();

	/// <summary>
	/// The billboards to draw this frame: the EMP rounds in flight and every impact effect playing. Both churn
	/// from tick to tick, so the list is rebuilt each frame rather than kept — see SpriteRenderer.
	/// </summary>
	public List<SpriteBatch> Sprites { get; } = new();

	/// <summary>
	/// Every transient item. Shots in flight ride on the end of both kept lists: they are never the player's own
	/// machine, so nothing hides them.
	/// </summary>
	public IEnumerable<SceneItem> Items => Projectiles.Concat(Weapons).Concat(Debris).Concat(DropPods);

	/// <summary>This frame's items, for the camera about to draw them.</summary>
	public void Refresh(Camera camera, int focalPixels, SimulatorPreferences preferences) {
		_camera = camera;
		_focalPixels = focalPixels;
		RefreshProjectileItems();
		RefreshDebrisItems(preferences);
		RefreshDropPodItems();
		RefreshGroundShapeItems(camera);
		RefreshWeaponItems(preferences);
		RefreshSpriteBatches();
	}

	// One transient shape's items for this frame -- a gun on its mount, a launcher round or a piece of
	// wreckage, which are rebuilt every frame rather than kept. A shape with no TSDetailPart is its one
	// mesh; one with them is its ungated pieces plus the level of each detail part TSDetailPart_Render
	// (004768bc) selects under `bias`, chosen here as the item is made because that is when the
	// original chooses it.
	//
	// A piece built under a TSBSPPart joins a group for that part, made for this one draw, whose planes
	// sit at the rest pose in front of `transform`. With `slot` the whole shape is painted in the turn
	// of that child of another group -- a weapon in its machine's hardpoint slot.
	private void AddAtDetail(List<SceneItem> into, SceneModel model, Matrix4x4 transform, int bias,
			Func<GpuMesh, SceneItem> make, (BspDrawGroup Group, int Leaf)? slot = null) {
		if (!uploads.Cells.TryGetValue(model.Key, out var pieces)) {
			if (uploads.Meshes.TryGetValue(model.Key, out var mesh)) {
				into.Add(InSlot(make(mesh)));
			}

			return;
		}

		_bspGroups.Clear();

		var eye = WorldScale.ToRender(_camera!.Position);
		_levelChoice.Clear();

		for (int i = 0; i < pieces.Length; i++) {
			var gate = model.Cells[i].Gate;
			if (gate.Detail is { } detail) {
				if (!_levelChoice.TryGetValue(detail, out int level)) {
					level = detail.Select(DetailMetrics.Distance(detail, transform, eye), _focalPixels, bias);
					_levelChoice[detail] = level;
				}

				if (level != gate.Level) {
					continue;
				}
			}

			var item = make(pieces[i]);
			if (model.Cells[i].Leaf is { } leaf) {
				if (!_bspGroups.TryGetValue(leaf.Tree, out var group)) {
					_bspGroups[leaf.Tree] = group = BspDrawGroup.AtRest(leaf.Tree, () => transform,
						slot?.Group, slot?.Leaf ?? -1);
				}

				item.BspGroup = group;
				item.BspLeaf = leaf.Index;
			} else {
				InSlot(item);
			}

			into.Add(item);
		}

		SceneItem InSlot(SceneItem item) {
			if (slot is { } parent) {
				item.BspGroup = parent.Group;
				item.BspLeaf = parent.Leaf;
			}

			return item;
		}
	}

	// One item per drop pod, from whichever of the pod's two shapes it is showing: the plain root while
	// it falls, and the cell of its opening flipbook its own counter has reached once it is down. That
	// swap is Meteor_Render's own -- the original keeps the two as separate shape instances on the
	// object and draws one or the other.
	//
	// Filed by the falling root's radius whichever it shows: that is the instance the pod's vtable +0x10
	// reads.
	private void RefreshDropPodItems() {
		DropPods.Clear();

		foreach (var pod in scene.World.DropPods) {
			var model = pod.Landed
				? (pod.AnimationFrame < scene.DropPodOpeningModels.Count
					? scene.DropPodOpeningModels[pod.AnimationFrame]
					: null)
				: scene.DropPodModel;

			if (model == null || !uploads.Meshes.TryGetValue(model.Key, out var mesh)) {
				continue;
			}

			DropPods.Add(new SceneItem(mesh, WorldScale.ToRenderMatrix(pod.WorldTransform), uploads.TextureOf(model.Key)) {
				Filing = filing.FrameEntry(pod, ObjectTypeTag.DropPod, pod.Position, scene.DropPodModel?.ShapeRadius ?? 0),
			});
		}
	}

	// One entry per ground shape within range of the camera -- the FlatObj walk at the head of
	// Scene_SubmitFrameObjects (0042841c), which submits a shape only under GroundShape.DrawRange of
	// the view object, oldest first. Each is drawn by FlatObj_Draw (0040991c), whose first act is to sit
	// the shape on the terrain under it, so the conform runs here, on exactly the shapes being drawn --
	// after its position is taken for filing, which the original's submit reads before any draw.
	//
	// The original draws a type-9 object on the spot inside its terrain cell's pass, with the ramp's
	// row count zeroed (so a textured face is a plain palette copy, fullbright) and the fade that
	// cell's quad installed (so it fogs as the ground it lies on does). Those are Fullbright and
	// CellQuantisedFog; the cell order is the renderer's, through the GroundShapeLayer.
	private void RefreshGroundShapeItems(Camera camera) {
		var groundLayer = world.GroundLayer;
		groundLayer.Shapes.Clear();

		foreach (var shape in scene.World.GroundShapes) {
			if (shape.Position.ApproxDistanceTo(camera.Position) >= GroundShape.DrawRange
				|| shape.ShapeIndex < 0 || shape.ShapeIndex >= scene.GroundShapeModels.Count
				|| scene.GroundShapeModels[shape.ShapeIndex] is not { Count: > 0 } cells) {
				continue;
			}

			var submitted = shape.Position;
			shape.ConformToTerrain(scene.World.Terrain);

			var model = cells[shape.Frame % cells.Count];
			if (!uploads.Meshes.TryGetValue(model.Key, out var mesh)) {
				continue;
			}

			var item = new SceneItem(mesh, WorldScale.ToRenderMatrix(shape.WorldFrame), uploads.TextureOf(model.Key),
				fullbright: true) {
				CellQuantisedFog = true,
			};
			groundLayer.Shapes.Add(new GroundShapeDraw(item, submitted, shape.ShapeRadius));
		}
	}

	// One item per piece of wreckage in the air, from the shape file and root its own record names --
	// a debris table's .DTS for anything a destruction threw, and MECHWPN2.DTS for a gun knocked off its
	// hardpoint. The transform is the piece's own frame, so the tumble shows.
	//
	// Debris_Draw (00408e6c) pushes the piece's own detail bias: 0 for a table-thrown piece, HERC DETAIL's
	// for a gun -- see DebrisObject.HercDetailBias.
	private void RefreshDebrisItems(SimulatorPreferences preferences) {
		Debris.Clear();
		int hercBias = PartDetail.HercBias(preferences[Prefs.HercDetailOption]);

		foreach (var piece in scene.World.DebrisInFlight) {
			if (!scene.DebrisModels.TryGetValue(piece.ShapeLibrary, out var shapes)
				|| piece.ShapeIndex < 0 || piece.ShapeIndex >= shapes.Count
				|| shapes[piece.ShapeIndex] is not { } model) {
				continue;
			}

			uint? texture = uploads.TextureOf(model.Key);
			var transform = WorldScale.ToRenderMatrix(piece.WorldTransform);
			var entry = filing.FrameEntry(piece, ObjectTypeTag.Debris, piece.Position, model.ShapeRadius);
			AddAtDetail(Debris, model, transform, piece.HercDetailBias ? hercBias : 0,
				mesh => new SceneItem(mesh, transform, texture) { Filing = entry });
		}
	}

	// One item per fitted, visibly-mounted weapon on every machine in the scene: the model its template
	// names for the mounting code it sits at, at the cell its own flipbook has reached.
	//
	// The flipbook IS the muzzle flash — DBSIM spawns no separate effect for one. Cell zero is the gun
	// at rest; firing starts the book and WeaponMount walks it a cell a tick until it wraps. An ELF's
	// spin-up walks the same book before the first shot, which is why that weapon takes seven ticks to
	// answer its trigger.
	//
	// A gun is drawn with the machine it hangs off, and culled with it: Mech_Draw (004174c8) splices each
	// fitted weapon's shape into the machine's own before drawing it, so the guns are parts of the one
	// object the draw table files (docs/retail/formats/mech-shape-drawing.md). That includes the skip for the
	// object the camera rides, so from inside the cockpit the player's own guns are not drawn either.
	//
	// The mount's draw slot (WeaponMount_RenderWithDetailBias (0040ded8)) pushes HERC DETAIL's TSDetailPart bias around the render.
	private void RefreshWeaponItems(SimulatorPreferences preferences) {
		Weapons.Clear();
		int bias = PartDetail.HercBias(preferences[Prefs.HercDetailOption]);

		foreach (var sceneObject in scene.Objects) {
			if (sceneObject.Object is not MechObject mech || sceneObject.Object.AwaitingDeployment) {
				continue;
			}

			foreach (var mount in mech.Weapons.Mounts) {
				if (!scene.MechWeaponModels.TryGetValue(mount.ModelShapeIndex, out var cells)
					|| cells.Count == 0) {
					continue;
				}

				var model = cells[mount.FlashCell % cells.Count];
				uint? texture = uploads.TextureOf(model.Key);
				var transform = WorldScale.ToRenderMatrix(mount.ModelFrame(mech));

				// Lit as part of the machine it hangs off, which is how the original draws it: a mount's
				// shape is composed into the mech's own render entry, so it takes that entry's selection.
				AddAtDetail(Weapons, model, transform, bias,
					mesh => new SceneItem(mesh, transform, texture) {
						LightSubject = mech,
						Filing = filing.ObjectEntry(mech),
					}, world.HardpointSlot(mech, mount));
			}
		}
	}

	// One item per live projectile, from the shape its PROJ.DAT subtype names — see
	// MissionScene.BulletModels. The transform is the shot's own frame, which carries both where it is
	// and which way it is pointing, so a round is drawn nose-first along its flight.
	// Launcher rounds come out of their own table and their own shape file, and go in the same list:
	// both classes are drawn through the same vtable slot in the original.
	private void RefreshProjectileItems() {
		Projectiles.Clear();

		foreach (var projectile in scene.World.Projectiles) {
			if (scene.BulletModels.TryGetValue(projectile.SubtypeId, out var model)) {
				AddModel(model, projectile.Frame, ProjectileEntry(projectile));
			}
		}

		// A rocket's shape is a flipbook of geometry, not one mesh: its exhaust flame is a two-cell
		// TSCellAnimPart, and the cell is the round's own frame counter. Picking the mesh here is the
		// engine's equivalent of TSCellAnimPart_Render choosing one child.
		foreach (var rocket in scene.World.RocketsInFlight) {
			if (scene.RocketModels.TryGetValue(rocket.ShapeSubtypeId, out var cells) && cells.Count > 0) {
				var entry = filing.FrameEntry(rocket, ObjectTypeTag.Projectile, rocket.Position, cells[0].ShapeRadius);
				AddModel(cells[rocket.AnimationFrame % cells.Count], rocket.Frame, entry);
			}
		}

		void AddModel(SceneModel model, Transform3 frame, DrawEntry? entry) {
			uint? texture = uploads.TextureOf(model.Key);
			var transform = WorldScale.ToRenderMatrix(frame);

			// Fullbright, because Bullet_Draw — the vtable slot both classes are drawn through — zeroes
			// the ramp's row count around the shape render, which makes a round's textured polys a plain
			// palette copy with no light term. See SceneItem.Fullbright. The plasma round is the one
			// retail shape it shows on; every other projectile shape is untextured.
			//
			// Bias 0, because Bullet_Draw pushes none: a launcher round's detail parts are chosen by
			// projected size alone, whatever either detail setting says.
			AddAtDetail(Projectiles, model, transform, 0,
				mesh => new SceneItem(mesh, transform, texture, fullbright: true) { Filing = entry });
		}
	}

	// A bullet's entry, which its mesh and its billboards share. Null when its subtype has no shape.
	private DrawEntry? ProjectileEntry(Projectile projectile) =>
		scene.BulletModels.TryGetValue(projectile.SubtypeId, out var model)
			? filing.FrameEntry(projectile, ObjectTypeTag.Projectile, projectile.Position, model.ShapeRadius)
			: null;

	// The frame's billboards, from the two things that have any.
	//
	// A shot in flight draws its shape's flipbook at its own frame counter, with the shot's own frame as
	// the transform — so an EMP round's puff leans with the round, which is what the original measures
	// its rotation and its squash off (see SpriteRenderer). An impact effect draws its EXPLOS.DTS root
	// at wherever the shot landed, upright: the original never gives one a rotation.
	private void RefreshSpriteBatches() {
		Sprites.Clear();

		foreach (var projectile in scene.World.Projectiles) {
			if (scene.BulletModels.TryGetValue(projectile.SubtypeId, out var model)) {
				Add(model, WorldScale.ToRenderMatrix(projectile.Frame), projectile.AnimationFrame,
					ProjectileEntry(projectile));
			}
		}

		// Nothing for rockets here: ROCKETS.DTS holds no billboards at all, only geometry — see
		// SceneModelLibrary.Rocket. Their flipbook is drawn in RefreshProjectileItems.
		//
		// An effect on the hull of the machine the view camera rides is left out — see
		// ImpactEffect.HiddenFromOwnerCockpit. Which camera that is differs by pass (the missile camera
		// rides nothing), so the draw table answers it per pass: asked of the owner itself, the test says
		// whether a camera riding the owner would hide the effect.
		foreach (var effect in scene.World.Effects) {
			// Filed under its owner's cell when it has one (Explosion_GetOwnerDrawCell, 00408228), and by
			// its own position and radius otherwise.
			if (scene.ExplosionModels.TryGetValue(effect.ShapeIndex, out var model)) {
				var entry = filing.FrameEntry(effect,
					effect.ObjectClass != 0 ? ObjectTypeTag.EffectFar : ObjectTypeTag.Effect,
					effect.Position, model.ShapeRadius, effect.Owner);
				entry.HiddenWhenRidden = effect.HiddenFromOwnerCockpit(effect.Owner) ? effect.Owner : null;
				Add(model, Matrix4x4.CreateTranslation(WorldScale.ToRender(effect.Position)), effect.Frame,
					entry);
			}
		}

		// A fire is the third: the same kind of billboard flipbook an impact effect is, upright at
		// wherever its owner has carried it to, and looping rather than playing once. It is always filed
		// under its owner's cell (Fire_GetOwnerDrawCell, 0046b74c).
		foreach (var fire in scene.World.Fires) {
			if (fire.ShapeIndex >= 0 && fire.ShapeIndex < scene.FireModels.Count
				&& scene.FireModels[fire.ShapeIndex] is { } model) {
				var entry = filing.FrameEntry(fire, ObjectTypeTag.Fire, fire.Position, model.ShapeRadius, fire.Owner);
				Add(model, Matrix4x4.CreateTranslation(WorldScale.ToRender(fire.Position)), fire.Frame, entry);
			}
		}

		void Add(SceneModel model, Matrix4x4 transform, int frame, DrawEntry? entry) {
			if (model.Sprites.Length == 0 || model.Atlas == null
				|| !uploads.SpriteTextures.TryGetValue(model.Key, out var texture)) {
				return;
			}

			Sprites.Add(new SpriteBatch(model.Sprites, model.Atlas, texture.Handle, transform, frame,
				entry));
		}
	}
}
