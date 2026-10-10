using Herculan.Engine.Cockpit;

namespace Herculan.Engine.Input;

/// <summary>
/// One widget root's press flash: a button pressed for the player rather than by the pointer shows pressed for
/// <see cref="FlashTicks"/> coarse ticks. <c>WidgetRoot_FlashPress</c> (<c>00453078</c>),
/// <c>WidgetRoot_ServicePressFlashes</c> (<c>004530b8</c>) and <c>WidgetRoot_PressFlashCount</c>
/// (<c>00453160</c>); see docs/retail/simulation/cockpit-input.md#the-press-flash.
///
/// <para>The cockpit keeps one, keyed on <see cref="CockpitWidgetId"/> (see the host's
/// <c>CockpitDisplays.FlashPress</c>), and each alert panel keeps its own, keyed on the panel's widget index
/// (<see cref="AlertPanelPresses"/>), as each panel in the original has a root of its own.</para>
///
/// <para>The original's list holds eight entries with no bound check; this one is unbounded.</para>
/// </summary>
/// <typeparam name="TId">How the owner names a widget.</typeparam>
public sealed class PressFlashes<TId> where TId : notnull {
	/// <summary>How long a flash holds a button down: the deadline is <c>Time_GetCoarseTicks() + 10</c>.</summary>
	public const int FlashTicks = 10;

	private readonly List<(TId Id, long Deadline)> _entries = new();
	private readonly List<TId> _popped = new();
	private bool _queued;

	/// <summary>
	/// The buttons showing pressed as of the last <see cref="Service"/>. Replaced rather than changed in place,
	/// so a holder of it keeps the set it was built with.
	/// </summary>
	public IReadOnlyList<TId> Lit { get; private set; } = Array.Empty<TId>();

	/// <summary><c>WidgetRoot_PressFlashCount</c>: how many entries the list holds, expired ones included until <see cref="Service"/> drops them.</summary>
	public int Count => _entries.Count;

	/// <summary>
	/// <c>WidgetRoot_FlashPress</c>: queues <paramref name="id"/> to show pressed until <see cref="FlashTicks"/>
	/// after <paramref name="nowTicks"/>. A button already flashing gets a second entry, as in the original.
	/// </summary>
	public void Flash(TId id, long nowTicks) {
		_entries.Add((id, nowTicks + FlashTicks));
		_queued = true;
	}

	/// <summary>
	/// <c>WidgetRoot_ServicePressFlashes</c>: drops every entry whose deadline has come and rebuilds
	/// <see cref="Lit"/> from the rest.
	///
	/// <para>The original sets an entry's button to state 1 while it lasts and to 0 when it expires, so a
	/// button the pointer was holding down pops up at its flash's deadline. The buttons that popped are
	/// returned for the caller to take off the pointer's press: every one that expired this pass, less any a
	/// later entry keeps lit.</para>
	/// </summary>
	/// <param name="nowTicks"><c>Time_GetCoarseTicks</c>, on the clock <see cref="Flash"/> was given.</param>
	public IReadOnlyList<TId> Service(long nowTicks) {
		_popped.Clear();
		for (int i = _entries.Count - 1; i >= 0; i--) {
			if (nowTicks >= _entries[i].Deadline) {
				_popped.Add(_entries[i].Id);
				_entries.RemoveAt(i);
			}
		}

		if (_queued || _popped.Count > 0) {
			Lit = _entries.Count == 0
				? Array.Empty<TId>()
				: _entries.Select(entry => entry.Id).Distinct().ToArray();
			_queued = false;
		}

		_popped.RemoveAll(Lit.Contains);
		return _popped;
	}

	/// <summary>Forgets every entry, for an alert panel coming down.</summary>
	public void Clear() {
		_entries.Clear();
		_popped.Clear();
		_queued = false;
		Lit = Array.Empty<TId>();
	}
}
