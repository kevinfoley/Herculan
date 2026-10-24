using HercWorks.Core.Data.File;
using HercWorks.Core.Data.File.Dyn;
using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.WeaponRows;

/// <summary>The console's four-byte state block, <c>ConsoleButtons_StateBlock</c> (<c>000b13e8</c>), which the weapon manager's per-frame pass reads and writes back.</summary>
/// <param name="Chain">Bytes 0–1, the current chain, <c>int16</c>, 0–2.</param>
/// <param name="Tracking">Byte 2, the tracking latch.</param>
/// <param name="Link">Byte 3, the link request.</param>
public record struct Es1ConsoleState(short Chain, bool Tracking, bool Link);

/// <summary>
/// The console buttons, the cockpit's instrument <c>+0x1dd</c>, as <c>ConsoleButtons_Ctor</c> (<c>ES1DBSIM 00037bd0</c>)
/// builds it on the <c>.GAU</c> block at 452 (docs/es1/simulation/cockpit-weapon-gauges.md#the-console-buttons): five
/// buttons, <c>I</c>, <c>II</c> and <c>III</c> for the three fire chains, the current one lit, <c>LINK</c> and
/// <c>TRACK</c>; the current chain <c>+0x7a</c>, the tracking latch <c>+0x7c</c> and the link request <c>+0x7d</c>; on
/// <c>WeaponRangeGaugeVtable</c> (<c>00093aee</c>). What each button does goes through the weapon manager's per-frame
/// pass (docs/es1/simulation/cockpit-weapon-gauges.md#what-the-buttons-do).
/// </summary>
public sealed class Es1ConsoleButtons {
	/// <summary>The buttons, <c>+0x64</c>.</summary>
	public const int ButtonCount = 5;

	/// <summary>The three chain buttons' indices run 0–2; then these.</summary>
	public const int LinkButton = 3, TrackButton = 4;

	/// <summary><c>STRINGS0.STR</c>'s group of the five captions, <c>000bd2cc</c>.</summary>
	public const int CaptionGroup = 4;

	/// <summary>The codes <see cref="HandleCommand"/> takes, <c>ConsoleButtons_CommandCodes</c> (<c>00093b1c</c>): <c>`</c>, <c>Alt</c>+<c>`</c>, <c>Alt+T</c> and <c>L</c>.</summary>
	public const ushort NextChainKey = 0x29, PreviousChainKey = 0x229, TrackKey = 0x214, LinkKey = 0x26;

	private readonly Es1ConsoleButton[] _buttons = new Es1ConsoleButton[ButtonCount];

	/// <summary>
	/// <c>ConsoleButtons_Ctor</c>(0, <paramref name="cockpit"/>, <paramref name="block"/>): <c>PanelGauge_Ctor</c>'s rect,
	/// the block's first 16 bytes; with (<i>x</i>, <i>y</i>) its first two <c>int32</c>, each button <i>i</i> on the
	/// block's rect at <c>+0x10</c> + 16<i>i</i> moved by them, registered on the cockpit's root and given its index and a
	/// caption label in <c>cpgreen</c> centered over the rect; the tracking latch cleared. The chain and the link request
	/// are not written: 0 from the zero-filled heap.
	/// </summary>
	/// <param name="strings"><c>STRINGS0.STR</c>, whose group 4 the captions are; none when null.</param>
	internal Es1ConsoleButtons(Es1Cockpit cockpit, Es1GauConsoleButtonsBlock block, IReadOnlyList<HudFont?> fonts,
			IReadOnlyList<DynamixBitmap>? pushButtons, StringFile? strings) {
		Cockpit = cockpit;
		Rect = Es1Rect.From(block.Header);
		for (int i = 0; i < ButtonCount; i++) {
			var rect = Es1Rect.From(block.Buttons[i]).Offset(Rect.X0, Rect.Y0);
			var button = new Es1ConsoleButton(this, i, rect, fonts, pushButtons, strings?.Text(CaptionGroup, i));
			cockpit.Root.RegisterClickable(button);
			_buttons[i] = button;
		}
	}

	/// <summary><c>+0x10</c>, the cockpit.</summary>
	public Es1Cockpit Cockpit { get; }

	/// <summary><c>+0</c>–<c>+0xc</c>, the gauge base's rect.</summary>
	public Es1Rect Rect { get; }

	/// <summary><c>+0x14</c>–<c>+0x24</c>, the buttons in index order.</summary>
	public IReadOnlyList<Es1ConsoleButton> Buttons => _buttons;

	/// <summary><c>+0x7a</c>, the current chain, <c>int16</c>.</summary>
	public short Chain { get; private set; }

	/// <summary><c>+0x7c</c>, the tracking latch, which the gunsight shows as <c>ATT</c>.</summary>
	public bool Tracking { get; private set; }

	/// <summary><c>+0x7d</c>, the link request.</summary>
	public bool LinkRequest { get; private set; }

	/// <summary>
	/// <c>ConsoleButtons_SetChainNumber</c> (<c>00037ce4</c>)(console, <paramref name="chain"/>): the chain stored; for 0, 1
	/// or 2 that chain's button lit and the other two put out, each invalidated only when its lamp changes.
	/// </summary>
	public void SetChainNumber(short chain) {
		Chain = chain;
		if (chain is < 0 or > 2) {
			return;
		}

		for (int i = 0; i <= 2; i++) {
			bool lit = i == chain;
			if (_buttons[i].Lamp != lit) {
				_buttons[i].Lamp = lit;
				_buttons[i].Invalidate();
			}
		}
	}

	/// <summary><c>ConsoleButtons_ToggleAutoTrack</c> (<c>00037df8</c>): the tracking latch flipped, nothing else.</summary>
	public void ToggleAutoTrack() => Tracking = !Tracking;

	/// <summary><c>ConsoleButtons_SetLinkLatch</c> (<c>00037e0c</c>)(console, <paramref name="value"/>): the link request stored.</summary>
	public void SetLinkLatch(bool value) => LinkRequest = value;

	/// <summary><c>ConsoleButtons_GetStateBlock</c> (<c>00037ed4</c>): the chain, the tracking latch and the link request.</summary>
	public Es1ConsoleState GetStateBlock() => new(Chain, Tracking, LinkRequest);

	/// <summary>
	/// <c>ConsoleButtons_SetStateBlock</c> (<c>00037e78</c>)(console, <paramref name="block"/>): a chain other than the
	/// console's stored and handed to <see cref="SetChainNumber"/>; a tracking byte other than the latch flips it; a link
	/// byte other than the request stored.
	/// </summary>
	public void SetStateBlock(Es1ConsoleState block) {
		if (block.Chain != Chain) {
			Chain = block.Chain;
			SetChainNumber(block.Chain);
		}

		if (block.Tracking != Tracking) {
			ToggleAutoTrack();
		}

		if (block.Link != LinkRequest) {
			SetLinkLatch(block.Link);
		}
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>ConsoleButtons_OnChildClick</c> (<c>00037f00</c>)(console, flags, <paramref name="button"/>):
	/// for a chain button, the other two chains' lamps put out, each that was lit invalidated, and its index the chain;
	/// for <c>LINK</c>, the link request set; for <c>TRACK</c>, <see cref="ToggleAutoTrack"/>.
	/// </summary>
	public void OnChildClick(Es1ConsoleButton button) {
		switch (button.Index) {
			case <= 2:
				for (int i = 0; i <= 2; i++) {
					if (i != button.Index && _buttons[i].Lamp) {
						_buttons[i].Lamp = false;
						_buttons[i].Invalidate();
					}
				}

				Chain = (short)button.Index;
				break;
			case LinkButton:
				SetLinkLatch(true);
				break;
			case TrackButton:
				ToggleAutoTrack();
				break;
		}
	}

	/// <summary>Slot <c>+8</c>, <c>ConsoleButtons_Paint</c> (<c>00037e20</c>): each button's paint.</summary>
	public void Paint() {
		foreach (var button in _buttons) {
			button.Paint();
		}
	}

	/// <summary>Slot <c>+0xc</c>, <c>ConsoleButtons_Update</c> (<c>00037e4c</c>): each button's per-frame slot.</summary>
	public void Update() {
		foreach (var button in _buttons) {
			button.Tick();
		}
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>ConsoleButtons_HandleCommand</c> (<c>00038034</c>)(console, <paramref name="code"/>), the
	/// cockpit's key chain reaching it: <c>`</c> the next chain, 2 wrapping to 0, and <c>Alt</c>+<c>`</c> the previous, 0
	/// wrapping to 2, through <see cref="SetChainNumber"/>; <c>Alt+T</c> presses <c>TRACK</c>, its click with the left
	/// button unless its state is 2 or 3; <c>L</c> <c>WeaponMounts_ToggleLink</c> (<c>0000d41c</c>) on the manager of
	/// <c>CockpitViewInstance</c>'s machine at once. Answers 1 for these codes and 0 for any other.
	/// </summary>
	public short HandleCommand(ushort code) {
		switch (code) {
			case NextChainKey:
				SetChainNumber((short)(Chain + 1 > 2 ? 0 : Chain + 1));
				return 1;
			case PreviousChainKey:
				SetChainNumber((short)(Chain - 1 < 0 ? 2 : Chain - 1));
				return 1;
			case TrackKey:
				var track = _buttons[TrackButton];
				if (track.State is not (Es1Widget.Hidden or Es1ConsoleButton.Disabled)) {
					track.OnClick(1);
				}

				return 1;
			case LinkKey:
				Cockpit.Machine?.Mounts?.ToggleLink();
				// PLACEHOLDER: Sound_Play(0x11); no plan step ports ES1's sound yet.
				return 1;
			default:
				return 0;
		}
	}
}

/// <summary>
/// A console button, <c>WeaponRangeSelectGadget</c> (docs/es1/simulation/cockpit-weapon-gauges.md#the-console-button--weaponrangeselectgadget):
/// a captioned box, a chain button its caption lit while its chain is the current one, <c>LINK</c> and <c>TRACK</c> a
/// <c>pbuttons</c> picture that shows the press. Its slots are <c>WeaponRangeSelectGadgetVtable</c> (<c>00093db6</c>)'s.
/// </summary>
public sealed class Es1ConsoleButton : Es1ButtonWidget {
	/// <summary>The state, besides <see cref="Es1Widget.Hidden"/>, that a key's press skips.</summary>
	public const short Disabled = 3;

	/// <summary><c>pbuttons</c>' frame <c>LINK</c> and <c>TRACK</c> draw up; pressed is the next.</summary>
	public const int UpFrame = 6;

	private readonly Es1ConsoleButtons _console;
	private readonly IReadOnlyList<HudFont?> _fonts;
	private readonly IReadOnlyList<DynamixBitmap>? _pushButtons;

	/// <summary>
	/// <c>WeaponRangeSelectGadget_Ctor</c> (<c>00038ce4</c>)(0, <paramref name="console"/>, <paramref name="rect"/>) with
	/// what <c>ConsoleButtons_Ctor</c> gives it after: the state 0, the lamp out, the caption's label in <c>cpgreen</c>
	/// centered over the rect, and the index.
	/// </summary>
	/// <param name="caption"><c>STRINGS0.STR</c> group 4's entry <paramref name="index"/>.</param>
	internal Es1ConsoleButton(Es1ConsoleButtons console, int index, Es1Rect rect, IReadOnlyList<HudFont?> fonts,
			IReadOnlyList<DynamixBitmap>? pushButtons, string? caption) {
		_console = console;
		_fonts = fonts;
		_pushButtons = pushButtons;
		Index = index;
		Caption = caption;
		Rect = rect;
		Label = new Es1Label(Es1WeaponGauge.Font(fonts, Es1CockpitViews.CpGreen));
		Label.SetRect(Es1Label.Centered, rect);
	}

	/// <summary><c>+0x28</c>, the index, 0–4.</summary>
	public int Index { get; }

	/// <summary>The caption, <c>STRINGS0.STR</c> group 4's entry <see cref="Index"/>: <c>I</c>, <c>II</c>, <c>III</c>, <c>LINK</c>, <c>TRACK</c>.</summary>
	public string? Caption { get; }

	/// <summary><c>+0x2c</c>, the caption's label.</summary>
	public Es1Label Label { get; }

	/// <summary><c>+0x3c</c>, the lamp; only the chain buttons' is ever lit.</summary>
	public bool Lamp { get; set; }

	/// <summary><c>+0x1b</c>, the paint counter the invalidate sets to 2.</summary>
	public byte PaintCounter { get; private set; }

	/// <summary>
	/// Slot <c>+4</c>, <c>ConsoleButton_Paint</c> (<c>00038d90</c>): when <c>CockpitView_RectCornerVisible</c> passes the
	/// rect, the caption written: a chain button's on a plate of <see cref="Es1WeaponGauge.DarkPlate"/>, in <c>cpylw</c>
	/// while lit and <c>cpblue</c> while not; <c>LINK</c>'s and <c>TRACK</c>'s over <c>pbuttons</c>' frame
	/// <see cref="UpFrame"/> + state blitted at the rect's top-left, in <c>cppress</c> while lit and <c>cpon</c> while not.
	/// </summary>
	public override void Paint() {
		var cockpit = _console.Cockpit;
		if (!cockpit.RectCornerVisible(Rect)) {
			return;
		}

		var canvas = cockpit.PushCanvasContext();
		if (Index <= 2) {
			Label.FillColor = Es1WeaponGauge.DarkPlate;
			Label.Font = Es1WeaponGauge.Font(_fonts, Lamp ? Es1CockpitViews.CpYellow : Es1CockpitViews.CpBlue);
		} else {
			int frame = UpFrame + State;
			canvas.Blit(_pushButtons != null && frame < _pushButtons.Count ? _pushButtons[frame] : null, Rect.X0, Rect.Y0);
			Label.Font = Es1WeaponGauge.Font(_fonts, Lamp ? Es1CockpitViews.CpPress : Es1CockpitViews.CpOn);
		}

		Label.SetText(canvas, Caption);
	}

	/// <summary>Slot <c>+8</c>, <c>Widget_InvalidateDeferredWeaponGaugeCopy</c> (<c>00038234</c>): the paint counter set to 2.</summary>
	public override void Invalidate() => PaintCounter = 2;

	/// <summary>
	/// Slot <c>+0xc</c>, <c>WeaponRangeSelectGadget_OnClick</c> (<c>00038ec0</c>)(button, <paramref name="value"/>): with
	/// bit 0 set (the left button), a chain button's lamp lit, the mixin's click sound, and the console's
	/// <see cref="Es1ConsoleButtons.OnChildClick"/>.
	/// </summary>
	public override void OnClick(short value) {
		if ((value & 1) == 0) {
			return;
		}

		if (Index <= 2) {
			Lamp = true;
		}

		// PLACEHOLDER: Widget_ClickSound, Sound_Play(0x11); no plan step ports ES1's sound yet.
		_console.OnChildClick(this);
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>WeaponRangeSelectGadget_FlushDeferredPaint</c> (<c>00038e88</c>): at a paint counter of 2 the
	/// paint, at 1 the rect copied from the page on the screen to the page drawn, each lowering it by 1.
	/// </summary>
	public override void Tick() {
		if (PaintCounter == 2) {
			Paint();
			PaintCounter--;
		} else if (PaintCounter == 1) {
			_console.Cockpit.DrawToCockpit(Rect, 0);
			PaintCounter--;
		}
	}
}
