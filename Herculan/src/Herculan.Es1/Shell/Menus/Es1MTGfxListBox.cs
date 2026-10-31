using HercWorks.Core.Data.File.Dat.Sim;
using HercWorks.Core.Data.File.Dyn;
using Herculan.Es1.Panels;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// A picture tile, a <c>.DLG</c> kind-16 record's control: class <c>MTGfxListBox</c> (vtable <c>0007f4d4</c>,
/// <c>0x4b</c> bytes) over <c>DUIVSlider</c> (<c>0007f412</c>) and <c>DUISliderControl</c> (<c>0007f2f8</c>)
/// (docs/es1/shell/hangar.md#the-screen): one frame of a bitmap array blitted inside the rect, or an empty corner frame
/// for a frame outside the array, and while it is framed a one-colour box one pixel in. The base classes' fields are kept
/// here, as <see cref="Es1MTStrListBox"/> keeps them; their slots are the tile's.
/// </summary>
public sealed class Es1MTGfxListBox : Es1DUIControl {
	/// <summary>The <c>STYLES.DAT</c> record the empty frame is drawn with: <c>00082654</c> + <c>0xc0</c>, the table's fourth.</summary>
	private const int FrameStyle = 3;

	/// <summary>The frame color the constructor stores, 10.</summary>
	private const short DefaultFrameColor = 10;

	private readonly Es1ShellData _data;
	private readonly Es1MenuBanks _banks;

	/// <summary>
	/// <c>0001eaef</c>(tile, <paramref name="max"/>, <paramref name="min"/>, <paramref name="word"/>, <paramref name="id"/>,
	/// <paramref name="bank"/>, <paramref name="rect"/>): <c>DUIControl_Ctor</c>(tile, 0, id, rect), so <c>Tab</c> passes
	/// over it; the slider's minimum, maximum and tracked point (the word, twice), <c>+0x2c</c> 1 and tracking 1; then the
	/// bank, its frame count over the slider's maximum, the frame the word modulo the count (the remainder keeps the word's
	/// sign, so −1 stays −1), the frame record, the frame color 10, not framed, and <see cref="Es1DUIControl.Changed"/>
	/// cleared. A bank of none counts 0 frames here and keeps the word as the frame, an engine guard where retail reads the
	/// count through a null pointer and divides by it.
	/// </summary>
	/// <param name="banks">The banks <see cref="Dtor"/> hands <paramref name="bank"/> back to, retail's statics.</param>
	public Es1MTGfxListBox(Es1ShellData data, Es1MenuBanks banks, short max, short min, short word, short id,
			IReadOnlyList<DynamixBitmap>? bank, Es1Rect rect) : base(false, 0, rect) {
		_data = data;
		_banks = banks;
		Id = id;
		Min = min;
		Count = max;
		TrackY = word;
		PreviousTrackY = word;
		ThumbHeight = 1;
		Tracking = 1;
		Bank = bank;
		Count = (short)(bank?.Count ?? 0);
		Frame = Count != 0 ? (short)(word % Count) : word;
		Style = data.Styles is { } styles && FrameStyle < styles.Styles.Length ? styles.Styles[FrameStyle] : null;
		FrameColor = DefaultFrameColor;
		Framed = false;
		Changed = false;
	}

	/// <summary><c>+0x24</c>, the slider's minimum.</summary>
	public short Min { get; }

	/// <summary><c>+0x26</c>, the slider's maximum, which the constructor sets to the bank's frame count.</summary>
	public short Count { get; }

	/// <summary><c>+0x28</c>, the tracked point's y, relative to the window; the constructor stores the word there.</summary>
	public short TrackY { get; private set; }

	/// <summary><c>+0x2a</c>, the tracked point's y before the last <see cref="SetTrackPoint"/>.</summary>
	public short PreviousTrackY { get; private set; }

	/// <summary><c>+0x2c</c>, 1: how far above the rect's bottom <see cref="SetTrackPoint"/> holds the point.</summary>
	public short ThumbHeight { get; }

	/// <summary><c>+0x3e</c>, the bitmap array; none when its file does not read.</summary>
	public IReadOnlyList<DynamixBitmap>? Bank { get; private set; }

	/// <summary><c>+0x42</c>, the frame shown; one below 0 or past the bank shows the empty frame.</summary>
	public short Frame { get; private set; }

	/// <summary><c>+0x44</c>, the empty frame's <c>STYLES.DAT</c> record; none when the table lacks it.</summary>
	public Es1WidgetStyle? Style { get; }

	/// <summary><c>+0x48</c>: the tile is framed in <see cref="FrameColor"/>.</summary>
	public bool Framed { get; private set; }

	/// <summary><c>+0x49</c>, the frame's color.</summary>
	public short FrameColor { get; private set; }

	/// <summary>
	/// Slot <c>+4</c>, <c>0001ec76</c>: the vtable stored, the bank handed to <c>Shell_FreeBitmapArraySlot</c>
	/// (<see cref="Es1MenuBanks.Free"/>) and cleared, the base vtables stored and the tile freed.
	/// </summary>
	public override void Dtor() {
		_banks.Free(Bank);
		Bank = null;
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>0001ed21</c>: for a frame below 0 or at or past <see cref="Count"/>, <c>CornerFrame_Draw</c> with
	/// <see cref="Style"/> over the rect; otherwise the frame blitted at the rect's corner plus (1, 1). Then, while
	/// <see cref="Framed"/>, <c>Raster_FrameRectColour</c>(<see cref="FrameColor"/>) of the rect one pixel in, (left + 1,
	/// top + 1)–(right − 1, bottom − 1); and <see cref="Es1DUIControl.Changed"/> cleared.
	/// </summary>
	public override void Draw(Es1ShellRenderContext context) {
		if (Bank is { } bank && Frame >= 0 && Frame < Count) {
			context.Blit(bank[Frame], Rect.X0 + 1, Rect.Y0 + 1);
		} else {
			Es1CornerFrame.Draw(context.Canvas, Style, Rect, _data.Corners);
		}

		if (Framed) {
			context.Canvas.OutlineRect(Rect.X0 + 1, Rect.Y0 + 1, Rect.X1 - 1, Rect.Y1 - 1, (byte)FrameColor);
		}

		Changed = false;
	}

	/// <summary>
	/// Slot <c>+0xc</c>, called after every state change: <c>0001ef5b</c>. With <see cref="TrackY"/> above the rect's top,
	/// <see cref="PrevFrame"/>; below its bottom, <see cref="NextFrame"/>. <see cref="SetTrackPoint"/> holds the point
	/// within the rect, so a press or a drag steps no frame.
	/// </summary>
	public override void Invalidate() {
		if (TrackY < Rect.Y0) {
			PrevFrame();
		} else if (Rect.Y1 < TrackY) {
			NextFrame();
		}
	}

	/// <summary>Slot <c>+0x10</c>, <c>0001efa7</c>: <see cref="Framed"/> and <see cref="Es1DUIControl.Changed"/> set.</summary>
	public override void Highlight() {
		Framed = true;
		Changed = true;
	}

	/// <summary>Slot <c>+0x14</c>, <c>0001efbd</c>(tile, <paramref name="color"/>): the color stored, then slot <c>+0x10</c> (<see cref="Highlight"/>).</summary>
	public void HighlightIn(short color) {
		FrameColor = color;
		Highlight();
	}

	/// <summary>Slot <c>+0x18</c>, <c>0001efe0</c>: <see cref="Framed"/> cleared, <see cref="Es1DUIControl.Changed"/> set.</summary>
	public override void Unhighlight() {
		Framed = false;
		Changed = true;
	}

	/// <summary>Slot <c>+0x1c</c>, <c>0001eef0</c>: the frame less 1 while it is above 0; <see cref="Es1DUIControl.Changed"/> set.</summary>
	public void PrevFrame() {
		if (Frame > 0) {
			Frame--;
		}

		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x20</c>, <c>0001ef12</c>: the frame plus 1 modulo <see cref="Count"/>, <see cref="Es1DUIControl.Changed"/>
	/// set, and the frame plus 1 over the count returned, 1 when it went round. A count of 0 leaves the frame and returns 0
	/// here, an engine guard where retail divides by it.
	/// </summary>
	public short NextFrame() {
		int next = Frame + 1;
		Changed = true;
		if (Count == 0) {
			return 0;
		}

		Frame = (short)(next % Count);
		return (short)(next / Count);
	}

	/// <summary>Slot <c>+0x24</c>, <c>0001ef3f</c>(tile, <paramref name="frame"/>): the frame stored as given, <see cref="Es1DUIControl.Changed"/> set.</summary>
	public void SetFrame(short frame) {
		Frame = frame;
		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x2c</c>, the value a release posts: <c>0001ab2c</c>, the slider's position, from the rect's height less
	/// the thumb rect <c>+0x2e</c>'s and the range <see cref="Count"/> − <see cref="Min"/>, in the compiler's floating
	/// point.
	/// PLACEHOLDER: the thumb rect is one the constructor never fills, and CREW's handler reads only the target; the value
	/// is unported and the base's 1 is posted, until a menu that reads it lands.
	/// </summary>
	public override short GetValue() => 1;

	/// <summary>
	/// Slot <c>+0x38</c>, <c>0001aac3</c>(tile, point): <see cref="PreviousTrackY"/> the tracked y, then the tracked y the
	/// point's y held between the rect's top and its bottom less <see cref="ThumbHeight"/>.
	/// </summary>
	public override void SetTrackPoint(int x, int y) {
		PreviousTrackY = TrackY;
		if (y < Rect.Y0) {
			TrackY = (short)Rect.Y0;
		} else if (Rect.Y1 - ThumbHeight < y) {
			TrackY = (short)(Rect.Y1 - ThumbHeight);
		} else {
			TrackY = (short)y;
		}
	}
}
