using System.Numerics;
using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using Herculan.Engine.Sim;
using Herculan.Engine.Terrain;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>What a sculpting brush does to the ground under it (<see cref="TerrainSculptor"/>).</summary>
internal enum SculptTool {
	/// <summary>Raises the ground, or lowers it while Shift is held.</summary>
	Raise,

	/// <summary>Lowers the ground, or raises it while Shift is held.</summary>
	Lower,

	/// <summary>Moves each height toward the average of its own and its eight neighbors'.</summary>
	Smooth,

	/// <summary>
	/// Moves each height toward the elevation set (<see cref="TerrainSculptor.Elevation"/>), at the full rate; a press with Shift
	/// held takes the elevation of the ground under it instead.
	/// </summary>
	SetHeight,
}

/// <summary>
/// Sculpting the mission's terrain, the Toolbox's "Sculpt Terrain" tab, on exactly while that tab is shown
/// (<see cref="Active"/>): a left-drag in the 3D view raises, lowers, smooths or sets the height of the ground under a round
/// brush (<see cref="SculptTool"/>) instead of picking or moving anything (docs/herculan/editor-manual.md#terrain). The brush
/// is strongest at its center and falls smoothly to nothing at its rim; held still, it keeps working where it stands, at a
/// rate per second set by its strength, or for "Set height" at the full rate toward <see cref="Elevation"/>. Holding
/// Shift swaps raising for lowering. The radius runs from <see cref="MinRadius"/> to <see cref="MaxRadius"/> cells, which
/// [ and ] and the mouse wheel over the view also set (<see cref="Grow"/>). The brush is drawn as a ring on the ground,
/// filled as opaque as half its strength (<see cref="Paint"/>). The tools, the brush's shape and its rate are the editor's
/// own; retail has no terrain editing.
///
/// <para>The brush writes the heightmap as the zone loads it (<see cref="HeightGrid.WriteLoadedHeights"/>), building each
/// cell's height up in fractions of a height step over a stroke and writing the whole steps each frame, and has the ground
/// levelled again (<see cref="TerrainLeveller"/>), which rebuilds the terrain mesh over the cells that changed, stands
/// everything on the ground again and lays the route and trigger-area lines on it. While sculpting is on the levelling
/// flattens nothing under the structures, so the ground shows as sculpted; it flattens again once sculpting ends, as the
/// game does on load.</para>
///
/// <para>A stroke, from press to release, is one step of <see cref="PointMover"/>'s undo history. At its end the mission's own
/// terrain is built again from the scene's (<see cref="TerrainChanger.TakeSculpted"/>), which Save and the exports write, and
/// every flyer position rises or falls with the ground under it over the stroke (<see cref="FlyerHeights"/>). Nothing is
/// written to a file. A mission on an archive zone is first given a terrain of its own, a copy of that zone, through a prompt
/// its first press raises (<see cref="TerrainChanger.CopyZone"/>), and that press sculpts nothing; a mission whose launch sets
/// the zone cannot be sculpted (<see cref="TerrainChanger.WhyFixed"/>).</para>
/// </summary>
internal sealed class TerrainSculptor {
	/// <summary>The smallest brush radius, in cells.</summary>
	public const float MinRadius = 0.5f;

	/// <summary>The largest brush radius, in cells.</summary>
	public const float MaxRadius = 16f;

	// The prompt a first press on a mission on an archive zone raises.
	private const string PromptTitle = "Sculpt this mission's terrain###sculpt-own-terrain";

	// How many height steps a second the brush moves the ground at its center at full strength.
	private const float FullRate = 128f;

	// How much of the way to its neighbors' average smoothing moves a height in a second at the brush's center at full
	// strength, taken a frame at a time.
	private const float SmoothRate = 10f;

	// The longest frame a held brush counts, so a stall does not land as one large step.
	private const double LongestFrame = 0.1;

	private static readonly Vector3 RingColor = new(1f, 0.75f, 0.35f);

	private readonly MissionDocument _source;
	private readonly SimWorld _world;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly TerrainChanger _changer;
	private readonly DrapedLines _drape;
	private readonly FlyerHeights _flyers;

	// The stroke under way, the wheel's notches short of a whole one, and whether the prompt is up and raised.
	private Stroke? _stroke;
	private float _wheel;
	private bool _asking;
	private bool _raised;
	private bool _copyRequested;

	public TerrainSculptor(MissionDocument source, SimWorld world, MissionIndex index, PointMover mover, SceneSync sync,
			TerrainChanger changer, DrapedLines drape) {
		_source = source;
		_world = world;
		_mover = mover;
		_sync = sync;
		_changer = changer;
		_drape = drape;
		_flyers = new FlyerHeights(source, index);
	}

	/// <summary>The brush's tool.</summary>
	public SculptTool Tool { get; set; } = SculptTool.Raise;

	/// <summary>
	/// The brush's radius in cells, <see cref="MinRadius"/> to <see cref="MaxRadius"/>: steps of <see cref="MinRadius"/> below one cell, whole
	/// cells from there (<see cref="Snapped"/>).
	/// </summary>
	public float Radius { get; private set; } = 4f;

	/// <summary>The brush's strength, 1 to 100 percent of <see cref="FullRate"/>, or of <see cref="SmoothRate"/> when smoothing; "Set height" works at the full rate.</summary>
	public int Strength { get; private set; } = 25;

	/// <summary>The elevation "Set height" moves the ground toward, in height steps, 0 to 255, each the terrain's height scale.</summary>
	public int Elevation { get; private set; } = 128;

	/// <summary>Whether a route or a trigger area is being placed, while which sculpting is off; set once the mission's tools are built.</summary>
	public Func<bool>? Blocked { get; set; }

	/// <summary>Whether sculpting is on: the "Sculpt Terrain" tab is shown and nothing is being placed, as of the last <see cref="Update"/>.</summary>
	public bool Active { get; private set; }

	/// <summary>Whether a stroke is under way, while which Undo, Redo, Save and Play wait.</summary>
	public bool Stroking => _stroke != null;

	/// <summary>Whether the prompt a first press raised is up, which takes Esc.</summary>
	public bool Asking => _asking;

	/// <summary>Why the last copy of the zone could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>What the last stroke could not do to the mission's positions, or null.</summary>
	public string? Note { get; private set; }

	/// <summary>
	/// Once a frame, with whether the "Sculpt Terrain" tab is shown: turns sculpting on or off, ending a stroke it turns off,
	/// and with it the flattening under the structures off or on, levelling the ground again when that changes.
	/// </summary>
	public void Update(bool tabShown) {
		bool active = tabShown && Blocked?.Invoke() != true;
		if (!active) {
			Release();
		}

		Active = active;
		var leveller = _mover.Leveller;
		bool turned = leveller.Flattening == active;
		leveller.Flattening = !active;
		if (turned || active) {
			leveller.Relevel();
		}
	}

	/// <summary>The mouse wheel turned <paramref name="notches"/> over the view: the radius grows or shrinks a step a notch (<see cref="Grow"/>).</summary>
	public void Wheel(float notches) {
		_wheel += notches;
		int steps = (int)MathF.Truncate(_wheel);
		_wheel -= steps;
		Grow(steps);
	}

	/// <summary>
	/// Grows the radius <paramref name="steps"/> steps, or shrinks it for fewer than none: <see cref="MinRadius"/> a step below one
	/// cell, a whole cell a step from there, as [ and ] and the mouse wheel change it.
	/// </summary>
	public void Grow(int steps) {
		for (; steps > 0; steps--) {
			Radius = Math.Min(Radius < 1f ? Radius + MinRadius : Radius + 1f, MaxRadius);
		}

		for (; steps < 0; steps++) {
			Radius = Math.Max(Radius <= 1f ? Radius - MinRadius : Radius - 1f, MinRadius);
		}
	}

	/// <summary>
	/// A left press over the view, whose ray meets the ground at <paramref name="ground"/>, or none: starts a stroke, or on a
	/// mission with no terrain of its own raises the prompt instead. With "Set height" and <paramref name="shift"/> held, it
	/// takes the elevation of the ground there as <see cref="Elevation"/> instead, to the nearest height step, and sculpts
	/// nothing.
	/// </summary>
	public void Press(Vec3i? ground, bool shift) {
		if (!Active || Stroking || _asking) {
			return;
		}

		var terrain = _world.Terrain;
		if (Tool == SculptTool.SetHeight && shift) {
			if (ground is { } at) {
				Elevation = Math.Clamp((int)MathF.Round((float)terrain.HeightAtWorld(at.X, at.Y) / terrain.HeightScale), 0,
					byte.MaxValue);
			}

			return;
		}

		if (_changer.WhyFixed != null) {
			return;
		}

		Failure = null;
		Note = null;
		if (_changer.OwnZone == null) {
			_asking = true;
			_raised = false;
			return;
		}

		var start = new byte[terrain.Width * terrain.Height];
		var heights = new float[start.Length];
		for (int y = 0; y < terrain.Height; y++) {
			for (int x = 0; x < terrain.Width; x++) {
				int cell = terrain.CellIndex(x, y);
				start[cell] = terrain.LoadedHeightAt(x, y);
				heights[cell] = start[cell];
			}
		}

		var flyers = _flyers.Read();
		var grounds = new Dictionary<short, int>();
		foreach (var flyer in flyers) {
			grounds[flyer.Point] = terrain.HeightAtWorld(flyer.X, flyer.Y);
		}

		_stroke = new Stroke(terrain, start, heights, flyers, grounds);
	}

	/// <summary>
	/// The stroke held for <paramref name="seconds"/> with the pointer over the ground at <paramref name="ground"/>, or none:
	/// works the ground under the brush with the tool chosen, raising and lowering the other way while
	/// <paramref name="swap"/> (Shift) is held.
	/// </summary>
	public void Hold(Vec3i? ground, double seconds, bool swap) {
		if (_stroke is not { } stroke || ground is not { } at || !ReferenceEquals(stroke.Terrain, _world.Terrain)) {
			return;
		}

		var terrain = stroke.Terrain;
		float centerX = (float)at.X / terrain.CellSize;
		float centerY = (float)at.Y / terrain.CellSize;
		int minX = Math.Max((int)MathF.Ceiling(centerX - Radius), 0);
		int maxX = Math.Min((int)MathF.Floor(centerX + Radius), terrain.Width - 1);
		int minY = Math.Max((int)MathF.Ceiling(centerY - Radius), 0);
		int maxY = Math.Min((int)MathF.Floor(centerY + Radius), terrain.Height - 1);
		if (minX > maxX || minY > maxY) {
			return;
		}

		float frame = (float)Math.Min(seconds, LongestFrame);
		float strength = Strength / 100f;
		bool raising = (Tool == SculptTool.Raise) != swap;
		int width = maxX - minX + 1;
		float[]? averages = Tool == SculptTool.Smooth ? Averages(stroke, minX, minY, maxX, maxY) : null;
		var written = new byte[width * (maxY - minY + 1)];
		bool changed = false;
		for (int y = minY; y <= maxY; y++) {
			for (int x = minX; x <= maxX; x++) {
				byte height = terrain.LoadedHeightAt(x, y);
				float reach = MathF.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY)) / Radius;
				if (reach < 1f) {
					int cell = terrain.CellIndex(x, y);
					float now = stroke.Heights[cell];
					float weight = Falloff(reach);
					float sculpted = Math.Clamp(Tool switch {
						SculptTool.Smooth => now + (averages![(y - minY) * width + x - minX] - now)
							* Math.Min(SmoothRate * strength * frame * weight, 1f),
						SculptTool.SetHeight => now + Math.Clamp(Elevation - now, -FullRate * frame * weight, FullRate * frame * weight),
						_ => now + (raising ? 1f : -1f) * strength * FullRate * frame * weight,
					}, 0f, byte.MaxValue);
					stroke.Heights[cell] = sculpted;
					byte rounded = (byte)MathF.Round(sculpted);
					if (rounded != height) {
						height = rounded;
						changed = true;
						stroke.Touch(x, y);
					}
				}

				written[(y - minY) * width + x - minX] = height;
			}
		}

		if (!changed) {
			return;
		}

		terrain.WriteLoadedHeights(minX, minY, width, written);
		_mover.Leveller.Invalidate();
		_mover.Leveller.Relevel();
	}

	/// <summary>
	/// The left button let go, or sculpting turned off: ends the stroke, as one step of the undo history when it changed the
	/// ground.
	/// </summary>
	public void Release() {
		if (_stroke is not { } stroke) {
			return;
		}

		_stroke = null;
		if (stroke.Right < 0) {
			return;
		}

		var terrain = stroke.Terrain;
		int width = stroke.Right - stroke.Left + 1;
		var before = new byte[width * (stroke.Top - stroke.Bottom + 1)];
		var after = new byte[before.Length];
		for (int y = stroke.Bottom; y <= stroke.Top; y++) {
			for (int x = stroke.Left; x <= stroke.Right; x++) {
				int at = (y - stroke.Bottom) * width + x - stroke.Left;
				before[at] = stroke.Start[terrain.CellIndex(x, y)];
				after[at] = terrain.LoadedHeightAt(x, y);
			}
		}

		if (before.AsSpan().SequenceEqual(after)) {
			return;
		}

		var zoneBefore = _changer.OwnZone!;
		var zoneAfter = _changer.TakeSculpted();
		var skipped = new List<string>();
		MsnEdit? change = null;
		var rising = stroke.Flyers.Where(flyer => Rise(stroke, flyer) != 0).ToList();
		if (rising.Count > 0) {
			change = _sync.Run(() => _source.Change(() => _flyers.Raise(rising, 0, 0, flyer => Rise(stroke, flyer), skipped)));
		}

		Note = skipped.Count > 0 ? $"Flyer positions left at their height: {string.Join(" ", skipped)}" : null;
		_mover.Record(new SculptEdit(this, terrain, stroke.Left, stroke.Bottom, width, before, after, zoneBefore, zoneAfter,
			change));
	}

	/// <summary>Gives the mission a terrain of its own, if the prompt asked for it since the last call.</summary>
	public void RunRequest() {
		if (_copyRequested) {
			_copyRequested = false;
			Failure = _changer.CopyZone();
		}
	}

	/// <summary>
	/// The brush's fill, painted on the ground at <paramref name="pointer"/>, the ground under the pointer, while sculpting is
	/// on: added last to <paramref name="discs"/>, so it lies over the team discs, blended half as far as its strength, or
	/// half way for "Set height", which works at the full rate.
	/// </summary>
	public void Paint(GroundDiscs discs, Vec3i? pointer) {
		if (!Active || pointer is not { } at) {
			return;
		}

		var center = WorldScale.ToRender(at);
		discs.Items.Add(new GroundDisc(new Vector2(center.X, center.Z), WorldScale.DistanceToRender(RadiusWorld),
			new Vector4(RingColor, (Tool == SculptTool.SetHeight ? 100 : Strength) / 200f)));
	}

	/// <summary>The brush's ring, laid on the ground at <paramref name="pointer"/>, the ground under the pointer, while sculpting is on, opaque.</summary>
	public void Draw(WireframeRenderer wireframe, Camera camera, float aspect, Vec3i? pointer) {
		if (!Active || pointer is not { } at) {
			return;
		}

		var lines = new List<Vector3>();
		_drape.AddCircle(lines, at, RadiusWorld);
		MissionOverlay.DrawLines(wireframe, camera, aspect, lines, RingColor);
	}

	// The brush's radius in world units.
	private int RadiusWorld => (int)(Radius * _world.Terrain.CellSize);

	/// <summary>
	/// The Toolbox's "Sculpt Terrain" tab: the tool, the radius, and the strength or for "Set height" the elevation, and why
	/// the brush does nothing when it does.
	/// </summary>
	public void DrawToolboxEntry() {
		EditorText.Note(Tool switch {
			SculptTool.Smooth => "Click and drag to smooth the terrain, evening out bumps and hollows.",
			SculptTool.SetHeight => "Click and drag to bring the terrain to the elevation set below. Shift-click the terrain to "
				+ "pick up its elevation.",
			_ => "Click and drag to raise or lower the terrain. Hold Shift to move it in the opposite direction.",
		} + " The mouse wheel, or [ and ], change the radius.");
		ImGui.AlignTextToFramePadding();
		ImGui.Text("Tool:");
		foreach (var (tool, label, tip) in new[] {
				(SculptTool.Raise, "Raise", "Raises the ground under the brush. Hold Shift to lower it instead."),
				(SculptTool.Lower, "Lower", "Lowers the ground under the brush. Hold Shift to raise it instead."),
				(SculptTool.Smooth, "Smooth", "Evens out the ground under the brush, each point toward the average of those around it."),
				(SculptTool.SetHeight, "Set height", "Brings the ground under the brush to the elevation set below. Shift-click "
					+ "the ground to set the elevation to its own.") }) {
			SameLineIfRoom(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + EditorText.TextRoom(label));
			if (ImGui.RadioButton(label, Tool == tool)) {
				Tool = tool;
			}

			EditorText.Tooltip(tip);
		}

		float radius = Radius;
		string meters = MissionText.Metres(RadiusWorld);
		if (ImGui.SliderFloat("Radius", ref radius, MinRadius, MaxRadius, meters,
				ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic)) {
			Radius = Snapped(radius);
		}

		EditorText.Tooltip("How far the brush reaches from its center: a half of a ground cell up to 16 cells. The mouse "
			+ "wheel over the view, and [ and ], change it too.");
		if (Tool == SculptTool.SetHeight) {
			int elevation = Elevation;
			if (ImGui.SliderInt("Elevation", ref elevation, 0, byte.MaxValue,
					MissionText.Metres(Elevation * _world.Terrain.HeightScale), ImGuiSliderFlags.AlwaysClamp)) {
				Elevation = elevation;
			}

			EditorText.Tooltip("The elevation the brush brings the ground to, from the terrain's lowest to its highest. "
				+ "Shift-click the ground to take its elevation.");
		} else {
			int strength = Strength;
			if (ImGui.SliderInt("Strength", ref strength, 1, 100, "%d%%", ImGuiSliderFlags.AlwaysClamp)) {
				Strength = strength;
			}

			EditorText.Tooltip(Tool == SculptTool.Smooth
				? "How fast the brush evens out the ground at its center while it is held."
				: "How fast the brush raises or lowers the ground at its center while it is held.");
		}
		ImGui.Separator();
		EditorText.Reason(_changer.WhyFixed, "Cannot be sculpted: ");
		EditorText.Reason(Blocked?.Invoke() == true ? "A route or trigger area is being placed." : null);
		EditorText.Reason(Failure, "No terrain of its own: ");
		if (Note != null) {
			EditorText.Note(Note);
		}

		EditorText.Note("Terrain flattening from structures is not shown while sculpting.");
	}

	/// <summary>Draws the prompt a first press raised, if any. Call once per frame, inside the ImGui frame, outside any menu.</summary>
	public void DrawPrompt() {
		if (!_asking) {
			return;
		}

		EditorModal.Raise(PromptTitle, ref _raised);
		ImGui.SetNextWindowSize(new Vector2(ScaledImGui.Scaled(PropertiesPanel.PanelWidth), 0f));
		ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		if (!ImGui.BeginPopupModal(PromptTitle, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove)) {
			_raised = false;
			return;
		}

		short zone = _changer.Zone;
		EditorText.Wrapped($"This mission is played on zone {zone}, one of the install's zones, which other missions share.");
		EditorText.Wrapped($"To sculpt it, the mission gets a terrain of its own: a copy of zone {zone}, saved with the mission. "
			+ "It looks the same until you sculpt it, and you can undo this like any other edit.");
		ImGui.Separator();
		bool close = false;
		if (ImGui.Button("Give it its own terrain")) {
			_copyRequested = true;
			close = true;
		}

		ImGui.SameLine();
		if (ImGui.Button("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape)) {
			close = true;
		}

		if (close) {
			_asking = false;
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();
	}

	// <radius> in cells as the radius takes it: to a step of MinRadius below one cell, to a whole cell from there.
	private static float Snapped(float radius) =>
		Math.Clamp(radius < 1f ? MathF.Round(radius / MinRadius) * MinRadius : MathF.Round(radius), MinRadius, MaxRadius);

	// Puts the next item, <width> wide, on the line with the last one when it fits there, else on the next line.
	private static void SameLineIfRoom(float width) {
		ImGui.SameLine();
		if (ImGui.GetContentRegionAvail().X < width) {
			ImGui.NewLine();
		}
	}

	// The average of each cell from (<minX>, <minY>) to (<maxX>, <maxY>) and its eight neighbors on the grid, as the stroke
	// has built them up, in rows from the lowest; taken before any of them moves, so smoothing reads the same heights
	// whichever order it works the cells in.
	private static float[] Averages(Stroke stroke, int minX, int minY, int maxX, int maxY) {
		var terrain = stroke.Terrain;
		int width = maxX - minX + 1;
		var averages = new float[width * (maxY - minY + 1)];
		for (int y = minY; y <= maxY; y++) {
			for (int x = minX; x <= maxX; x++) {
				float sum = 0f;
				int count = 0;
				for (int ny = Math.Max(y - 1, 0); ny <= Math.Min(y + 1, terrain.Height - 1); ny++) {
					for (int nx = Math.Max(x - 1, 0); nx <= Math.Min(x + 1, terrain.Width - 1); nx++) {
						sum += stroke.Heights[terrain.CellIndex(nx, ny)];
						count++;
					}
				}

				averages[(y - minY) * width + x - minX] = sum / count;
			}
		}

		return averages;
	}

	// How strongly the brush works at <reach>, its distance from the center as a share of its radius: 1 at the center,
	// falling smoothly to 0 at the rim.
	private static float Falloff(float reach) {
		float t = 1f - reach;
		return t * t * (3f - 2f * t);
	}

	// How far the ground under <flyer> has risen since the stroke began.
	private static int Rise(Stroke stroke, FlyerPosition flyer) =>
		stroke.Terrain.HeightAtWorld(flyer.X, flyer.Y) - stroke.Grounds[flyer.Point];

	// A stroke under way: the terrain it works, the heights the zone loaded as it began and as it has built them up, cell by
	// cell, the flyer positions and the ground under each as it began, and the rectangle of cells it has changed.
	private sealed class Stroke(HeightGrid terrain, byte[] start, float[] heights, IReadOnlyList<FlyerPosition> flyers,
			Dictionary<short, int> grounds) {
		public HeightGrid Terrain { get; } = terrain;

		public byte[] Start { get; } = start;

		public float[] Heights { get; } = heights;

		public IReadOnlyList<FlyerPosition> Flyers { get; } = flyers;

		public Dictionary<short, int> Grounds { get; } = grounds;

		public int Left { get; private set; } = int.MaxValue;

		public int Bottom { get; private set; } = int.MaxValue;

		public int Right { get; private set; } = -1;

		public int Top { get; private set; } = -1;

		public void Touch(int x, int y) {
			Left = Math.Min(Left, x);
			Bottom = Math.Min(Bottom, y);
			Right = Math.Max(Right, x);
			Top = Math.Max(Top, y);
		}
	}

	// Puts the heights the zone loads, the mission's own terrain and the flyer positions back as the stroke found or left them
	// over the cells it changed, from (<Left>, <Bottom>) in rows of <Width>; the history then levels the ground.
	private sealed record SculptEdit(TerrainSculptor Sculptor, HeightGrid Terrain, int Left, int Bottom, int Width, byte[] Before,
			byte[] After, ZoneFiles ZoneBefore, ZoneFiles ZoneAfter, MsnEdit? Change) : IEditorEdit {
		public void Undo() {
			Terrain.WriteLoadedHeights(Left, Bottom, Width, Before);
			Sculptor._changer.PutSculpted(ZoneBefore);
			if (Change != null) {
				Sculptor._sync.Undo(Change);
			}

			Sculptor._mover.Leveller.Invalidate();
		}

		public void Redo() {
			Terrain.WriteLoadedHeights(Left, Bottom, Width, After);
			Sculptor._changer.PutSculpted(ZoneAfter);
			if (Change != null) {
				Sculptor._sync.Redo(Change);
			}

			Sculptor._mover.Leveller.Invalidate();
		}
	}
}
