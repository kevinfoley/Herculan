using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Es1.Input;
using Herculan.Es1.Panels;
using Herculan.Es1.View;
using Herculan.Es1.WeaponRows;

namespace Herculan.Es1.Gunsight;

/// <summary>
/// The roving gunsight, the cockpit's instrument <c>+0x1f1</c>, as <c>Gau_RovingGunsightWidget</c> (<c>ES1DBSIM
/// 00034aec</c>) builds it on the <c>.GAU</c> block at 1104 (docs/es1/simulation/cockpit-gunsight-hud.md#building-the-gunsight):
/// the gauge base's rect and cockpit, its state block <c>+0x7a</c>, its eight children <c>+0xa0</c> and the <c>ATT</c>
/// label <c>+0xcc</c>, with the slots of <c>HUDVtable</c> (<c>00093842</c>) the cockpit and the frame run: the paint
/// <c>+8</c> and the per-frame update <c>+0xc</c> (docs/es1/simulation/cockpit-gunsight-hud.md#painting-and-the-per-frame-update).
/// </summary>
public sealed class Es1Gunsight {
	/// <summary>The child array's slots, <c>+0xa0</c>–<c>+0xc4</c>.</summary>
	public const int ChildCapacity = 10;

	/// <summary>The limits each slide bar's set-limits is given, ± these.</summary>
	public const short HeadingLimit = 0xe38, RotationLimit = 0x38e3, VerticalLimit = 0x1f40;

	/// <summary>The <c>ATT</c> label's fill colour <c>+0x1c</c>.</summary>
	public const int AttFillColor = 0x38;

	/// <summary>The value the update stores at the state block's offset <c>0x20</c> with a selection.</summary>
	public const int SelectionLiteral = 2000;

	/// <summary><c>STRINGS0.STR</c>'s group of <c>ATT</c>, <c>000bd4a8</c>.</summary>
	public const int AttGroup = 33;

	private readonly string? _attText;

	private readonly Es1Cockpit _cockpit;
	private readonly List<Es1GunsightChild> _children = new(ChildCapacity);

	/// <summary>
	/// <c>Gau_RovingGunsightWidget</c>(0, <paramref name="cockpit"/>, <paramref name="block"/>): <c>PanelGauge_Ctor</c>'s
	/// rect, the block's first 16 bytes; the children in order, each rect but the reticle's centre moved by the block's
	/// origin <i>o</i>, with the pointer put on the reticle's centre <i>p</i> between children 0 and 1; the <c>ATT</c>
	/// label; and the state block and the condition zeroed.
	/// </summary>
	/// <param name="fonts"><c>ColorSchemePanels</c> (<c>00092d40</c>), whose <c>cpgreen</c> captions the waypoint and <c>cpblack</c> writes <c>ATT</c>.</param>
	/// <param name="mouse">The cockpit's mouse, whose cursor and driver the pointer's move reaches.</param>
	/// <param name="attText"><c>STRINGS0.STR</c> group 33's <c>ATT</c>; nothing is written when null.</param>
	internal Es1Gunsight(Es1Cockpit cockpit, Es1GauGunsightBlock block, Es1GunsightBanks banks, IReadOnlyList<HudFont?> fonts,
			Es1CockpitMouse mouse, string? attText = null) {
		_cockpit = cockpit;
		_attText = attText;
		Rect = new Es1Rect(block.Origin.X, block.Origin.Y, block.Int2, block.Int3);
		int ox = block.Origin.X, oy = block.Origin.Y;
		var p = block.ReticleCentre;

		ClickSurface = new Es1GunsightClickSurface(cockpit, Es1Rect.From(block.ClickSurface).Offset(ox, oy), this);
		AddChild(ClickSurface);

		// Cursor_SyncPosition(p); Mouse_SetPosition(p) when there is a mouse device; and Mouse_PostEvent({2p.x, p.y, 0}, 1),
		// a move with no button down, which leaves the driver's pointer at p. This engine's driver keeps the screen's
		// columns undoubled, so the event carries p.x; and the driver is the device, so with none there is no handler
		// for the event to reach.
		mouse.Cursor.SyncPosition(p.X, p.Y);
		if (mouse.Driver is { } driver) {
			driver.SetPosition(p.X, p.Y);
			driver.PostEvent(new Es1MouseRecord(p.X, p.Y, 0), Es1HostMouse.Moved);
		}

		var tape = Es1Rect.From(block.HeadingTape).Offset(ox, oy);
		HeadingTape = new Es1HudHeadingTape(cockpit, banks, tape);
		AddChild(HeadingTape);
		HeadingTape.SetLimits(HeadingLimit, -HeadingLimit);

		RotationIndicator = new Es1HudRotationIndicator(cockpit, banks,
			new Es1Rect(tape.X0 + 15, tape.Y0 + 10, tape.X1 - 15, tape.Y1 + 10));
		AddChild(RotationIndicator);
		RotationIndicator.SetLimits(RotationLimit, -RotationLimit);

		VerticalBar = new Es1HudSlideBarV(cockpit, banks, Es1Rect.From(block.SlideBar).Offset(ox, oy));
		AddChild(VerticalBar);
		VerticalBar.SetLimits(VerticalLimit, -VerticalLimit);

		int s = block.ReticleHalfSize;
		var reticle = new Es1Rect(p.X - s + ox, p.Y - s + oy, p.X + s + ox, p.Y + s + oy);
		Reticle = new Es1GunsightReticle(cockpit, reticle) { CenterX = p.X, CenterY = p.Y };
		AddChild(Reticle);
		TargetIndicator = new Es1GunsightTargetIndicator(cockpit, reticle) { State = State };
		AddChild(TargetIndicator);
		UnusedChild = new Es1GunsightUnusedChild(cockpit, reticle) { State = State };
		AddChild(UnusedChild);

		WaypointIndicator = new Es1HudWaypointIndicator(cockpit, banks, tape, Font(fonts, Es1CockpitViews.CpGreen));
		AddChild(WaypointIndicator);
		WaypointIndicator.SetLimits(HeadingLimit, -HeadingLimit);

		// About the heading tape's top-left, child 1's rect.
		var corner = HeadingTape.Rect;
		AttLabel = new Es1Label(Font(fonts, Es1CockpitViews.CpBlack)) { FillColor = AttFillColor };
		AttLabel.SetRect(Es1Label.Centered, new Es1Rect(corner.X0 - 3, corner.Y0 + 10, corner.X0 + 12, corner.Y0 + 15));
	}

	/// <summary><c>+0</c>–<c>+0xc</c>, the gauge's rect, which the gunsight drag tests the pointer against.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x68</c>, the condition, 0 intact to 4 destroyed.</summary>
	public short Condition { get; private set; }

	/// <summary><c>+0x74</c>, set while the display is dark. PLACEHOLDER: the sensor dropout, plan step 7b, never sets it.</summary>
	public bool Dark { get; private set; }

	/// <summary><c>+0x7a</c>, the state block.</summary>
	public Es1GunsightState State { get; } = new();

	/// <summary><c>+0xa0</c>, the children in the order built, their count <c>+0xc8</c>.</summary>
	public IReadOnlyList<Es1GunsightChild> Children => _children;

	/// <summary>Child 0, the click surface.</summary>
	public Es1GunsightClickSurface ClickSurface { get; }

	/// <summary>Child 1, the compass.</summary>
	public Es1HudHeadingTape HeadingTape { get; }

	/// <summary>Child 2, the horizontal bar, which tracks the turret's twist.</summary>
	public Es1HudRotationIndicator RotationIndicator { get; }

	/// <summary>Child 3, the vertical bar, which tracks the turret's pitch.</summary>
	public Es1HudSlideBarV VerticalBar { get; }

	/// <summary>Child 4, the reticle.</summary>
	public Es1GunsightReticle Reticle { get; }

	/// <summary>Child 5, the target box.</summary>
	public Es1GunsightTargetIndicator TargetIndicator { get; }

	/// <summary>Child 6, the unused child.</summary>
	public Es1GunsightUnusedChild UnusedChild { get; }

	/// <summary>Child 7, the waypoint indicator.</summary>
	public Es1HudWaypointIndicator WaypointIndicator { get; }

	/// <summary><c>+0xcc</c>, the <c>ATT</c> label.</summary>
	public Es1Label AttLabel { get; }

	/// <summary><c>Gunsight_PickArmed</c> (<c>000936d8</c>), a global byte the click surface's gadget sets.</summary>
	public bool PickArmed { get; set; }

	/// <summary><c>Gunsight_GetStateBlock</c> (<c>00035180</c>): the gunsight's own block.</summary>
	public Es1GunsightState GetStateBlock() => State;

	/// <summary>
	/// <c>Gunsight_SetValues</c> (<c>00035090</c>)(gunsight, <paramref name="block"/>): for each of offsets 0, 2 and 4
	/// that differs from the gunsight's, the new value kept and its delta handed to the add-delta slot <c>+0x10</c>:
	/// offset 0's new − old to the heading tape and the waypoint indicator, offset 2's old − new to the rotation
	/// indicator, offset 4's old − new to the vertical bar; then the block copied over the gunsight's.
	/// </summary>
	public void SetValues(Es1GunsightState block) {
		if (block.Heading != State.Heading) {
			short delta = (short)(block.Heading - State.Heading);
			State.Heading = block.Heading;
			HeadingTape.AddDelta(delta);
			WaypointIndicator.AddDelta(delta);
		}

		if (block.Twist != State.Twist) {
			short delta = (short)(State.Twist - block.Twist);
			State.Twist = block.Twist;
			RotationIndicator.AddDelta(delta);
		}

		if (block.Pitch != State.Pitch) {
			short delta = (short)(State.Pitch - block.Pitch);
			State.Pitch = block.Pitch;
			VerticalBar.AddDelta(delta);
		}

		State.CopyFrom(block);
	}

	/// <summary>
	/// <c>Gunsight_Paint</c> (<c>00034ec4</c>), slot <c>+8</c>, which <c>CockpitView_InvalidateWidgets</c> runs: unless the
	/// display is dark, each child's paint, then the <c>ATT</c> legend.
	/// </summary>
	public void Paint() {
		if (Dark) {
			return;
		}

		foreach (var child in _children) {
			child.Paint();
		}

		PaintAttLegend();
	}

	/// <summary>
	/// <c>Gunsight_UpdateAndPaint</c> (<c>00034f3c</c>), slot <c>+0xc</c>, once a frame after the 3D pass: nothing in view
	/// 4; the condition and the dropout; then, unless the display is dark or <c>CockpitView_RectCornerVisible</c> refuses
	/// child 0's rect, the pick handed on, the cockpit's selection into the block, each child's repaint and the
	/// <c>ATT</c> legend.
	/// </summary>
	public void UpdateAndPaint() {
		if (_cockpit.CurrentView == Es1CockpitViews.ExternalView) {
			return;
		}

		// PLACEHOLDER: Player_DependentCondition(4) stored at +0x68 and, when it is not 0, PanelGauge_TickDropout, damage
		// and the sensor dropout of plan step 7b; until then the condition is 0 and the display never goes dark.
		Condition = 0;
		if (Dark || !_cockpit.RectCornerVisible(ClickSurface.Rect)) {
			return;
		}

		// 1. Once the 3D pass has answered the pick, the object it found, if any, to TargetSelect_SetObject.
		var pick = _cockpit.Globals.ObjectPick;
		if (PickArmed && pick.IsIdle) {
			if (pick.GetObject() is { } picked) {
				_cockpit.Targets.SetObject(picked);
			}

			PickArmed = false;
		}

		// 2. The cockpit's selection +0x20c at offset 6, and with one its position, its shape's radius, 2000 and its heading;
		// with none the block keeps the last selection's.
		State.Selection = _cockpit.Selection;
		if (State.Selection is { } selection) {
			State.SelectionPosition = selection.Position;
			State.SelectionRadius = selection.ShapeRadius;
			State.Literal2000 = SelectionLiteral;
			State.SelectionHeading = selection.Heading;
		}

		// 3.
		foreach (var child in _children) {
			child.Repaint();
		}

		PaintAttLegend();
	}

	/// <summary>
	/// Slot <c>+0x10</c> of <c>HUDVtable</c>, <c>PanelGauge_KeyDispatchNoneGunsightCopy</c> (<c>00034e8b</c>)(gunsight,
	/// code): 0, taking no key.
	/// </summary>
	public static short KeyDispatch(ushort code) {
		_ = code;
		return 0;
	}

	/// <summary><c>Gunsight_AddChild</c> (<c>00034e94</c>): a child appended.</summary>
	private void AddChild(Es1GunsightChild child) => _children.Add(child);

	/// <summary>
	/// The <c>ATT</c> legend both paints end with: while the console buttons' tracking latch <c>+0x7c</c> (the cockpit's
	/// <c>+0x1dd</c>, <see cref="Es1ConsoleButtons.Tracking"/>) is set, <c>Label_SetText</c> of <see cref="AttLabel"/>
	/// with <c>ATT</c> in the render context installed then, the scene's
	/// (docs/es1/simulation/cockpit-gunsight-hud.md#what-the-box-is-drawn-through).
	/// </summary>
	private void PaintAttLegend() {
		if (_cockpit.ConsoleButtons.Tracking) {
			AttLabel.SetText(_cockpit.SceneCanvas(), _attText);
		}
	}

	private static HudFont? Font(IReadOnlyList<HudFont?> fonts, int index) => index < fonts.Count ? fonts[index] : null;
}
