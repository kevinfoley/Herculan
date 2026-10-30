using System.Numerics;
using Herculan.Engine.Host.Debugging;
using Herculan.Engine.Render;
using Herculan.Engine.Scene;
using Herculan.Engine.Settings;
using Herculan.Engine.Sim;
using Herculan.Engine.Sim.Anim;
using Silk.NET.OpenGL;

namespace Herculan.Engine.Host.Simulator.Rendering;

/// <summary>
/// One pass of the world into a viewport: the scene with its beams and billboards (<see cref="WorldDrawPass"/>), then the
/// debug skeleton over it. Every view the simulator draws — the cockpit's panels, the heads-down window, the external
/// view, the observer camera and the MFD's missile camera — is one or more of these. Each pass reads
/// <see cref="TweakSettingDefinitions.PerPixelDepthSorting"/> as it starts, so the tweak takes effect on the next frame.
/// </summary>
sealed class WorldPassRenderer : IDisposable {
	private readonly MissionScene _scene;
	private readonly DebugOptions _debugOptions;
	private readonly MechObject? _pilotMech;
	private readonly WireframeRenderer _wireframe;
	private readonly BeamRenderer? _beams;
	private readonly SpriteRenderer _sprites;
	private readonly WorldDrawPass _draw;
	private readonly WorldDrawItems _world;
	private readonly TransientDrawItems _transient;

	public WorldPassRenderer(GL gl, MissionScene scene, Camera camera, DebugOptions debugOptions,
			WorldDrawItems world, TransientDrawItems transient) {
		_scene = scene;
		_debugOptions = debugOptions;
		_pilotMech = scene.PlayerMech;
		_world = world;
		_transient = transient;
		Scene = new SceneRenderer(gl);

		// How far this zone is visible and what it fades into, both off the zone and its theater rather
		// than hand-picked — see Scene.Atmosphere. The sky is deliberately left alone.
		scene.Models.Atmosphere.ApplyTo(Scene);

		// The lights impact effects claim, which the renderer selects out of per drawn object — see
		// EffectLightSelection. Live slots only ever come from the simulation, so this is the whole of
		// the wiring.
		Scene.EffectLights = scene.World.Effects.Lights;

		// And the same distance as the camera's far plane, so the view stops where the original's
		// terrain draw region does instead of drawing fully-fogged geometry past it.
		scene.Models.Atmosphere.ApplyTo(camera);

		// The theater's shaded-surface colours — what a TSShadedPoly is actually drawn through. See
		// SurfaceRampTable.
		Scene.SetShadeRamps(scene.Models.ShadeRamps);
		Scene.SetPaletteRamp(scene.Models.PaletteRamp);

		// And the same two through the theater's damage-flash palette, which the cockpit shake swaps the
		// scene to for a fraction of a second at a time — see Scene.ImpactFlash. After the two above,
		// which this is measured against.
		Scene.SetImpactRamps(scene.Models.ImpactFlash?.ShadeRamps, scene.Models.ImpactFlash?.PaletteRamp);
		Scene.DistanceBands = scene.Models.DistanceBands;

		_wireframe = new WireframeRenderer(gl);

		// Beams draw only if their two resources loaded; a scene without them still fires and still
		// damages, it just shows nothing.
		_beams = scene.Beams != null ? new BeamRenderer(gl, scene.Beams) : null;

		// Billboards need no resource of their own — every sprite they draw belongs to a model already
		// built, so this is unconditional where the beam renderer is not.
		_sprites = new SpriteRenderer(gl);
		_draw = new WorldDrawPass(gl, Scene, _sprites, _beams);
	}

	public SceneRenderer Scene { get; }

	/// <summary>
	/// Whether this frame's passes leave the player's own machine out: while looking out of its cockpit, the
	/// cockpit node the eye rides sits well inside the torso, so its geometry would wrap the camera and fill the
	/// canopy. The draw table's skip for the object the camera rides leaves it out of the same passes, as the
	/// original's does (ObjectDrawTable). The observer camera and the external view both put it back, which is
	/// the only way to see the machine you are flying.
	/// </summary>
	public bool LeavePlayerOut { get; set; }

	/// <summary>Whether any beam renderer exists, which is what <c>--fire</c>'s capture waits on.</summary>
	public bool DrawsBeams => _beams != null;

	/// <summary>What this frame's cameras draw: the kept items, less the player's while <see cref="LeavePlayerOut"/>, and every transient one.</summary>
	public IEnumerable<SceneItem> VisibleItems() =>
		(LeavePlayerOut ? _world.Piloted : _world.All).Concat(_transient.Items);

	/// <summary>The world into one viewport, from <paramref name="view"/>.</summary>
	public void Draw(Camera view, int x, int y, int width, int height) {
		DrawWorld(view, VisibleItems(), _world.GroundLayer, x, y, width, height);
		DrawSkeleton(view, width, height);
	}

	/// <summary>
	/// The MFD's MISSILE CAM, from inside the overlay's pass over the forward panel: the world as
	/// MfdMissileViewScreen_Paint draws it, from the camera the screen placed, into the screen's own rect. It is
	/// the cockpit view's draw again with four things changed, all of them the paint's own: no sky — the overlay
	/// has already flooded the rect with the colour the paint floods it with — the terrain untextured whatever
	/// TERRAIN TEXTURE says, every object submitted, the machine being flown among them and the ground shapes
	/// within range of this camera, since the view object is the camera and not the cockpit, and every level
	/// of detail picked by size on this screen from this camera. The cockpit's items are put back after it.
	/// </summary>
	public void DrawMissileView(GL gl, int x, int y, int width, int height, Camera view, Camera cockpitCamera) {
		view.FarPlane = cockpitCamera.FarPlane;

		// The world's picks first: a gun's rebuilt item is painted in the slot of its machine's drawn root.
		int focalPixels = DetailMetrics.FocalPixels(view, height);
		_world.SelectDetail(view, focalPixels);
		_transient.RefreshForView(view, focalPixels);

		gl.Enable(EnableCap.ScissorTest);
		gl.Scissor(x, y, (uint)width, (uint)height);
		gl.Clear(ClearBufferMask.DepthBufferBit);
		gl.Enable(EnableCap.DepthTest);
		gl.Disable(EnableCap.Blend);

		var sky = Scene.Sky;
		Scene.Sky = null;
		var terrainItem = _world.Terrain;
		uint? terrainTexture = terrainItem.TextureHandle;
		terrainItem.TextureHandle = null;

		// The view object rides nothing, so the draw table's skip for the ridden object skips nothing.
		var groundLayer = _world.GroundLayer;
		var riding = groundLayer.Objects.CameraAttachedTo;
		groundLayer.Objects.CameraAttachedTo = null;

		DrawWorld(view, _world.All.Concat(_transient.Items), groundLayer, x, y, width, height);

		groundLayer.Objects.CameraAttachedTo = riding;
		terrainItem.TextureHandle = terrainTexture;
		_world.RestoreFrameDetail();
		_transient.RestoreFrameView();

		Scene.Sky = sky;
		gl.Disable(EnableCap.ScissorTest);
	}

	// The scene, every beam fired on the last tick and the billboards (the EMP rounds crossing the ground, the impact
	// effects wherever shots have landed, the fires), in the mode the tweak picks for this pass. The tracers outlive the
	// tick that made them by exactly one tick, so a shot is on screen for every frame drawn in that window and for none
	// after — see BeamTracer.
	private void DrawWorld(Camera view, IEnumerable<SceneItem> items, GroundShapeLayer groundLayer,
			int x, int y, int width, int height) {
		var mode = TweakSettings.Current.GetSettingValue(TweakSettingDefinitions.PerPixelDepthSorting)
			? WorldDrawMode.PerPixelDepth
			: WorldDrawMode.PaintersOrder;
		_draw.Draw(mode, view, items, groundLayer, _transient.Sprites, _scene.World.Tracers, _transient.BeamSpans,
			x, y, width, height);
	}

	// The animating skeleton, drawn over whatever was just rendered into the current viewport.
	//
	// This is the only view of the animation system there is. The mesh is baked at the shape's default
	// pose and drawn with one matrix per object (see DtsMeshBuilder.ResolveGroupOffset), so a playing
	// walk cycle moves nothing on screen; before this, the only observable output of the whole thread
	// was where the player's eye ended up. Bones are drawn through solid geometry on purpose — the
	// skeleton is inside the model it belongs to.
	private void DrawSkeleton(Camera view, int viewportWidth, int viewportHeight) {
		if (!_debugOptions.DrawSkeleton || _pilotMech == null) {
			return;
		}

		var joints = SkeletonPose.Build(_pilotMech);
		_debugOptions.SkeletonJointCount = joints.Length;
		if (joints.Length == 0) {
			return;
		}

		float aspect = (float)viewportWidth / Math.Max(viewportHeight, 1);
		_wireframe.DrawLines(view, SkeletonWireframe.Build(joints), new Vector3(0.2f, 1f, 0.85f), aspect);

		// The node the eye rides, flagged in its own colour: it is the one joint whose motion the player
		// actually feels, so it wants to be findable among the rest.
		int cameraNode = SkeletonPose.CameraTransformId(_pilotMech);
		if (cameraNode >= 0 && cameraNode < joints.Length) {
			_wireframe.DrawLines(view,
				SkeletonWireframe.Marker(joints[cameraNode].World, SkeletonWireframe.CameraCrossMeters),
				new Vector3(1f, 0.85f, 0.1f), aspect);
		}
	}

	public void Dispose() {
		Scene.Dispose();
		_wireframe.Dispose();
		_beams?.Dispose();
		_sprites.Dispose();
	}
}
