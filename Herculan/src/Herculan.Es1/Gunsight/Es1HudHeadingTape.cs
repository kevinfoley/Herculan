using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Gunsight;

/// <summary>
/// The compass, <c>HudHeadingTape_Ctor</c> (<c>ES1DBSIM 00033c20</c>): a <c>0x54</c>-byte rotation indicator on
/// <c>HRevRotationGaugeVtable</c> (<c>0009374e</c>) that slides two frames of <see cref="Es1GunsightBanks.HTick"/>
/// through its rect (docs/es1/simulation/cockpit-gunsight-hud.md#the-heading-tape). Its add-delta slot is the set-heading,
/// which reads the cockpit's machine rather than the delta.
/// </summary>
public sealed class Es1HudHeadingTape : Es1HudRotationIndicator {
	/// <summary>The rotation indicator's constructor, then <c>HudHeadingTape_Recompute</c> directly.</summary>
	public Es1HudHeadingTape(Es1Cockpit cockpit, Es1GunsightBanks banks, Es1Rect rect) : base(cockpit, banks, rect) {
		RecomputeTape();
	}

	/// <summary><c>+0x40</c>, <i>P</i>: frames × width × <c>0x400</c> / <c>0x10000</c>.</summary>
	public int FramePixels { get; private set; }

	/// <summary><c>+0x44</c>, the column the left frame is blitted at.</summary>
	public int StripX { get; private set; }

	/// <summary><c>+0x48</c>, the left frame.</summary>
	public DynamixBitmap? LeftFrame { get; private set; }

	/// <summary><c>+0x4c</c>, the frame a width further right.</summary>
	public DynamixBitmap? RightFrame { get; private set; }

	/// <summary>The bank's frame count, <i>F</i>; 0 with no bank.</summary>
	private int FrameCount => Banks.HTick?.Count ?? 0;

	/// <summary><c>HudHeadingTape_Recompute</c> (<c>00033c7c</c>).</summary>
	public override void Recompute() => RecomputeTape();

	/// <summary>
	/// <c>HudHeadingTape_SetLimits</c> (<c>00033eac</c>)(tape, <paramref name="max"/>, <paramref name="min"/>): (max − min) /
	/// width at <c>+0x40</c>, then the bar's set-limits, whose recompute writes <i>P</i> back. With a width of 0, where
	/// retail divides by zero, the store is skipped: an engine guard no retail layout reaches.
	/// </summary>
	public override void SetLimits(short max, short min) {
		if (Travel != 0) {
			FramePixels = (max - min) / Travel;
		}

		base.SetLimits(max, min);
	}

	/// <summary>
	/// <c>HudHeadingTape_SetHeading</c> (<c>00033d08</c>), the add-delta slot: <paramref name="delta"/> unread, the strip
	/// placed for the cockpit's machine's heading <i>h</i> = −<c>+0xa</c> as an unsigned 16-bit value, with <i>W</i> the
	/// width and <i>F</i> the frame count: <i>q</i> = (<i>h</i> × <i>P</i>) &gt;&gt; 10, the frame <i>q</i> / <i>W</i> and
	/// the offset <i>q</i> mod <i>W</i> plus ((<i>h</i> &gt;&gt; 8) × ((<i>P</i> × <c>0xe39</c>) &gt;&gt; 10)) &gt;&gt; 8;
	/// an offset of <i>W</i> or more takes <i>W</i> off and the next frame, wrapped to 0 at <i>F</i>; then the strip at
	/// <i>x0</i> − the offset, the frame and the one after it, wrapped at <i>F</i>. With no machine, width or bank
	/// nothing moves, an engine guard.
	/// </summary>
	public override void AddDelta(short delta) {
		if (Cockpit.Machine is not { } machine || Travel == 0 || FrameCount == 0) {
			return;
		}

		int h = (ushort)-machine.Heading;
		int q = (h * FramePixels) >> 10;
		int frame = q / Travel;
		int offset = q % Travel + (((h >> 8) * ((FramePixels * 0xe39) >> 10)) >> 8);
		if (offset >= Travel) {
			offset -= Travel;
			frame++;
			if (frame == FrameCount) {
				frame = 0;
			}
		}

		int next = frame + 1 == FrameCount ? 0 : frame + 1;
		StripX = Rect.X0 - offset;
		RightFrame = Es1GunsightBanks.Frame(Banks.HTick, next);
		LeftFrame = Es1GunsightBanks.Frame(Banks.HTick, frame);
	}

	/// <summary>
	/// <c>HudHeadingTape_Paint</c> (<c>00033dd4</c>), in the canvas push: the context's columns narrowed to the tape's
	/// <i>x0</i> and <i>x1</i> less the origin's <i>x</i>, and that <i>x</i> set to the tape's <i>x0</i> less itself; the
	/// left frame at (<see cref="StripX"/>, <i>y0</i>) and the right frame a width further on; the context put back.
	/// </summary>
	public override void Paint() {
		var canvas = Cockpit.PushCanvasContext();
		int originX = canvas.Origin.X;
		var narrowed = new Es1PanelCanvas(canvas.Display,
			new Es1Rect(Rect.X0 - originX, canvas.Rect.Y0, Rect.X1 - originX, canvas.Rect.Y1),
			(Rect.X0 - originX, canvas.Origin.Y));
		narrowed.Blit(LeftFrame, StripX, Rect.Y0);
		narrowed.Blit(RightFrame, StripX + Travel, Rect.Y0);
	}

	/// <summary>
	/// The width, <i>P</i>, the strip at <i>x0</i> with the bank's last frame and its first beside it. With no bank,
	/// where retail reads frame −1, no frame: an engine guard.
	/// </summary>
	private void RecomputeTape() {
		Travel = Rect.X1 - Rect.X0;
		FramePixels = FrameCount * Travel * 0x400 / 0x10000;
		StripX = Rect.X0;
		LeftFrame = Es1GunsightBanks.Frame(Banks.HTick, FrameCount - 1);
		RightFrame = Es1GunsightBanks.Frame(Banks.HTick, 0);
	}
}
