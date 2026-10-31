using System.Globalization;
using HercWorks.Core.Data.File.Dat.Sim;
using Herculan.Es1.Panels;
using Herculan.Es1.Ttm;
using Herculan.Es1.View;

namespace Herculan.Es1.Shell.Menus;

/// <summary>
/// An animation, a <c>.DLG</c> kind-17 record's control: class <c>0001eff6</c> (vtable <c>0007f534</c>, <c>0x49</c>
/// bytes) over <c>DUIVSlider</c> (<c>0007f412</c>) and <c>DUISliderControl</c> (<c>0007f2f8</c>)
/// (docs/es1/formats/dlg-menu-dialogs.md#the-kinds): a corner frame over the rect and one of the <c>.TTM</c> scripts
/// <see cref="OpenScripts"/> opened for its id playing, the others hidden; ARMING's 1803 shows a weapon's or a missile
/// type's script (docs/es1/shell/arming.md#the-panel). The base classes' fields are kept here, as
/// <see cref="Es1MTGfxListBox"/> keeps them; their slots are the control's. The retail class's name is not known.
/// </summary>
public sealed class Es1AnimationControl : Es1DUIControl {
	/// <summary>The handle bytes <see cref="OpenScripts"/> fills, <c>000a056c</c>.</summary>
	public const int ScriptCount = 18;

	/// <summary>The id whose scripts <see cref="OpenScripts"/> opens, ARMING's animation.</summary>
	private const short ArmingAnimation = 1803;

	/// <summary>The weapon scripts <c>w1800w00.ttm</c> to <c>w1800w13.ttm</c> and the missile-type scripts <c>w1800r00.ttm</c> to <c>w1800r03.ttm</c>.</summary>
	private const int WeaponScripts = 14, MissileScripts = 4;

	/// <summary>The <c>STYLES.DAT</c> record the control is framed with: <c>00082654</c> + <c>0xc0</c>, the table's fourth.</summary>
	private const int FrameStyle = 3;

	/// <summary>The color <c>Raster_FrameRectColour</c> frames a framed control in.</summary>
	private const byte FramedColor = 10;

	/// <summary>The page a script shown is moved to, <c>Ttm_SetPage</c>(1).</summary>
	private const short FirstPage = 1;

	private readonly Es1ShellData _data;
	private readonly Func<Es1TtmManager?> _scripts;

	/// <summary>
	/// <c>0001eff6</c>(control, <paramref name="max"/>, <paramref name="min"/>, <paramref name="tracked"/>,
	/// <paramref name="id"/>, <paramref name="handles"/>, <paramref name="rect"/>): <c>DUIControl_Ctor</c>(control, 0,
	/// id, rect), so <c>Tab</c> passes over it; the slider's minimum, maximum and tracked point (twice), <c>+0x2c</c> 1 and
	/// tracking 1; then the handles, the maximum again, script 0 the one shown, the frame record, and
	/// <see cref="Es1DUIControl.Changed"/> cleared. <see cref="Framed"/> is not set by the constructor and starts clear
	/// here.
	/// </summary>
	/// <param name="scripts">The shell's scripts (<see cref="Es1ShellScreens.Scripts"/>), which the slots set playing, hidden and to a page.</param>
	public Es1AnimationControl(Es1ShellData data, Func<Es1TtmManager?> scripts, short max, short min, short tracked, short id,
			byte[] handles, Es1Rect rect) : base(false, 0, rect) {
		_data = data;
		_scripts = scripts;
		Id = id;
		Min = min;
		Count = max;
		TrackY = tracked;
		PreviousTrackY = tracked;
		ThumbHeight = 1;
		Tracking = 1;
		Handles = handles;
		Shown = 0;
		Style = data.Styles is { } styles && FrameStyle < styles.Styles.Length ? styles.Styles[FrameStyle] : null;
		Changed = false;
	}

	/// <summary><c>+0x24</c>, the slider's minimum.</summary>
	public short Min { get; }

	/// <summary><c>+0x26</c>, the slider's maximum, <c>0x13</c> from the build: one more than the handles.</summary>
	public short Count { get; }

	/// <summary><c>+0x28</c>, the tracked point's y, relative to the window.</summary>
	public short TrackY { get; private set; }

	/// <summary><c>+0x2a</c>, the tracked point's y before the last <see cref="SetTrackPoint"/>.</summary>
	public short PreviousTrackY { get; private set; }

	/// <summary><c>+0x2c</c>, 1: how far above the rect's bottom <see cref="SetTrackPoint"/> holds the point.</summary>
	public short ThumbHeight { get; }

	/// <summary><c>+0x3e</c>, the handle bytes <see cref="OpenScripts"/> returned; none once destroyed.</summary>
	public byte[]? Handles { get; private set; }

	/// <summary><c>+0x42</c>, the index of the script shown, −1 for none.</summary>
	public short Shown { get; private set; }

	/// <summary><c>+0x44</c>, the frame's <c>STYLES.DAT</c> record; none when the table lacks it.</summary>
	public Es1WidgetStyle? Style { get; }

	/// <summary><c>+0x48</c>: the control is framed in color 10.</summary>
	public bool Framed { get; private set; }

	/// <summary>
	/// Handle <paramref name="index"/> of <see cref="Handles"/>. Retail reads the byte before them for −1 and the byte after
	/// them for 18, which <see cref="SelectPrev"/> and <see cref="SelectNext"/> can reach; either is no handle here, an
	/// engine guard.
	/// </summary>
	private byte Handle(int index) => Handles is { } handles && index >= 0 && index < handles.Length ? handles[index] : Es1TtmManager.NoHandle;

	/// <summary>
	/// Slot <c>+4</c>, <c>0001f164</c>: <see cref="CloseScripts"/> of <see cref="Handles"/> and the field cleared, the base
	/// vtables stored and the control freed. The handle bytes keep the closed handles.
	/// </summary>
	public override void Dtor() {
		if (Handles is { } handles) {
			CloseScripts(_scripts(), handles);
		}

		Handles = null;
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>0001f20f</c>: <c>CornerFrame_Draw</c> with <see cref="Style"/> over the rect; while
	/// <see cref="Framed"/>, <c>Raster_FrameRectColour</c>(10, rect); while <see cref="Shown"/> is 0 or more, its script
	/// set playing (<c>Ttm_SetMode</c>(0)); and <see cref="Es1DUIControl.Changed"/> cleared.
	/// </summary>
	public override void Draw(Es1ShellRenderContext context) {
		Es1CornerFrame.Draw(context.Canvas, Style, Rect, _data.Corners);
		if (Framed) {
			context.Canvas.OutlineRect(Rect.X0, Rect.Y0, Rect.X1, Rect.Y1, FramedColor);
		}

		if (Shown >= 0) {
			_scripts()?.SetMode(Es1TtmPlayer.Playing, Handle(Shown));
		}

		Changed = false;
	}

	/// <summary>
	/// Slot <c>+0xc</c>, called after every state change: <c>0001f3ca</c>. With <see cref="TrackY"/> above the rect's top,
	/// <see cref="SelectPrev"/>; below its bottom, <see cref="SelectNext"/>. <see cref="SetTrackPoint"/> holds the point
	/// within the rect, so a press or a drag changes no script.
	/// </summary>
	public override void Invalidate() {
		if (TrackY < Rect.Y0) {
			SelectPrev();
		} else if (Rect.Y1 < TrackY) {
			SelectNext();
		}
	}

	/// <summary>Slot <c>+0x10</c>, <c>0001f416</c>: <see cref="Framed"/> and <see cref="Es1DUIControl.Changed"/> set.</summary>
	public override void Highlight() {
		Framed = true;
		Changed = true;
	}

	/// <summary>Slot <c>+0x18</c>, <c>0001f42c</c>: <see cref="Framed"/> cleared, <see cref="Es1DUIControl.Changed"/> set.</summary>
	public override void Unhighlight() {
		Framed = false;
		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x1c</c>, <c>0001f275</c>: while <see cref="Shown"/> is 0 or more, its script hidden (<c>Ttm_SetMode</c>(2)),
	/// the index less 1, and that script moved to page 1 (<c>Ttm_SetPage</c>) and set playing; <see cref="Es1DUIControl.Changed"/> set.
	/// </summary>
	public void SelectPrev() {
		if (Shown >= 0) {
			var scripts = _scripts();
			scripts?.SetMode(Es1TtmPlayer.Hidden, Handle(Shown));
			Shown--;
			scripts?.SetPage(FirstPage, Handle(Shown));
			scripts?.SetMode(Es1TtmPlayer.Playing, Handle(Shown));
		}

		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x20</c>, <c>0001f2dc</c>: while <see cref="Shown"/> is 0 or more its script hidden; the index plus 1
	/// modulo <see cref="Count"/>, and that script moved to page 1 and set playing; <see cref="Es1DUIControl.Changed"/>
	/// set. A count of 0 leaves the index here, an engine guard where retail divides by it.
	/// </summary>
	public void SelectNext() {
		var scripts = _scripts();
		if (Shown >= 0) {
			scripts?.SetMode(Es1TtmPlayer.Hidden, Handle(Shown));
		}

		if (Count != 0) {
			Shown = (short)((Shown + 1) % Count);
		}

		scripts?.SetPage(FirstPage, Handle(Shown));
		scripts?.SetMode(Es1TtmPlayer.Playing, Handle(Shown));
		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x24</c>, <c>0001f359</c>(control, <paramref name="index"/>), the one ARMING calls: while
	/// <see cref="Shown"/> is 0 or more its script hidden; the index stored; for an index of 0 or more that script moved to
	/// page 1 and set playing, so it starts again; <see cref="Es1DUIControl.Changed"/> set. −1 shows none.
	/// </summary>
	public void Select(short index) {
		var scripts = _scripts();
		if (Shown >= 0) {
			scripts?.SetMode(Es1TtmPlayer.Hidden, Handle(Shown));
		}

		Shown = index;
		if (index >= 0) {
			scripts?.SetPage(FirstPage, Handle(Shown));
			scripts?.SetMode(Es1TtmPlayer.Playing, Handle(Shown));
		}

		Changed = true;
	}

	/// <summary>
	/// Slot <c>+0x2c</c>, the value a release posts: <c>0001ab2c</c>, the slider's position, from the rect's height less
	/// the thumb rect <c>+0x2e</c>'s and the range <see cref="Count"/> − <see cref="Min"/>, in the compiler's floating
	/// point.
	/// PLACEHOLDER: the thumb rect is one the constructor never fills, and ARMING's 1803 case reads only the target; the
	/// value is unported and the base's 1 is posted, until a menu that reads it lands.
	/// </summary>
	public override short GetValue() => 1;

	/// <summary>
	/// Slot <c>+0x38</c>, <c>0001aac3</c>(control, point): <see cref="PreviousTrackY"/> the tracked y, then the tracked y
	/// the point's y held between the rect's top and its bottom less <see cref="ThumbHeight"/>.
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

	/// <summary>
	/// <c>Menu_OpenControlTtms</c> (<c>00037996</c>)(<paramref name="id"/>), which <c>Menu_BuildFromDlg</c> hands a
	/// kind-17 control: for 1803 the scripts <c>w1800w00.ttm</c> to <c>w1800w09.ttm</c> and <c>w1800w10.ttm</c> to
	/// <c>w1800w13.ttm</c> into bytes 0 to 13 and <c>w1800r00.ttm</c> to <c>w1800r03.ttm</c> into bytes 14 to 17, each
	/// opened playing, not looping, on the render context <c>0007fcdc</c> (<see cref="Es1ShellScreens.WindowContext"/>)
	/// and run once (<c>Ttm_RunScript</c>), then every opened one hidden; for any other id every byte <c>0xff</c>.
	/// Returns the bytes, <paramref name="context"/>'s one array (<see cref="Es1MenuContext.ControlScripts"/>). The runs'
	/// fade waits are passed over, as a handler's are, and with no scripts every byte is no handle, engine guards.
	/// </summary>
	public static byte[] OpenScripts(Es1MenuContext context, short id) {
		byte[] handles = context.ControlScripts;
		var scripts = context.Screens.Scripts;
		if (id != ArmingAnimation || scripts == null) {
			Array.Fill(handles, Es1TtmManager.NoHandle);
			return handles;
		}

		var windowContext = context.Screens.WindowContext;
		for (int i = 0; i < WeaponScripts; i++) {
			handles[i] = OpenAndRun(scripts, windowContext, string.Create(CultureInfo.InvariantCulture, $"w1800w{i:00}.ttm"));
		}

		for (int i = 0; i < MissileScripts; i++) {
			handles[WeaponScripts + i] = OpenAndRun(scripts, windowContext, string.Create(CultureInfo.InvariantCulture, $"w1800r0{i}.ttm"));
		}

		foreach (byte handle in handles) {
			if (handle != Es1TtmManager.NoHandle) {
				scripts.SetMode(Es1TtmPlayer.Hidden, handle);
			}
		}

		return handles;

		static byte OpenAndRun(Es1TtmManager scripts, Func<Es1PanelCanvas>? windowContext, string name) {
			byte handle = scripts.Open(Es1TtmPlayer.Playing, 0, windowContext, name);
			foreach (var _ in scripts.RunScript(handle)) {
			}

			return handle;
		}
	}

	/// <summary><c>Menu_CloseControlTtms</c> (<c>00037aac</c>)(<paramref name="handles"/>): each of the 18 bytes that is not <c>0xff</c> closed with <c>Ttm_Close</c>.</summary>
	public static void CloseScripts(Es1TtmManager? scripts, byte[] handles) {
		foreach (byte handle in handles) {
			if (handle != Es1TtmManager.NoHandle) {
				scripts?.Close(handle);
			}
		}
	}
}
