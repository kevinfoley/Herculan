using System.Diagnostics;
using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// A ring of choices round the centre of the screen, worked by one of a gamepad's sticks while that stick's click is
/// held: leaning the stick picks the nearest choice, centring it picks none, and letting go of the click takes the one
/// picked. The squad orders and the cockpit's screens are one each (docs/herculan/herculan-key-bindings.md, "Squad
/// orders on a gamepad" and "Screens on a gamepad"). This engine's own. It picks and draws; the host acts on the choice
/// and mutes the mission's stick while <see cref="MutesStick"/>.
/// </summary>
/// <param name="clickButton">The stick's click, in the standard gamepad layout.</param>
/// <param name="xAxis">The stick's X axis; its Y is the next.</param>
/// <param name="count">How many choices; the first is at the top and the rest follow clockwise.</param>
/// <param name="labelRadius">
/// How far from the centre the labels are centred, in pixels on a 100% display (<see cref="ScaledImGui.Scaled(float)"/>),
/// far enough apart for neighbouring labels of <see cref="LabelWidth"/> not to meet.
/// </param>
sealed class PadRadial(int clickButton, int xAxis, int count, float labelRadius) {
	/// <summary>The widest a label runs before it wraps onto another line, in pixels on a 100% display.</summary>
	public const float LabelWidth = 200f;

	// How far the stick has to lean, as a fraction of its travel, to pick a choice.
	private const float PickTravel = 0.5f;

	// How long the mission's stick stays muted after a choice is taken, so the stick springing back from the choice as
	// it is let go does not steer or aim.
	private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(0.5);

	// In pixels on a 100% display: the disc's margin beyond the labels, and the dot that shows where the stick is.
	private const float DiscMargin = 20f;
	private const float StickDotRadius = 8f;
	private const float StickDotTravel = 50f;

	// As ABGR.
	private const uint DiscColour = 0xB0000000;
	private const uint SliceColour = 0xC0207FFF;
	private const uint LabelColour = 0xFFE0E0E0;
	private const uint PickedLabelColour = 0xFFFFFFFF;
	private const uint StickDotColour = 0xFFFFFFFF;

	// GLFW's slot of the gamepad whose click opened the radial, or -1 while it is down.
	private int _slot = -1;
	private bool _clickDown;
	private Vector2 _stick;

	// The Stopwatch timestamp of the last choice taken, or 0 for none.
	private long _takenAt;

	/// <summary>Whether the radial is up.</summary>
	public bool IsOpen => _slot >= 0;

	/// <summary>
	/// Whether the mission's stick should drive nothing: while the radial is up, and for <see cref="SettleTime"/> after a
	/// choice is taken. Taking nothing leaves the stick at once, since a centred stick has nothing to spring back from.
	/// </summary>
	public bool MutesStick => IsOpen || (_takenAt != 0 && Stopwatch.GetElapsedTime(_takenAt) < SettleTime);

	/// <summary>The choice picked, or -1 for none.</summary>
	public int Picked { get; private set; } = -1;

	/// <summary>
	/// Reads the pads once a frame. The radial opens on a click pressed while <paramref name="allowed"/>, and closes
	/// taking nothing when that stops being so. Returns the choice taken, on the frame the click is let go with one
	/// picked.
	/// </summary>
	public int? Update(IInputContext input, bool allowed) {
		bool clickDown = false;
		int clickSlot = -1;
		foreach (var device in ControllerDevice.Connected(input)) {
			if (device.IsGamepad && device.Button(clickButton)) {
				clickDown = true;
				clickSlot = device.Slot;
				break;
			}
		}

		bool clicked = clickDown && !_clickDown;
		_clickDown = clickDown;

		if (!IsOpen) {
			if (clicked && allowed) {
				_slot = clickSlot;
				Picked = -1;
				_stick = Vector2.Zero;
			}

			return null;
		}

		if (!allowed || ControllerDevice.At(input, _slot) is not { IsGamepad: true } pad) {
			Cancel();
			return null;
		}

		if (pad.Button(clickButton)) {
			_stick = new Vector2(pad.Axis(xAxis), pad.Axis(xAxis + 1));
			Picked = Pick(_stick);
			return null;
		}

		int picked = Picked;
		Cancel();
		if (picked < 0) {
			return null;
		}

		_takenAt = Stopwatch.GetTimestamp();
		return picked;
	}

	/// <summary>Takes the radial down taking nothing, as the window losing the focus does.</summary>
	public void Cancel() {
		_slot = -1;
		Picked = -1;
	}

	/// <summary>
	/// Draws the radial while it is up, over everything else ImGui draws, each choice labelled with
	/// <paramref name="label"/>'s text for it in <paramref name="font"/> (<see cref="ScaledImGui.LargeFont"/>). Call once per
	/// frame inside the ImGui frame.
	/// </summary>
	public void Draw(Func<int, string> label, ImFontPtr font) {
		if (!IsOpen) {
			return;
		}

		var draw = ImGui.GetForegroundDrawList();
		var centre = ImGui.GetMainViewport().GetCenter();
		float fontSize = ScaledImGui.LargeFontSize;
		float labelWidth = ScaledImGui.Scaled(LabelWidth);
		float radius = ScaledImGui.Scaled(labelRadius);
		float discRadius = radius + labelWidth / 2f + ScaledImGui.Scaled(DiscMargin);
		draw.AddCircleFilled(centre, discRadius, DiscColour);

		float slice = MathF.Tau / count;
		if (Picked >= 0) {
			float middle = Picked * slice - MathF.PI / 2f;
			draw.PathLineTo(centre);
			draw.PathArcTo(centre, discRadius, middle - slice / 2f, middle + slice / 2f);
			draw.PathFillConvex(SliceColour);
		}

		for (int choice = 0; choice < count; choice++) {
			var lines = Wrap(label(choice), font, fontSize, labelWidth);
			var at = centre + Direction(choice) * radius;
			float top = at.Y - lines.Count * fontSize / 2f;
			uint colour = choice == Picked ? PickedLabelColour : LabelColour;
			foreach (string line in lines) {
				float width = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, line).X;
				draw.AddText(font, fontSize, new Vector2(at.X - width / 2f, top), colour, line);
				top += fontSize;
			}
		}

		draw.AddCircleFilled(centre + _stick * ScaledImGui.Scaled(StickDotTravel), ScaledImGui.Scaled(StickDotRadius),
			StickDotColour);
	}

	// The choice nearest the way the stick leans, or -1 while it is near the centre. GLFW reads a stick pushed up as
	// negative, which is also the screen's way up.
	private int Pick(Vector2 stick) {
		if (stick.Length() < PickTravel) {
			return -1;
		}

		float clockwiseFromUp = MathF.Atan2(stick.X, -stick.Y);
		int choice = (int)MathF.Round(clockwiseFromUp / (MathF.Tau / count));
		return (choice % count + count) % count;
	}

	// The screen direction of a choice's label: the first straight up, the rest clockwise.
	private Vector2 Direction(int choice) {
		float angle = choice * MathF.Tau / count;
		return new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
	}

	// The label broken between words into lines no wider than width, each centred on its own when drawn. A word wider
	// than that has a line to itself.
	private static List<string> Wrap(string text, ImFontPtr font, float fontSize, float width) {
		var lines = new List<string>();
		string line = string.Empty;
		foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
			string longer = line.Length == 0 ? word : line + " " + word;
			if (line.Length > 0 && font.CalcTextSizeA(fontSize, float.MaxValue, 0f, longer).X > width) {
				lines.Add(line);
				line = word;
			} else {
				line = longer;
			}
		}

		if (line.Length > 0) {
			lines.Add(line);
		}

		return lines;
	}
}
