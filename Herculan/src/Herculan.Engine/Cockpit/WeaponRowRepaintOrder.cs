using HercWorks.Core.Data.Struct;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// The order the weapon rows were last repainted in, which is the order they have to be drawn in for
/// neighbouring row plates to overlap as they do in retail.
///
/// <para>Retail paints a row into the cockpit's own page and leaves it there until something repaints it:
/// the row's first paint once the power-up reaches it, the frame it comes back from the sensor dropout's
/// dark, and any change to the bytes its mount pushes — selected, in the fire group, ready, destroyed. A
/// change only marks the row, and the gauges' own updates repaint the marked ones later in the frame, in
/// the order the mounts built them. A plate's two-pixel bezel lies outside its <c>.GAU</c> rect, so where
/// two rects stand closer than that (the RAZOR's rows 3 and 6) the row repainted last owns the shared
/// columns. This engine redraws the whole panel every frame, so it draws the rows oldest repaint first to
/// end up with the same pixels. See docs/retail/simulation/cockpit-hud-widgets.md#weapon-hardpoint-rows.</para>
///
/// <para>Retail also repaints a row on a press that leaves its state as it was (arming the row already armed);
/// this tracks state changes only, so such a press leaves the order alone.</para>
/// </summary>
public sealed class WeaponRowRepaintOrder {
	private WeaponRowState[] _last = Array.Empty<WeaponRowState>();
	private long[] _paintedAt = Array.Empty<long>();
	private int[] _order = Array.Empty<int>();
	private readonly List<int> _repainting = new();
	private long _sequence;

	/// <summary>
	/// Takes this frame's rows and returns the slots in the order to draw them, the most recently repainted
	/// last. The list is reused from call to call.
	/// </summary>
	public IReadOnlyList<int> Update(IReadOnlyList<WeaponRowState> rows) {
		if (_last.Length != rows.Count) {
			_last = new WeaponRowState[rows.Count];
			Array.Fill(_last, WeaponRowState.Empty with { Powered = false });
			_paintedAt = new long[rows.Count];
			_order = new int[rows.Count];
		}

		_repainting.Clear();
		for (int slot = 0; slot < rows.Count; slot++) {
			if (Repaints(_last[slot], rows[slot])) {
				_repainting.Add(slot);
			}

			_last[slot] = rows[slot];
		}

		_repainting.Sort((a, b) => rows[a].MountIndex.CompareTo(rows[b].MountIndex));
		foreach (int slot in _repainting) {
			_paintedAt[slot] = ++_sequence;
		}

		for (int slot = 0; slot < _order.Length; slot++) {
			_order[slot] = slot;
		}

		// Insertion sort, stable and cheap for ten rows: ties (never-painted rows) keep slot order.
		for (int i = 1; i < _order.Length; i++) {
			int slot = _order[i];
			int j = i - 1;
			for (; j >= 0 && _paintedAt[_order[j]] > _paintedAt[slot]; j--) {
				_order[j + 1] = _order[j];
			}

			_order[j + 1] = slot;
		}

		return _order;
	}

	// The name changes only when the mount is destroyed, which swaps it for OFFLINE.
	private static bool Repaints(WeaponRowState previous, WeaponRowState now) =>
		now.Powered && now.Kind != WeaponMountKind.None
		&& (!previous.Powered || previous.Kind != now.Kind
			|| (previous.DropoutHidden && !now.DropoutHidden)
			|| previous.Selected != now.Selected || previous.InGroup != now.InGroup
			|| previous.Ready != now.Ready || previous.Name != now.Name);
}
