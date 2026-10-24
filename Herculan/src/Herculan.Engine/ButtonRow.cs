using ImGuiNET;
using System.Numerics;

namespace Herculan.Engine;

/// <summary>
/// A row of buttons that fills the available width without clipping a label, whatever language it is in: equal
/// widths when every label fits an equal share, else each button its label's own width plus an equal share of what
/// is left over. When even the labels' own widths overflow the row, every button takes the whole width on a line of
/// its own. Draw the buttons in order with <see cref="Button"/>.
/// </summary>
public ref struct ButtonRow {
	private readonly Span<float> _widths;
	private readonly bool _oneLine;
	private int _drawn;

	/// <param name="widths">
	/// Each button's <see cref="LabelWidth"/>, in drawing order. The row overwrites them with the widths it draws at,
	/// so a caller can pass a <c>stackalloc</c> span and allocate nothing.
	/// </param>
	public ButtonRow(Span<float> widths) {
		_widths = widths;
		float spacing = ImGui.GetStyle().ItemSpacing.X;
		float available = ImGui.GetContentRegionAvail().X;
		float labelled = available - spacing * (widths.Length - 1);
		float share = labelled / widths.Length;
		float needed = 0f;
		float widest = 0f;
		foreach (float width in widths) {
			needed += width;
			widest = Math.Max(widest, width);
		}

		_oneLine = needed <= labelled;
		if (widest <= share) {
			widths.Fill(share);
		} else if (_oneLine) {
			float spare = (labelled - needed) / widths.Length;
			for (int i = 0; i < widths.Length; i++) {
				widths[i] += spare;
			}
		} else {
			widths.Fill(available);
		}
	}

	/// <summary>The width a button needs to show <paramref name="label"/> in full.</summary>
	public static float LabelWidth(string label) => ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2;

	/// <summary>The row's height: one frame, or one per button with spacing between when they did not fit one line.</summary>
	public readonly float Height => _oneLine
		? ImGui.GetFrameHeight()
		: _widths.Length * ImGui.GetFrameHeight() + (_widths.Length - 1) * ImGui.GetStyle().ItemSpacing.Y;

	/// <summary>Draws the row's next button; true when it was clicked.</summary>
	public bool Button(string label) {
		if (_drawn > 0 && _oneLine) {
			ImGui.SameLine();
		}

		return ImGui.Button(label, new Vector2(_widths[_drawn++], 0f));
	}
}
