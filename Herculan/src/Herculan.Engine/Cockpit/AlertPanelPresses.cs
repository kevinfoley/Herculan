using Herculan.Engine.Input;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// What one alert panel keeps for the widgets a key or a stick presses: its own press-flash list, which the
/// original keeps on the panel's widget root at <c>panel+0x285</c>, and the close flag's hold that
/// <c>AlertPanel_Present</c> (<c>00454ab0</c>) puts on it while a flash is still queued. See
/// docs/retail/simulation/alert-panels.md#keys-and-the-press-flash.
///
/// <para>A panel's widgets are named by their index in the panel's own widget list (<c>panel+0x291</c>).</para>
///
/// <para>The clock is <c>Time_GetCoarseTicks</c>' wall time, which keeps running under a panel; the caller
/// passes it, and it must not be one that a panel stops.</para>
/// </summary>
public sealed class AlertPanelPresses {
	private readonly PressFlashes<int> _flashes = new();
	private readonly HashSet<int> _restedByFlash = new();
	private bool _closeFlag;
	private bool _closeHeld;

	/// <summary>Whether a flash is holding <paramref name="widget"/> down.</summary>
	public bool IsLit(int widget) => _flashes.Lit.Contains(widget);

	/// <summary>
	/// Whether a flash ending left <paramref name="widget"/> in state 0, the plain button's look, where a
	/// preferences or controls option row otherwise rests in state 3. The service writes 0 whatever the
	/// widget's state was before, and only the panel's highlight logic writes another state over it — see
	/// <see cref="Restate"/>.
	/// </summary>
	public bool RestedByFlash(int widget) => _restedByFlash.Contains(widget);

	/// <summary>
	/// The panel's highlight logic has written <paramref name="widget"/>'s state itself, 0 or 3, over whatever
	/// a flash's end left there.
	/// </summary>
	public void Restate(int widget) => _restedByFlash.Remove(widget);

	/// <summary>
	/// <c>WidgetRoot_FlashPress</c> on the panel's root — what <c>AlertPanel_PressWidget</c> (<c>00454dcc</c>)
	/// does after the widget's click, and what <c>ControlsPanel_HandleEvent</c> (<c>00458f9c</c>) does for the
	/// row a stick button picks.
	/// </summary>
	public void Flash(int widget, long nowTicks) => _flashes.Flash(widget, nowTicks);

	/// <summary>
	/// <c>WidgetRoot_ServicePressFlashes</c> on the panel's root, once a pass of the panel's loop. Returns the
	/// widgets whose flash ended this pass, which the service has just put in state 0.
	/// </summary>
	public IReadOnlyList<int> Service(long nowTicks) {
		var ended = _flashes.Service(nowTicks);
		foreach (int widget in ended) {
			_restedByFlash.Add(widget);
		}

		return ended;
	}

	/// <summary>The panel's close flag, <c>panel+0x2d1</c>: a click that takes the panel down sets it.</summary>
	public void RequestClose() => _closeFlag = true;

	/// <summary>
	/// <c>AlertPanel_Present</c>'s hold, run after <see cref="Service"/>: while the close flag is set and a
	/// flash is still queued, the flag moves to <c>panel+0x331</c> and the loop runs one more pass; on that
	/// pass it moves back. So a key that takes a panel down shows its button pressed for one pass before the
	/// panel goes. Returns whether the panel closes at the end of this pass.
	/// </summary>
	public bool HoldClose() {
		if (_closeFlag && _flashes.Count != 0) {
			_closeHeld = true;
			_closeFlag = false;
		} else if (_closeHeld) {
			_closeFlag = true;
			_closeHeld = false;
		}

		return _closeFlag;
	}

	/// <summary>Whether the close flag is set — <c>PreferencesPanel_Present</c> (<c>00457180</c>) has no hold, so that panel closes on it at once.</summary>
	public bool CloseRequested => _closeFlag;

	/// <summary>Forgets the flashes and the close flag, for a panel going up or coming down.</summary>
	public void Reset() {
		_flashes.Clear();
		_restedByFlash.Clear();
		_closeFlag = false;
		_closeHeld = false;
	}
}
