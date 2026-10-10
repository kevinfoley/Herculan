using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Render;

namespace Herculan.Engine.Input;

/// <summary>Which mouse buttons a cockpit event carries. Bit positions, so a mask holds both at once.</summary>
[Flags]
public enum CockpitMouseButtons {
	/// <summary>No button held.</summary>
	None = 0,

	/// <summary>The left button.</summary>
	Left = 1,

	/// <summary>The right button.</summary>
	Right = 2,
}

/// <summary>One completed click on a cockpit widget.</summary>
/// <param name="Id">Which widget was clicked.</param>
/// <param name="Button">Which button completed the click.</param>
/// <param name="ArtX">
/// Where the release landed, in the widget's own surface's art pixels. Only a widget that is a
/// <i>region</i> rather than a button cares — the command display's map, whose click resolves to a
/// gridpoint or to whatever unit is under the pointer.
/// </param>
/// <param name="ArtY">The same on y.</param>
/// <param name="DoubleClick">
/// Whether the release came within <see cref="CockpitInput.DoubleClickSeconds"/> of the same button's
/// previous release, wherever that one landed — <c>CockpitMouse_DoubleClick</c> (<c>0049dbe0</c>). The two
/// list shortcuts that press XMIT need it, and the MFD's XMIT ignores a press that carries it.
/// </param>
public readonly record struct CockpitClick(CockpitWidgetId Id, CockpitMouseButtons Button,
	float ArtX = 0f, float ArtY = 0f, bool DoubleClick = false);

/// <summary>
/// Where a captured pointer is on the widget it is dragging, in that surface's art pixels.
/// </summary>
/// <param name="Id">The widget holding the capture.</param>
/// <param name="Surface">The surface its rect is measured on.</param>
/// <param name="ArtX">Pointer x in that surface's art pixels — not clamped to the widget.</param>
/// <param name="ArtY">Pointer y in that surface's art pixels.</param>
/// <param name="Released">
/// Whether this is the release that ends the capture — where the original reads the widget's value
/// (<c>+0x10</c>) and commits it (<c>+8</c>). The throttle acts on every position; a charge bar only
/// on this one.
/// </param>
public readonly record struct CockpitDrag(CockpitWidgetId Id, CockpitSurface Surface,
	float ArtX, float ArtY, bool Released = false);

/// <summary>
/// The cockpit's mouse pipeline: raw window events in, completed widget clicks out.
/// Reverse-engineered from DBSIM — see docs/retail/simulation/cockpit-input.md, whose section numbers this
/// file's comments refer to.
///
/// <para><b>What is reproduced.</b> Events are queued as they arrive and processed once per frame
/// rather than handled in the callback (§3-4), because that is what keeps input aligned to the sim
/// tick. A <i>left</i> press arms a widget; a release completes the click only if it lands back on
/// the widget that was pressed (§7), however long the button was held. A right click arms nothing and
/// fires on whatever its release is over, which is the original's own asymmetry rather than a
/// simplification. A release that follows the same button's previous one inside
/// <see cref="DoubleClickSeconds"/> marks its click a <see cref="CockpitClick.DoubleClick"/> (§4).</para>
///
/// <para>A held button's widget draws depressed while the pointer is on it and pops back up when the
/// pointer leaves — see <see cref="Depressed"/>. There is deliberately <b>no hover state</b>: the
/// original has none, and the function long mistaken for one turns out to be this same press
/// tracking.</para>
///
/// <para><b>Drag capture</b> (§7) is reproduced, because one retail control does use it. A press on a
/// widget whose <c>+0x1d</c> flag is set — the slider base <c>004524a8</c> sets it, every button class
/// leaves it clear — latches the pointer to that widget until the button comes back up, and every
/// move in between is delivered to it wherever the pointer has got to. The throttle slider is the one such
/// widget a press can reach — the energy rows' charge bars carry the flag but lie wholly under their
/// rows' select gadgets — which is why it is the only one that can be dragged, unless
/// <see cref="Settings.TweakSettingDefinitions.ChargeBarPowerLevel"/> lists the bars first. Read §7
/// without tracing that flag to its one setter and the cockpit looks entirely undraggable.
/// A captured
/// release fires no click: the original's release path skips <c>Widget_OnMouseUp</c> entirely while
/// the capture flag is set.</para>
///
/// <para><b>What is not.</b> The Win32 hook chain and the two ten-slot subscriber tables the original
/// routes events through (§1-2) are a message-pump workaround with no purpose here. The cursor (§9) is
/// the host's.</para>
///
/// <para><b>Where the timing differs.</b> The original stamps each event as it arrives and debounces
/// pushes to one per ~16ms coarse tick; this stamps events with the frame they are processed in, and
/// does not debounce. At any sane frame rate the two resolutions are comparable, and stamping at
/// drain time keeps the whole class free of an ambient clock — the double-click window is then
/// measured in whole frames, which is also the granularity the original's own tick gives it in
/// practice.</para>
///
/// <para><b>The double-click flag lasts one click here.</b> The original's global is cleared only as
/// the queue drains its next record, so a key that presses the MFD's XMIT before then finds it still
/// set. Recorded as a divergence in KNOWN_ISSUES.md; the mechanism is
/// docs/retail/simulation/cockpit-input.md#4-once-per-frame-the-real-clickpressdrag-logic.</para>
/// </summary>
public sealed class CockpitInput {
	/// <summary>
	/// The original's double-click window, <c>CockpitMouse_DoubleClickTicks</c> (<c>004d1e70</c>) —
	/// <c>0x1e</c> coarse UI ticks of 16ms each, between a button's release and its previous one.
	/// </summary>
	public const float DoubleClickSeconds = (float)(30 * Cockpit.MessagePorts.CoarseTickSeconds);

	/// <summary>
	/// How many unprocessed events are kept before further ones are dropped. The original's queue caps
	/// at 99 and drops pushes past that; this is larger only because it does not debounce.
	/// </summary>
	public const int QueueCapacity = 256;

	private readonly Queue<Event> _queue = new();
	private CockpitMouseButtons _lastButtons;
	private float _elapsedSeconds;

	private CockpitWidgetId? _pressed;
	private CockpitSurface _pressedSurface;
	private bool _capturing;

	// Each button's previous release, 0049dbe4 and 0049dbe8.
	private float _leftReleasedAtSeconds = float.NegativeInfinity;
	private float _rightReleasedAtSeconds = float.NegativeInfinity;

	private readonly List<CockpitClick> _clicks = new();
	private readonly List<CockpitDrag> _drags = new();

	/// <summary>
	/// The widget a press is armed on, or null. Stays set while the pointer wanders off it — the
	/// original keeps the press armed and simply fires nothing if the release lands elsewhere.
	/// </summary>
	public CockpitWidgetId? Pressed => _pressed;

	/// <summary>
	/// The widget that should draw held-down right now: the armed widget while the pointer is actually
	/// on it, and null the moment the pointer leaves. Distinct from <see cref="Pressed"/>, which stays
	/// armed either way.
	///
	/// <para>This is <c>Widget_TrackPressedWidget</c> (<c>00452954</c>), which the symbol table long called
	/// <c>Widget_OnMouseHover</c> — a misreading. The function early-outs unless
	/// <c>Widget_PressedIndex</c> (<c>0049dbdc</c>), the globally remembered pressed-widget index, is valid, and that global is
	/// set only by <c>Widget_OnMouseDown</c> and cleared to -1 by <c>Widget_OnMouseUp</c>. It therefore
	/// cannot run unless a button is held, and what it does is toggle the held widget's state byte
	/// between 1 and 0 as the pointer moves on and off it, repainting each time: a button that pops
	/// back up when you drag off it and depresses again when you come back. <b>DBSIM has no hover state
	/// at all</b> — corroborated by <c>CockpitMouse_Init</c>'s event mask excluding plain movement
	/// (§3), which leaves nothing to drive one.</para>
	/// </summary>
	public CockpitWidgetId? Depressed { get; private set; }

	/// <summary>
	/// Every position a captured pointer reported during the last drain, oldest first — empty when
	/// nothing is being dragged. The capture opens on the press that starts it, because the original
	/// dispatches its drag handler straight from <c>Widget_OnMouseDown</c>: clicking anywhere on a
	/// slider's track jumps the knob there.
	///
	/// <para>The coordinates are the captured surface's art pixels and are <b>not</b> clamped to the
	/// widget: a capture follows the pointer off its own rect, off the surface, and off the window,
	/// which is what lets a drag past the end of a track pin it to that end.</para>
	///
	/// <para>The list is cleared and refilled by each drain, like the clicks it is returned beside.
	/// The release that ends a capture carries a position too, and it is reported: the original
	/// updates the widget from the pointer before it looks at the button.</para>
	/// </summary>
	public IReadOnlyList<CockpitDrag> Drags => _drags;

	/// <summary>
	/// Queues one mouse event, in window pixels with the origin at the top-left. Called straight from
	/// the host's mouse callbacks; does no hit-testing and touches no widget state.
	/// </summary>
	/// <param name="windowX">Pointer x in window pixels.</param>
	/// <param name="windowY">Pointer y in window pixels.</param>
	/// <param name="buttons">Every button held at this moment, not just the one that changed.</param>
	public void Enqueue(float windowX, float windowY, CockpitMouseButtons buttons) {
		if (_queue.Count >= QueueCapacity) {
			return;
		}

		_queue.Enqueue(new Event(windowX, windowY, buttons));
	}

	/// <summary>
	/// Drains a frame's queued events and returns the clicks they completed.
	///
	/// <para>Call once per frame, before the sim tick, so a click and the tick that consumes it stay
	/// in a fixed order. The returned list is reused between calls — copy it if it needs to outlive
	/// the frame.</para>
	///
	/// <para>Every event in one drain is hit-tested against the <paramref name="state"/> passed in, so
	/// a click that changes which widgets are visible does not affect a second click in the same
	/// frame. The original re-reads its list as handlers run and so would; with one pointer at frame
	/// rate, two clicks in one frame does not arise.</para>
	/// </summary>
	/// <param name="deltaSeconds">Real time since the previous drain, for the double-click window.</param>
	/// <param name="layout">This frame's placement, for window-to-art conversion.</param>
	/// <param name="art">The cockpit whose widgets to test against.</param>
	/// <param name="state">The HUD state deciding which widgets are visible, and so hit-testable.</param>
	public IReadOnlyList<CockpitClick> Drain(double deltaSeconds, CockpitScreenLayout layout,
			CockpitArt art, CockpitHudState state) {
		ArgumentNullException.ThrowIfNull(layout);
		ArgumentNullException.ThrowIfNull(art);

		return Drain(deltaSeconds, (x, y) => HitTest(layout, art, state, x, y),
			(surface, x, y) => layout.Surface(surface) is { } placed
				? placed.WindowToArt(x, y)
				: (x, y));
	}

	/// <summary>
	/// Drains a frame's queued events against an arbitrary hit test — the form the cockpit overload
	/// above is built on.
	///
	/// <para>The split is where the two halves of this pipeline meet: press/release edges and the
	/// double-click window are one problem, and "which widget is under this window point" is a wholly separate one
	/// owned by <see cref="CockpitScreenLayout"/> and <see cref="CockpitWidgets"/>. Keeping the seam
	/// open also lets the state machine be exercised without a loaded cockpit.</para>
	/// </summary>
	/// <param name="deltaSeconds">Real time since the previous drain, for the double-click window.</param>
	/// <param name="hitTest">Window x and y to the widget under them, or null for bare art.</param>
	/// <param name="toArt">
	/// Window x and y to a named surface's art pixels, unclamped — how a captured drag is positioned
	/// once the pointer has left the widget it grabbed. Null passes window pixels through unchanged,
	/// which is what a caller with no surfaces to speak of wants.
	/// </param>
	public IReadOnlyList<CockpitClick> Drain(double deltaSeconds, Func<float, float, CockpitWidget?> hitTest,
			Func<CockpitSurface, float, float, (float X, float Y)>? toArt = null) {
		ArgumentNullException.ThrowIfNull(hitTest);

		_elapsedSeconds += (float)Math.Max(deltaSeconds, 0d);
		_clicks.Clear();
		_drags.Clear();

		while (_queue.Count > 0) {
			var e = _queue.Dequeue();
			var hit = hitTest(e.X, e.Y);

			// A held button's widget depresses and pops back up as the pointer moves on and off it,
			// which is all Widget_TrackPressedWidget (00452954) does. Nothing happens here when no button is held: there is no
			// hover state to track, and nothing happens during a capture either — the mouse pump takes
			// the drag branch instead of calling Widget_TrackPressedWidget at all, so a captured widget stays down
			// however far the pointer wanders.
			if (!_capturing && _pressed is { } armed) {
				Depressed = hit is { } over && over.Id == armed && over.Surface == _pressedSurface
					? armed
					: null;
			}

			var pressedNow = e.Buttons & ~_lastButtons;
			var releasedNow = _lastButtons & ~e.Buttons;
			_lastButtons = e.Buttons;

			// Only the left button presses. CockpitMouse_ProcessQueue calls Widget_OnMouseDown for a left
			// press and not a right one, so a right press arms nothing and lights nothing, and a right
			// click works entirely off its own release below.
			if (pressedNow.HasFlag(CockpitMouseButtons.Left)) {
				OnPress(hit);
			}

			// A capture is fed on the press that started it and on every move after — the original's
			// mouse pump dispatches the drag handler from both places (004527a0 and 00452d18).
			if (_capturing && _pressed is { } dragged) {
				var (artX, artY) = toArt?.Invoke(_pressedSurface, e.X, e.Y) ?? (e.X, e.Y);
				_drags.Add(new CockpitDrag(dragged, _pressedSurface, artX, artY,
					Released: releasedNow != CockpitMouseButtons.None));
			}

			if (releasedNow != CockpitMouseButtons.None) {
				// A right release fires on whatever the release itself is over, so its coordinates
				// belong to that widget's surface rather than to the pressed one's.
				var surface = !_capturing && releasedNow.HasFlag(CockpitMouseButtons.Right) && hit is { } over
					? over.Surface
					: _pressedSurface;
				var (releaseX, releaseY) = toArt?.Invoke(surface, e.X, e.Y) ?? (e.X, e.Y);
				OnRelease(hit, releasedNow, releaseX, releaseY, DoubleClicked(releasedNow));
			}
		}

		return _clicks;
	}

	/// <summary>
	/// Forgets any in-flight press — for a lost window focus or a cockpit teardown, where a release
	/// will never arrive and an armed press would otherwise complete against whatever the pointer
	/// happened to be over on the way back.
	/// </summary>
	public void Reset() {
		_queue.Clear();
		_lastButtons = CockpitMouseButtons.None;
		_pressed = null;
		Depressed = null;
		_capturing = false;
		_drags.Clear();
	}

	/// <summary>
	/// Lets <paramref name="id"/> back up if the pointer is holding it down: a press flash on that button
	/// ending, which writes the state byte the held press set back to 0 (see
	/// <see cref="PressFlashes{TId}.Service"/>). The press stays armed, and the next pointer event over the
	/// button depresses it again, as <c>Widget_TrackPressedWidget</c> does.
	/// </summary>
	public void PopUp(CockpitWidgetId id) {
		if (!_capturing && Depressed == id) {
			Depressed = null;
		}
	}

	/// <summary>
	/// §7's <c>Widget_OnMouseDown</c>: a press on a widget arms it, and on a draggable one also latches
	/// the pointer capture.
	/// </summary>
	private void OnPress(CockpitWidget? hit) {
		if (hit is not { } widget) {
			_pressed = null;
			Depressed = null;
			_capturing = false;
			return;
		}

		_pressed = widget.Id;
		_pressedSurface = widget.Surface;
		Depressed = widget.Id;
		_capturing = widget.Draggable;
	}

	/// <summary>
	/// §4's double-click test on a release edge: each button that came up is measured against its own
	/// previous release, which this release then replaces. Either one inside the window sets the flag,
	/// captured release or not.
	/// </summary>
	private bool DoubleClicked(CockpitMouseButtons released) {
		bool doubleClick = false;
		if (released.HasFlag(CockpitMouseButtons.Left)) {
			doubleClick |= _elapsedSeconds - _leftReleasedAtSeconds < DoubleClickSeconds;
			_leftReleasedAtSeconds = _elapsedSeconds;
		}

		if (released.HasFlag(CockpitMouseButtons.Right)) {
			doubleClick |= _elapsedSeconds - _rightReleasedAtSeconds < DoubleClickSeconds;
			_rightReleasedAtSeconds = _elapsedSeconds;
		}

		return doubleClick;
	}

	/// <summary>
	/// §7's <c>Widget_OnMouseUp</c>. The release re-hit-tests, and what it does with the answer depends
	/// on which button came up:
	///
	/// <list type="bullet">
	/// <item><b>Left:</b> the click fires only if the release landed back on the widget the press
	/// armed — a button dragged off and let go does nothing.</item>
	/// <item><b>Right:</b> it fires on whatever the release is over, full stop. The original's second
	/// condition is the right-button bit of the click value word, which short-circuits the
	/// same-widget test; since a right press never armed anything in the first place, that bit is the
	/// whole of what makes a right click work.</item>
	/// </list>
	///
	/// <para>A captured widget ends its drag here and fires nothing — the original's release path
	/// takes the capture branch instead of the click one.</para>
	/// </summary>
	private void OnRelease(CockpitWidget? hit, CockpitMouseButtons released, float artX, float artY,
			bool doubleClick) {
		if (_capturing) {
			_capturing = false;
			_pressed = null;
			Depressed = null;
			return;
		}

		var armed = _pressed;
		_pressed = null;
		Depressed = null;

		if (hit is not { } widget) {
			return;
		}

		bool rightRelease = released.HasFlag(CockpitMouseButtons.Right);
		if (rightRelease || (widget.Id == armed && widget.Surface == _pressedSurface)) {
			_clicks.Add(new CockpitClick(widget.Id, released, artX, artY, doubleClick));
		}
	}

	// The system buttons go first because SystemButtons_Ctor (00434368) registers them before every
	// gauge, and the side strips after every art widget for the same reason the bottom one goes last in
	// CockpitWidgets.Visible: the original registers all three strips after the gauges. In a 4:3 window
	// the right-hand system button and the right strip contest a few columns at the top of the screen,
	// as they do in retail, and the button takes them.
	private static CockpitWidget? HitTest(CockpitScreenLayout layout, CockpitArt art,
			CockpitHudState state, float windowX, float windowY) =>
		SystemButtons.At(layout.WindowWidth, layout.WindowHeight, windowX, windowY)
		?? (layout.WindowToArt(windowX, windowY) is { } surfaceHit
			? CockpitWidgets.HitTest(art, state, surfaceHit.Surface, surfaceHit.ArtX, surfaceHit.ArtY)
			: null)
		?? layout.SideViewEdgeAt(windowX, windowY);

	/// <summary>One queued event: where the pointer was and everything held at the time (§3's record).</summary>
	private readonly record struct Event(float X, float Y, CockpitMouseButtons Buttons);
}
