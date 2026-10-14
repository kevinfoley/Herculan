using System.Numerics;
using Herculan.Engine.Platform;
using ImGuiNET;
using Silk.NET.Input;

namespace Herculan.Engine.Host.Simulator;

/// <summary>
/// The squad orders on a gamepad, round the centre of the screen while the left stick's click is held
/// (docs/herculan/herculan-key-bindings.md, "Squad orders on a gamepad"). This engine's own. It picks and draws; the
/// host transmits the row picked through FLASH COMM's [Alt] key and mutes the mission's stick while it is up.
/// </summary>
sealed class SquadOrderRadial {
	/// <summary>How many orders the radial offers: FLASH COMM's rows.</summary>
	public const int OrderCount = Cockpit.MfdFlashCommScreen.RowCount;

	// How far the stick has to lean, as a fraction of its travel, to pick an order.
	private const float PickTravel = 0.5f;

	// In pixels on a 100% display (ScaledImGui.Scaled): the ring the orders' labels are centred on, the disc behind
	// them, and the dot that shows where the stick is.
	private const float LabelRadius = 150f;
	private const float DiscRadius = 215f;
	private const float StickDotRadius = 6f;
	private const float StickDotTravel = 40f;

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

	/// <summary>Whether the radial is up.</summary>
	public bool IsOpen => _slot >= 0;

	/// <summary>The order picked, by FLASH COMM row, or -1 for none.</summary>
	public int Picked { get; private set; } = -1;

	/// <summary>
	/// Reads the pads once a frame. The radial opens on a click pressed while <paramref name="allowed"/>, and closes
	/// without transmitting when that stops being so. Returns the row to transmit, on the frame the click is let go
	/// with one picked.
	/// </summary>
	public int? Update(IInputContext input, bool allowed) {
		bool clickDown = false;
		int clickSlot = -1;
		foreach (var device in ControllerDevice.Connected(input)) {
			if (device.IsGamepad && device.Button(ControllerDevice.GamepadLeftStick)) {
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

		if (pad.Button(ControllerDevice.GamepadLeftStick)) {
			_stick = new Vector2(pad.Axis(0), pad.Axis(1));
			Picked = Pick(_stick);
			return null;
		}

		int picked = Picked;
		Cancel();
		return picked >= 0 ? picked : null;
	}

	/// <summary>
	/// Draws the radial while it is up, over everything else ImGui draws, each order labelled with
	/// <paramref name="label"/>'s text for its row. Call once per frame inside the ImGui frame.
	/// </summary>
	public void Draw(Func<int, string> label) {
		if (!IsOpen) {
			return;
		}

		var draw = ImGui.GetForegroundDrawList();
		var centre = ImGui.GetMainViewport().GetCenter();
		float discRadius = ScaledImGui.Scaled(DiscRadius);
		draw.AddCircleFilled(centre, discRadius, DiscColour);

		const float slice = MathF.Tau / OrderCount;
		if (Picked >= 0) {
			float middle = Picked * slice - MathF.PI / 2f;
			draw.PathLineTo(centre);
			draw.PathArcTo(centre, discRadius, middle - slice / 2f, middle + slice / 2f);
			draw.PathFillConvex(SliceColour);
		}

		float labelRadius = ScaledImGui.Scaled(LabelRadius);
		for (int row = 0; row < OrderCount; row++) {
			string text = label(row);
			var at = centre + Direction(row) * labelRadius - ImGui.CalcTextSize(text) / 2f;
			draw.AddText(at, row == Picked ? PickedLabelColour : LabelColour, text);
		}

		draw.AddCircleFilled(centre + _stick * ScaledImGui.Scaled(StickDotTravel), ScaledImGui.Scaled(StickDotRadius),
			StickDotColour);
	}

	/// <summary>Takes the radial down without transmitting, as the window losing the focus does.</summary>
	public void Cancel() {
		_slot = -1;
		Picked = -1;
	}

	// The row nearest the way the stick leans, or -1 while it is near the centre. GLFW reads a stick pushed up as
	// negative, which is also the screen's way up.
	private static int Pick(Vector2 stick) {
		if (stick.Length() < PickTravel) {
			return -1;
		}

		float clockwiseFromUp = MathF.Atan2(stick.X, -stick.Y);
		int row = (int)MathF.Round(clockwiseFromUp / (MathF.Tau / OrderCount));
		return (row % OrderCount + OrderCount) % OrderCount;
	}

	// The screen direction of a row's label: the first straight up, the rest clockwise.
	private static Vector2 Direction(int row) {
		float angle = row * MathF.Tau / OrderCount;
		return new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
	}
}
