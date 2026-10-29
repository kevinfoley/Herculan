using System.Numerics;
using Herculan.Engine.Numerics;
using Herculan.Engine.Platform;
using Herculan.Engine.Render;
using ImGuiNET;

namespace Herculan.Engine.Editor;

/// <summary>
/// The editor's area-placing mode: the Toolbox's or the Trigger areas list's New box or New circle starts it. A box is made
/// by pressing on the ground at one corner and dragging to the opposite corner, a circle by pressing at its centre and
/// dragging out its radius; the area is drawn on the ground as it grows and added when the button is let go, as one edit,
/// one step of the undo history, through <see cref="AreaEditor.CreateBox"/> or <see cref="AreaEditor.CreateCircle"/>, once
/// the frame's ImGui is rendered (<see cref="RunRequest"/>). A drag that makes a box of no width or depth, or a circle of no
/// radius, adds nothing; Esc or the Toolbox's Cancel ends the mode adding nothing (<see cref="Cancel"/>). Placing drags
/// rather than clicks is the user's choice for this editor.
///
/// <para>While it is on, the host routes the view's left presses and releases here instead of to the picker and the move
/// handles, and Esc here instead of to the unsaved-changes prompt; the camera flies as ever (<see cref="EditorHost"/>).</para>
/// </summary>
internal sealed class AreaPlacing {
	private const string BoxHint = "Press on the ground at one corner and drag to the opposite corner. Esc cancels.";
	private const string CircleHint = "Press on the ground at the centre and drag out the radius. Esc cancels.";

	private static readonly Vector3 BandColor = new(1f, 1f, 0.6f);

	private readonly AreaEditor _editor;
	private readonly DrapedLines _drape;

	// Where the press began, the ground last under the pointer, and the drag let go, until the mode ends.
	private Vec3i? _anchor;
	private Vec3i? _pointer;
	private (Vec3i From, Vec3i To)? _finish;

	public AreaPlacing(AreaEditor editor, DrapedLines drape) {
		_editor = editor;
		_drape = drape;
	}

	/// <summary>Whether an area is being placed.</summary>
	public bool Active { get; private set; }

	/// <summary>Whether the area being placed is a circle rather than a box.</summary>
	public bool Circle { get; private set; }

	/// <summary>Why the last placing could not be added, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Whether another placing mode is on, which this one does not start during; set once the mission's tools are built.</summary>
	public Func<bool>? Blocked { get; set; }

	/// <summary>Why placing cannot start, or null when it can.</summary>
	public string? WhyNoStart => Blocked?.Invoke() == true ? "A route is being placed." : null;

	/// <summary>What the mode asks of the user, in the Toolbox and over the view.</summary>
	public string Hint => Circle ? CircleHint : BoxHint;

	/// <summary>Starts placing a box.</summary>
	public void BeginBox() => Begin(circle: false);

	/// <summary>Starts placing a circle.</summary>
	public void BeginCircle() => Begin(circle: true);

	/// <summary>A left press over the view, whose ray meets the ground at <paramref name="ground"/>, or none: the drag starts there.</summary>
	public void Press(Vec3i? ground) {
		if (Active && _finish == null && ground is { } at) {
			_anchor = Flat(at);
			_pointer = _anchor;
		}
	}

	/// <summary>The ground under the pointer this frame, or none when it is over a panel or the sky.</summary>
	public void Hover(Vec3i? ground) {
		if (Active && ground is { } at) {
			_pointer = Flat(at);
		}
	}

	/// <summary>
	/// The left button let go, over the ground at <paramref name="ground"/> or none, where the ground last under the pointer
	/// counts: ends a drag begun over the view.
	/// </summary>
	public void Release(Vec3i? ground) {
		if (Active && _finish == null && _anchor is { } from) {
			_finish = (from, ground is { } at ? Flat(at) : _pointer ?? from);
		}
	}

	/// <summary>Ends placing and adds nothing.</summary>
	public void Cancel() {
		if (Active) {
			Reset();
			Failure = null;
		}
	}

	/// <summary>Adds the area a release finished, if any; <see cref="Failure"/> says why it could not be added.</summary>
	public void RunRequest() {
		if (_finish is not var (from, to)) {
			return;
		}

		bool circle = Circle;
		Reset();
		Failure = circle ? _editor.CreateCircle(from, Radius(from, to)) : _editor.CreateBox(from, to);
	}

	/// <summary>The area as the drag has it so far, laid on the ground.</summary>
	public void Draw(WireframeRenderer wireframe, Camera camera, float aspect) {
		if (!Active || _anchor is not { } from || _pointer is not { } to) {
			return;
		}

		var lines = new List<Vector3>();
		if (Circle) {
			_drape.AddCircle(lines, from, Radius(from, to));
			_drape.AddPost(lines, from.X, from.Y, MissionOverlay.PostHeight);
		} else {
			_drape.AddRectangle(lines, from, to);
		}

		MissionOverlay.DrawLines(wireframe, camera, aspect, lines, BandColor);
	}

	/// <summary>The area's size by the pointer while it is dragged, and <see cref="Hint"/> centred at the top of the view, below <paramref name="top"/>.</summary>
	public void DrawLabels(Camera camera, Vector2 display, float top) {
		if (!Active) {
			return;
		}

		var drawList = ImGui.GetBackgroundDrawList();
		if (_anchor is { } from && _pointer is { } to
				&& new ScreenProjection(camera).ToScreen(_drape.OnGround(to.X, to.Y)) is { } at) {
			string size = Circle
				? $"radius {MissionText.Distance(Radius(from, to))}"
				: $"{MissionText.Distance(Math.Abs(to.X - from.X))} by {MissionText.Distance(Math.Abs(to.Y - from.Y))}";
			MissionOverlay.Label(drawList, at + new Vector2(ScaledImGui.Scaled(10f), 0f), size, BandColor);
		}

		var hintSize = ImGui.CalcTextSize(Hint);
		MissionOverlay.Label(drawList, new Vector2((display.X - hintSize.X) / 2f, top + ScaledImGui.Scaled(16f)), Hint, BandColor);
	}

	/// <summary>The Toolbox's trigger-area entry: New box and New circle, or while placing Cancel.</summary>
	public void DrawToolboxEntry() {
		if (Active) {
			ImGui.Text(Circle ? "New circle" : "New box");
			ImGui.SameLine();
			if (ImGui.Button("Cancel##area")) {
				Cancel();
			}

			EditorText.Note(Hint);
		} else {
			DrawStartButtons(small: false);
		}

		EditorText.Reason(Failure);
	}

	/// <summary>New box and New circle, as buttons, or small ones for a list's header (<see cref="MissionOutliner"/>).</summary>
	public void DrawStartButtons(bool small) {
		ImGui.BeginDisabled(Active || WhyNoStart != null);
		if (small ? ImGui.SmallButton("New box") : ImGui.Button("New box")) {
			BeginBox();
		}

		ImGui.SameLine();
		if (small ? ImGui.SmallButton("New circle") : ImGui.Button("New circle")) {
			BeginCircle();
		}

		ImGui.EndDisabled();
	}

	private void Begin(bool circle) {
		if (Active || WhyNoStart != null) {
			return;
		}

		Reset();
		Circle = circle;
		Failure = null;
		Active = true;
	}

	private void Reset() {
		_anchor = null;
		_pointer = null;
		_finish = null;
		Active = false;
	}

	// A circle's radius for a drag from its centre to <to>: the ground-plane distance, in world units.
	private static int Radius(Vec3i centre, Vec3i to) {
		double dx = (double)to.X - centre.X, dy = (double)to.Y - centre.Y;
		return (int)Math.Min(Math.Round(Math.Sqrt(dx * dx + dy * dy)), int.MaxValue);
	}

	// A ground point at Z 0, as the area's points are written.
	private static Vec3i Flat(Vec3i at) => new(at.X, at.Y, 0);
}
