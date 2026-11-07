namespace Herculan.Engine.Cockpit;

/// <summary>Which way a gamepad's d-pad or stick steps a modal panel's focus.</summary>
public enum PadDirection {
	Up,
	Down,
	Left,
	Right,
}

/// <summary>
/// A gamepad stepping a modal panel's focus by where its widgets sit, rather than by their order in its list as
/// [Tab] and joystick button 2 do (<see cref="AlertPanelFocus"/>). This engine's own, retail having no gamepad:
/// docs/herculan/herculan-key-bindings.md, "Retail's panels on a gamepad".
///
/// <para>The step lands on the nearest widget whose centre lies that way from the focused one's. A widget level with
/// the focused one across the step, its edges overlapping the focused one's, wins over one off to the side however
/// near, so a step down a column stays in the column. Off-axis distance counts double among the rest. Nothing that
/// way leaves the focus where it is: the step does not wrap.</para>
/// </summary>
public static class PanelPadFocus {
	/// <summary>
	/// Steps <paramref name="focus"/> toward <paramref name="direction"/> through <see cref="AlertPanelFocus.Set"/>, so the
	/// pointer follows as it does for any focus change. With nothing focused, the first widget takes the focus, as
	/// [Return] gives it.
	/// </summary>
	public static void Step(AlertPanelFocus focus, PadDirection direction) {
		if (focus.Count <= 0) {
			return;
		}

		if (focus.Index < 0 || focus.Index >= focus.Count) {
			focus.Set(0);
		} else if (Nearest(focus.RectOf, focus.Count, focus.Index, direction) is { } next) {
			focus.Set(next);
		}
	}

	/// <summary>The widget a step from <paramref name="from"/> toward <paramref name="direction"/> lands on, or null for none.</summary>
	public static int? Nearest(Func<int, AlertPanelLayout.Rect> rect, int count, int from, PadDirection direction) {
		var origin = rect(from);
		bool vertical = direction is PadDirection.Up or PadDirection.Down;
		int sign = direction is PadDirection.Down or PadDirection.Right ? 1 : -1;

		// A centre has to be past a quarter of the focused widget's own extent to count as that way, so a neighbour a
		// pixel lower in the same row is not "down".
		float threshold = (vertical ? origin.Y1 - origin.Y0 : origin.X1 - origin.X0) / 4f;

		int? best = null;
		bool bestOffAxis = false;
		float bestScore = 0f;
		for (int i = 0; i < count; i++) {
			if (i == from) {
				continue;
			}

			var candidate = rect(i);
			float along = sign * (vertical ? CentreY(candidate) - CentreY(origin) : CentreX(candidate) - CentreX(origin));
			if (along <= threshold) {
				continue;
			}

			float across = MathF.Abs(vertical ? CentreX(candidate) - CentreX(origin) : CentreY(candidate) - CentreY(origin));
			bool level = vertical
				? candidate.X0 <= origin.X1 && candidate.X1 >= origin.X0
				: candidate.Y0 <= origin.Y1 && candidate.Y1 >= origin.Y0;
			bool offAxis = !level;
			float score = level ? along + across / 8f : along + 2f * across;
			bool better = best == null
				|| (offAxis != bestOffAxis ? !offAxis : score < bestScore);
			if (better) {
				best = i;
				bestOffAxis = offAxis;
				bestScore = score;
			}
		}

		return best;
	}

	private static float CentreX(AlertPanelLayout.Rect rect) => (rect.X0 + rect.X1) / 2f;

	private static float CentreY(AlertPanelLayout.Rect rect) => (rect.Y0 + rect.Y1) / 2f;
}
