using HercWorks.Core.Data.File.Cfg;
using System.Numerics;
using Herculan.Engine.Content;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Scene;

/// <summary>
/// The draw items that last the whole mission: the terrain, and every placed machine, structure and flyer,
/// built once from <see cref="SceneUploads"/> and kept. Each frame moves them, poses them, and picks which of
/// their pieces show — a level of detail, a damage cell, a wreck, a unit still waiting to deploy.
/// </summary>
public sealed class WorldDrawItems {
	private readonly MissionScene _scene;
	private readonly SceneUploads _uploads;

	// Everything that can move needs its transform refreshed every frame. Structures never do, so they
	// stay on the one built here.
	private readonly List<(SceneObject Object, SceneItem Item)> _movers = new();

	// The structures that can change what they are drawn as: one that leaves a wreck swaps its geometry
	// for the hulk the moment its last part collapses, and one that leaves nothing drops out of the
	// world. Neither moves otherwise, so they are not in _movers.
	//
	// A structure is drawn a piece at a time -- one per cell, or one per node and cell for a type that
	// animates -- so the swap covers a set of items and puts a wreck item of its own in their place
	// rather than overwriting one item's mesh. The wreck is drawn a cell at a time too, for its detail
	// levels' sake, so it is a set as well.
	private readonly List<(SceneObject Object, BaseObject Structure, SceneItem[] Items,
		(SceneItem Item, CellGate Gate)[] Hulk, bool Posed)> _wreckable = new();

	// Every drawn piece that stands on one cell of one animation sequence, and so is on screen only
	// while its object's ShapeCellFrames says that cell is showing. This is how a machine's destroyed
	// limb comes off and a structure's collapsed part turns to rubble: the shape holds all its cells
	// built and uploaded, and damage moves which one is picked -- see DtsMeshBuilder.BuildCells.
	private readonly List<(SimObject Object, CellGate Gate, SceneItem Item)> _gatedParts = new();

	// The objects the mission has not deployed yet, with the items that draw them and the visibility
	// each was built with. Entries leave the list the frame their group arrives.
	private readonly List<(SimObject Object, (SceneItem Item, bool Visible)[] Parts)> _undeployed;

	// Every node piece of an animating object, refreshed from its node's pose each frame.
	private readonly List<(SimObject Subject, int TransformId, SceneItem Item)> _posedParts = new();

	// The machines on the field, each with every LOD root of its shape built and uploaded and one of
	// them selected. Only a machine is here: nothing else in the original carries a detail table, so
	// nothing else changes which shape it is drawn as — see Render.ShapeDetail.
	private readonly List<MechDetailChain> _detailChains = new();

	// Each machine's TSBSPPart groups, per LOD root, and the chain that says which root is drawn. A
	// fitted weapon is painted in the turn of the hardpoint slot it is spliced into, which is a child
	// of the drawn root's part -- see HardpointSlot and Render.BspDrawGroup.
	private readonly Dictionary<SimObject, List<BspDrawGroup>[]> _machineBspGroups = new();
	private readonly Dictionary<SimObject, MechDetailChain> _machineDetailChains = new();

	// Every drawn piece that is one level of a TSDetailPart, and so is on screen only while that part's
	// projected size selects its level -- TSDetailPart_Render's choice, made per object per frame by
	// SelectDetailLevels. Only what is built by cell or by node is here: a structure, its wreck and a
	// flyer, which are drawn under STRUCTURE DETAIL's bias. Guns, launcher rounds and debris carry detail
	// parts too, and choose their levels as their items are rebuilt each frame -- see TransientDrawItems.
	private readonly List<(SimObject Object, CellGate Gate, SceneItem Item)> _detailLevelParts = new();

	// SelectDetailLevels' per-frame scratch: the level each object's detail part chose this frame.
	private readonly Dictionary<(SimObject, PartDetail), int> _detailLevelChoice = new();

	public WorldDrawItems(MissionScene scene, SceneUploads uploads, DrawFiling filing, uint? terrainTexture) {
		_scene = scene;
		_uploads = uploads;

		// The terrain's own draw item, kept because its texture follows the TERRAIN TEXTURE preference and so
		// changes after the item list is built -- see the per-frame update.
		Terrain = new SceneItem(uploads.TerrainMesh, Matrix4x4.Identity, terrainTexture) {
			CellQuantisedFog = true,
			GroundFill = true,
		};

		// The terrain again, with the ground shapes painted in its cell order -- see SceneRenderer.Render.
		// Its shape list is rebuilt each frame by TransientDrawItems.
		GroundLayer = new GroundShapeLayer(Terrain, scene.World.Terrain);
		var built = new List<SceneItem> { Terrain };

		// The player's own machine, kept aside so the cockpit view can leave it out — see below. A
		// segmented machine contributes one item per node, so this is a set rather than one item.
		var playerItems = new HashSet<SceneItem>();

		foreach (var sceneObject in scene.Objects) {
			if (sceneObject.Model is { } model) {
				Build(sceneObject, model, built, playerItems);
			}
		}

		// A unit whose group is still waiting on its arrival action is not in the mission, and
		// Scene_SubmitFrameObjects does not submit it -- but it does submit it the moment the group
		// arrives, so this is a per-frame filter and not a build-time one. Its geometry is built like
		// everything else's and hidden until then; skipping the build instead leaves an arrived machine
		// with no body at all, and only its weapons, which are rebuilt every frame, on screen.
		//
		// The built-time visibility is captured rather than assumed: a wreckable structure's hulk item is
		// built hidden, and un-hiding it on arrival would show every waiting building as its own rubble.
		_undeployed = built
			.Where(entry => entry.LightSubject is { AwaitingDeployment: true })
			.GroupBy(entry => entry.LightSubject!)
			.Select(group => (group.Key, group.Select(entry => (entry, entry.Visible)).ToArray()))
			.ToList();

		foreach (var (_, parts) in _undeployed) {
			foreach (var (part, _) in parts) {
				part.Visible = false;
			}
		}

		All = built.ToArray();

		// Every item that draws a simulation object is filed with it. The terrain has no subject and is
		// not filed.
		foreach (var item in built) {
			if (item.LightSubject is { } subject) {
				item.Filing = filing.ObjectEntry(subject);
			}
		}

		// Piloting means sitting inside the machine, and its own geometry is all around the eye — the
		// cockpit node the camera rides is well inside the torso, so drawing it fills the canopy and
		// hides the world. Both lists share the same SceneItem objects, so the per-frame transform
		// refresh reaches whichever one is being drawn.
		Piloted = playerItems.Count > 0
			? built.Where(entry => !playerItems.Contains(entry)).ToArray()
			: All;
	}

	public SceneItem Terrain { get; }

	public GroundShapeLayer GroundLayer { get; }

	/// <summary>Every kept item.</summary>
	public SceneItem[] All { get; }

	/// <summary>Every kept item but the player's own machine's.</summary>
	public SceneItem[] Piloted { get; }

	/// <summary>
	/// The frame's moves and choices, for the camera about to draw it: transforms, which root of each machine and
	/// which level of every detail part is drawn, the pose of every node, and which pieces show.
	/// </summary>
	public void Refresh(Camera camera, int focalPixels, SimulatorPreferences preferences) {
		foreach (var (sceneObject, item) in _movers) {
			item.Transform = MissionScene.TransformOf(sceneObject);
		}

		// Each node of an animating machine is re-read here, alongside the whole-object transforms above.
		// Reading more often than the simulation ticks costs nothing and gains nothing: the thread's
		// intra-frame fraction only moves in Advance, so consecutive reads between ticks return the same
		// pose. That is the original's cadence too — see docs/retail/formats/dts-node-posing.md's "Evaluation cadence".
		// Which root of each machine's shape is drawn, and which level of every detail part, settled before
		// the two loops that follow so that a piece taken up this frame is posed and gated this frame
		// rather than one frame stale.
		SelectDetailRoots(camera, focalPixels, preferences);
		SelectDetailLevels(camera, focalPixels, preferences);

		foreach (var (subject, transformId, item) in _posedParts) {
			if (!item.DetailSelected) {
				continue;
			}

			item.Transform = MissionScene.PosedTransformOf(subject, transformId);
		}

		// The arrival gate, run before the sequence gate below so that a part which is both waiting and
		// gated is answered by the gate once its group is in the mission. An entry is dropped the frame
		// it arrives -- a group deploys once and never goes back.
		for (int i = _undeployed.Count - 1; i >= 0; i--) {
			var (owner, parts) = _undeployed[i];
			if (owner.AwaitingDeployment) {
				continue;
			}

			foreach (var (part, visible) in parts) {
				part.Visible = visible;
			}

			_undeployed.RemoveAt(i);
		}

		// Which cell of each animation sequence is on screen, read straight off the object the way
		// TSCellAnimPart_Render reads shapeInstance+8. Every piece the shape holds is already uploaded,
		// so a destroyed component or a collapsed structure part costs a flag rather than a rebuild.
		foreach (var (owner, gate, item) in _gatedParts) {
			if (!item.DetailSelected) {
				continue;
			}

			item.Visible = !owner.AwaitingDeployment && gate.VisibleIn(owner.CellFrames);
		}

		RefreshWreckItems();
	}

	/// <summary>
	/// The child of the machine's drawn root that a mount's shape is spliced into, so the walk paints the gun in
	/// that child's turn: Mech_SpliceHardpointShapes (004030d0) writes it over the slot part, and
	/// TSBSPPart_RenderNode (00476a1c) reaches it there. A slot the walk never reaches leaves its gun undrawn, as
	/// the original does. Null for a root whose part has no such child.
	/// </summary>
	public (BspDrawGroup Group, int Leaf)? HardpointSlot(MechObject mech, WeaponMount mount) {
		if (!_machineBspGroups.TryGetValue(mech, out var roots)) {
			return null;
		}

		int root = _machineDetailChains.TryGetValue(mech, out var chain) ? chain.Active : 0;
		if (root < 0 || root >= roots.Length || roots[root] is not { } groups) {
			return null;
		}

		foreach (var group in groups) {
			if (group.Tree.TryGetLeafOfPart(mount.HardpointBoneId, out int leaf)) {
				return (group, leaf);
			}
		}

		return null;
	}

	private void Build(SceneObject sceneObject, SceneModel model, List<SceneItem> built, HashSet<SceneItem> playerItems) {
		uint? texture = _uploads.TextureOf(model.Key);
		bool isPlayer = ReferenceEquals(sceneObject, _scene.PlayerObject);

		// A machine is uploaded once per LOD root and drawn as whichever one its projected size on
		// screen selects, so the build below runs once per root and leaves all but root 0 deselected.
		// Everything else has one root and the loop runs once — see Render.ShapeDetail, and
		// SelectDetailRoots for the per-frame half.
		var detailRoots = sceneObject.Detail?.Roots ?? new[] { model };

		if (_uploads.Segments.ContainsKey(model.Key)) {
			var subject = sceneObject.Object;
			var rootItems = new SceneItem[detailRoots.Count][];
			var rootBspGroups = new List<BspDrawGroup>[detailRoots.Count];

			for (int root = 0; root < detailRoots.Count; root++) {
				var rootModel = detailRoots[root];
				if (!_uploads.Segments.TryGetValue(rootModel.Key, out var segments)) {
					rootItems[root] = Array.Empty<SceneItem>();
					continue;
				}

				uint? rootTexture = _uploads.TextureOf(rootModel.Key);
				var segmentItems = new SceneItem[segments.Length];

				// A posed segment's node planes are in the nodes' posed frames.
				var rootGroups = new Dictionary<BspTree, BspDrawGroup>();

				for (int i = 0; i < segments.Length; i++) {
					var segment = rootModel.Segments[i];
					var part = new SceneItem(segments[i],
						MissionScene.PosedTransformOf(subject, segment.TransformId), rootTexture) {
						LightSubject = subject,
						DetailSelected = root == 0
					};

					if (segment.Leaf is { } leaf) {
						if (!rootGroups.TryGetValue(leaf.Tree, out var group)) {
							rootGroups[leaf.Tree] = group = new BspDrawGroup(leaf.Tree,
								frame => MissionScene.PosedTransformOf(subject, frame));
						}

						part.BspGroup = group;
						part.BspLeaf = leaf.Index;
					}

					segmentItems[i] = part;
					built.Add(part);
					_posedParts.Add((subject, segment.TransformId, part));
					if (segment.Gate.IsGated) {
						_gatedParts.Add((subject, segment.Gate, part));
					}

					if (segment.Gate.IsDetailGated) {
						_detailLevelParts.Add((subject, segment.Gate, part));
					}

					if (isPlayer) {
						playerItems.Add(part);
					}
				}

				rootItems[root] = segmentItems;
				rootBspGroups[root] = rootGroups.Values.ToList();
			}

			if (subject is MechObject) {
				_machineBspGroups[subject] = rootBspGroups;
			}

			if (sceneObject.Detail is { } chain && rootItems.Length > 1) {
				var detailChain = new MechDetailChain(subject, chain.ShapeRadius, rootItems);
				_detailChains.Add(detailChain);
				_machineDetailChains[subject] = detailChain;
			}

			// Nothing here goes in _movers: the posed refresh carries the object's own frame in
			// front of each node's, so a segmented object that moves is followed by that alone.
			//
			// An animated structure still leaves a wreck like any other, and every one of the eight
			// types that reach here does have a hulk. A structure has the one root, so the wreck swap
			// covers rootItems[0] and there is nothing else for it to cover.
			if (subject is BaseObject segmentedStructure) {
				RegisterWreckable(sceneObject, segmentedStructure, rootItems[0], built, posed: true);
			}

			return;
		}

		// A shape split by cell contributes one item per cell instead of one for the whole model,
		// with the object's own transform on every one of them: unlike a machine's segments these
		// are already placed, so only the gate differs between them.
		if (_uploads.Cells.TryGetValue(model.Key, out var cells)) {
			var cellItems = new SceneItem[cells.Length];
			var cellGroups = new Dictionary<BspTree, BspDrawGroup>();
			for (int i = 0; i < cells.Length; i++) {
				var cell = model.Cells[i];
				var part = new SceneItem(cells[i], MissionScene.TransformOf(sceneObject), texture) {
					LightSubject = sceneObject.Object
				};
				JoinBspGroup(part, cell.Leaf, cellGroups, sceneObject);

				cellItems[i] = part;
				built.Add(part);
				if (cell.Gate.IsGated) {
					_gatedParts.Add((sceneObject.Object, cell.Gate, part));
				}

				if (cell.Gate.IsDetailGated) {
					_detailLevelParts.Add((sceneObject.Object, cell.Gate, part));
				}

				// A flyer flies, and every cell of it rides the one object transform, so all of them
				// need refreshing each frame. So does a ground vehicle, the one structure class that
				// drives; every other structure stands still and keeps the transform built here.
				if (sceneObject.Object is FlyerObject
						or BaseObject { Class: BaseObject.StructureClass.GroundVehicle }) {
					_movers.Add((sceneObject, part));
				}

				if (isPlayer) {
					playerItems.Add(part);
				}
			}

			if (sceneObject.Object is BaseObject celledStructure) {
				RegisterWreckable(sceneObject, celledStructure, cellItems, built);
			}

			return;
		}

		if (!_uploads.Meshes.TryGetValue(model.Key, out var mesh)) {
			return;
		}

		var item = new SceneItem(mesh, MissionScene.TransformOf(sceneObject), texture) {
			LightSubject = sceneObject.Object
		};
		built.Add(item);

		if (isPlayer) {
			playerItems.Add(item);
		}

		if (sceneObject.Object is BaseObject wreckableStructure) {
			RegisterWreckable(sceneObject, wreckableStructure, new[] { item }, built);
		}

		// Anything that can move needs its transform refreshed every frame. Structures never do, so
		// they stay on the one built here.
		if (sceneObject.Object is MechObject) {
			_movers.Add((sceneObject, item));
		}
	}

	// The two ways a structure can stop being drawn as itself. A type that leaves a wreck gets a
	// second, hidden item carrying the hulk, so the swap is a visibility flip rather than a mesh
	// rebuild at the moment the last part falls; a one-part type that leaves nothing sinks instead
	// and needs only its transform refreshed. A type with neither does neither, and is not listed.
	private void RegisterWreckable(SceneObject sceneObject, BaseObject structure, SceneItem[] structureItems,
			List<SceneItem> built, bool posed = false) {
		if (structure.Type.HulkTypeIndex < 0 && structure.Type.Components.Length > 1) {
			return;
		}

		var hulkItems = Array.Empty<(SceneItem Item, CellGate Gate)>();
		if (structure.Type.HulkTypeIndex >= 0
				&& _scene.HulkModels.TryGetValue(structure.Type.HulkTypeIndex, out var hulk)
				&& _uploads.Cells.TryGetValue(hulk.Key, out var hulkCells)) {
			uint? hulkTexture = _uploads.TextureOf(hulk.Key);
			hulkItems = new (SceneItem, CellGate)[hulkCells.Length];
			var hulkGroups = new Dictionary<BspTree, BspDrawGroup>();
			for (int i = 0; i < hulkCells.Length; i++) {
				var gate = hulk.Cells[i].Gate;
				var hulkItem = new SceneItem(hulkCells[i], MissionScene.TransformOf(sceneObject), hulkTexture) {
					LightSubject = structure,
					Visible = false
				};
				JoinBspGroup(hulkItem, hulk.Cells[i].Leaf, hulkGroups, sceneObject);

				hulkItems[i] = (hulkItem, gate);
				built.Add(hulkItem);
				if (gate.IsDetailGated) {
					_detailLevelParts.Add((structure, gate, hulkItem));
				}
			}
		}

		_wreckable.Add((sceneObject, structure, structureItems, hulkItems, posed));
	}

	// A piece baked at the rest pose joins its object's group for the part it is a child of,
	// whose planes sit at the rest pose in front of the object's own frame.
	private static void JoinBspGroup(SceneItem item, BspLeaf? leaf, Dictionary<BspTree, BspDrawGroup> groups,
			SceneObject owner) {
		if (leaf is not { } child) {
			return;
		}

		if (!groups.TryGetValue(child.Tree, out var group)) {
			groups[child.Tree] = group = BspDrawGroup.AtRest(child.Tree,
				() => MissionScene.TransformOf(owner));
		}

		item.BspGroup = group;
		item.BspLeaf = child.Index;
	}

	// Which root of each machine's shape is drawn this frame -- Shape_DrawAtDetailLevel (004033e4),
	// ported in Render.ShapeDetail and run here because this is where the camera and the window size
	// both are. The original runs it inside the machine's own draw slot, once per machine per frame,
	// which is this cadence.
	//
	// HERC DETAIL is re-read every frame for the same reason TERRAIN TEXTURE is: the preferences
	// panel steps it over a scene that is still being drawn behind it, so the player watches the
	// machines coarsen as they step the row.
	private void SelectDetailRoots(Camera camera, int focalPixels, SimulatorPreferences preferences) {
		if (_detailChains.Count == 0) {
			return;
		}

		int bias = ShapeDetail.BiasFor(preferences[Prefs.HercDetailOption]);
		var eye = camera.Position;

		foreach (var chain in _detailChains) {
			// Eye to the object's origin, in world units -- Math_FastMagnitude3D of the view-space
			// translation the model transform installs, which is the same distance by a shorter route.
			var offset = chain.Subject.Position - eye;
			int distance = (int)Math.Min(
				Math.Sqrt((double)offset.X * offset.X + (double)offset.Y * offset.Y
					+ (double)offset.Z * offset.Z),
				int.MaxValue);

			int root = ShapeDetail.SelectRoot(chain.Roots.Length, chain.ShapeRadius, distance,
				focalPixels, bias);
			if (root == chain.Active) {
				continue;
			}

			foreach (var part in chain.Roots[chain.Active]) {
				part.DetailSelected = false;
			}

			foreach (var part in chain.Roots[root]) {
				part.DetailSelected = true;
			}

			chain.Active = root;
		}
	}

	// Which level of every detail part is drawn this frame -- TSDetailPart_Render (004768bc), ported in
	// Render.PartDetail. Everything on the list is drawn by Structure_DrawWithDetailBias (004034f4) or
	// Flyer_Draw (004215cc), and both push the bias STRUCTURE DETAIL selects, so one bias serves the
	// whole list. STRUCTURE DETAIL is re-read every frame, as HERC DETAIL is above.
	private void SelectDetailLevels(Camera camera, int focalPixels, SimulatorPreferences preferences) {
		if (_detailLevelParts.Count == 0) {
			return;
		}

		int bias = PartDetail.StructureBias(preferences[Prefs.StructureDetailOption]);
		var eye = WorldScale.ToRender(camera.Position);

		// Many pieces share one detail part -- every cell of every level -- so each part is measured once.
		_detailLevelChoice.Clear();
		foreach (var (owner, gate, item) in _detailLevelParts) {
			var detail = gate.Detail!;
			if (!_detailLevelChoice.TryGetValue((owner, detail), out int level)) {
				level = detail.Select(DetailMetrics.Distance(detail, WorldScale.ToRenderMatrix(owner.WorldFrame), eye),
					focalPixels, bias);
				_detailLevelChoice[(owner, detail)] = level;
			}

			item.DetailSelected = level == gate.Level;
		}
	}

	// The two things a collapsing structure does to what is on screen. A type that leaves a wreck is
	// redrawn as its BHULKS.DGS root the moment its last part falls -- the original writes that shape
	// straight onto the object's model instance, which here is the building's own items going dark and
	// the wreck item built beside them coming up. A type that leaves nothing is dropped a hundred
	// thousand units under the terrain, which is the original's own way of making a small structure
	// disappear, so its transform has to be refreshed once for it to go.
	//
	// This runs after the per-frame cell-gate pass, and overrides it: once the whole building is a wreck,
	// which of its parts were still standing stops meaning anything.
	private void RefreshWreckItems() {
		foreach (var (sceneObject, structure, structureItems, hulkItems, posed) in _wreckable) {
			if (structure.Sunk) {
				// A posed structure's items are in node space and the posed refresh above has already
				// put the sunk position on every one of them; writing the object transform over them
				// would stack each node's geometry at the shape's origin.
				if (!posed) {
					var sunk = MissionScene.TransformOf(sceneObject);
					foreach (var item in structureItems) {
						item.Transform = sunk;
					}
				}

				continue;
			}

			if (!structure.ShowingHulk || hulkItems.Length == 0) {
				continue;
			}

			foreach (var item in structureItems) {
				item.Visible = false;
			}

			// The swap replaces only the instance's shape, so the wreck's cell-animation parts read the
			// structure's own cell frames, frozen where the standing building left them -- see
			// docs/retail/simulation/destruction-effects.md, "A structure coming down".
			foreach (var (hulkItem, gate) in hulkItems) {
				hulkItem.Visible = gate.VisibleIn(structure.CellFrames);
			}
		}
	}
}
