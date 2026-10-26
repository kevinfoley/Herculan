using Herculan.Engine.Content;
using Herculan.Es1.Panels;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Messages;

/// <summary>
/// The pilot channel, the cockpit's <c>+0x203</c>: <c>MsgPort</c> with the table <c>DialogPortVtable</c>
/// (<c>000931b6</c>) and two save-under buffers, the screen under the box <c>+0x468</c> and the box as painted
/// <c>+0x46c</c>, a <c>0x474</c>-byte object (docs/es1/simulation/cockpit-messages.md#the-pilot-channel). A squadmate's
/// line goes up in a box of the squadmate's color, sized to the line and centered on the 320-wide screen, with the
/// speaker's name before it. The show plays nothing: the speaker's comm box on the Heads-Down Display speaks the line,
/// and its begin and end callbacks (<see cref="Es1MessagePort.AddBeginCallback"/>,
/// <see cref="Es1MessagePort.AddEndCallback"/>) and its writes of the ready and cancelled latches are what put a line
/// up, plan step 6c6's part C. Until then a due line is never made ready and goes by unshown.
///
/// <para>The box is lifted off the screen and put back around everything that draws under it: the cockpit's frame puts
/// the screen back (<see cref="RestoreBackground"/>) before the 3D pass and <c>Sim_RenderFrame</c>'s
/// <see cref="Update"/> saves the new screen under it and paints it again after; the view changes and the Heads-Down
/// Display's screen copy do the same around their own draws.</para>
/// </summary>
public sealed class Es1PilotChannel : Es1MessagePort {
	/// <summary>The characters of the message's text <c>PilotMessagePort_ComposeLine</c> appends, its <c>strncat</c>'s count.</summary>
	public const int TextLimit = 0x4a;

	/// <summary><c>000931da</c>, the name a line from no squadmate is signed with, and <c>000931df</c>, what follows the name.</summary>
	public const string NoSpeaker = "XXXX", NameSeparator = ": ";

	/// <summary>The box's left edge less half the line's width, the columns it adds to the width, and the text's center.</summary>
	public const int BoxLeft = 150, BoxPadding = 20, TextCenter = 160;

	/// <summary>The box's fill and frame for a line from no squadmate, the brushes {0, 63} and {4, 53}.</summary>
	public const byte NoSpeakerFill = 63, NoSpeakerFrame = 53;

	private readonly HudFont? _font;

	/// <summary><c>+0x468</c>, the screen under the box.</summary>
	private ushort[]? _under;

	/// <summary><c>+0x46c</c>, the box as painted.</summary>
	private ushort[]? _painted;

	/// <summary>
	/// <c>DialogPort_Ctor</c> (<c>0002f3d8</c>)(0, <paramref name="rect"/>), the <c>.GAU</c> record + 1668
	/// (docs/es1/simulation/cockpit-messages.md#building-the-two-ports): <c>MessagePort_CtorBase</c> and the table
	/// <c>000931b6</c>. Retail's two buffers are <c>Region_PixelCount</c> of the rect each; here each is the size of the
	/// box it last saved.
	/// </summary>
	/// <param name="font"><c>ColorSchemePanels</c>[2], <c>cpred</c> (<c>00092d48</c>), the line's font; none draws no text and measures 0.</param>
	internal Es1PilotChannel(Es1Cockpit cockpit, Es1Rect rect, Es1MessageTables tables, HudFont? font)
		: base(cockpit, rect, tables) {
		_font = font;
	}

	/// <summary><c>PilotMessagePort_LineBuffer</c> (<c>000b0e64</c>), the line <see cref="SetText"/> last composed.</summary>
	public string LineBuffer { get; private set; } = string.Empty;

	/// <summary>
	/// <c>Ai_PostSquadMessage</c> (<c>0001a188</c>)(<paramref name="machine"/>, <paramref name="force"/>,
	/// <paramref name="id"/>) (docs/es1/simulation/cockpit-messages.md#posting): unless the machine is destroyed
	/// (<see cref="Es1Mech.Destroyed"/>, <c>+0x93</c>) and <paramref name="force"/> is clear, {id, machine} posted to the
	/// pilot channel of <c>CockpitViewInstance</c> (<c>CockpitView_GetSquadMessagePort</c>, <c>0002d614</c>), the post
	/// reading the id's low byte. With no cockpit built, which retail always has, nothing.
	/// </summary>
	public static void PostSquadMessage(Es1SimGlobals globals, Es1Mech machine, bool force, short id) {
		if (machine.Destroyed && !force) {
			return;
		}

		globals.CockpitViewInstance?.PilotChannel.Post((byte)id, machine);
	}

	/// <summary>
	/// Slot <c>+4</c>, <c>PilotMessagePort_Show</c> (<c>0002f4f4</c>): false while a view change is staged. Otherwise the
	/// line composed (<see cref="SetText"/>) and up, and the display's page drawn (<c>+0x8c</c>) made its source page;
	/// inside the canvas push, with <i>w</i> the line's width in <c>cpred</c> and (<i>ox</i>, <i>oy</i>) the canvas
	/// origin: the box's <i>x0</i> = <i>ox</i> + <see cref="BoxLeft"/> − <i>w</i> / 2 and <i>x1</i> = <i>x0</i> + <i>w</i>
	/// + <see cref="BoxPadding"/> on its own rows, each halving an arithmetic shift; the screen under the box moved by the
	/// origin saved (<c>Region_Save</c>, <c>+0x468</c>); the box filled and framed in the speaker's slot color
	/// (<see cref="Es1Cockpit.SquadSlotColor"/>) and the one below it, or <see cref="NoSpeakerFill"/> and
	/// <see cref="NoSpeakerFrame"/> for a subject in no slot; the line drawn from (<i>ox</i> + <see cref="TextCenter"/> −
	/// <i>w</i> / 2, <i>y1</i> − 1); the box copied to the page on the screen
	/// (<see cref="Es1MessagePort.CopyBoxBetweenPages"/>, 1); the box as painted saved (<c>+0x46c</c>); the cursor drawn
	/// again unless the current view is 4; and the page drawn made the draw page. Answers true.
	/// </summary>
	protected override bool Show() {
		if (Cockpit.ViewChangeStaged) {
			return false;
		}

		SetText(Current);
		LineUp = true;
		var display = Cockpit.Display;
		int drawn = display.PairDrawn;
		display.SourcePage = drawn;
		var canvas = Cockpit.PushCanvasContext();
		var origin = canvas.Origin;
		int width = _font?.Measure(LineBuffer) ?? 0;
		int x0 = origin.X - (width >> 1) + BoxLeft;
		Box = Box with { X0 = x0, X1 = x0 + width + BoxPadding };
		_under = canvas.SaveRegion(Box.Offset(origin.X, origin.Y));
		int squad = Cockpit.SquadIndexOf(Current?.Subject);
		byte fill = squad == -1 ? NoSpeakerFill : (byte)Es1Cockpit.SquadSlotColor(squad);
		byte frame = squad == -1 ? NoSpeakerFrame : (byte)(fill - 1);
		canvas.FillRect(Box.X0, Box.Y0, Box.X1, Box.Y1, fill);
		canvas.OutlineRect(Box.X0, Box.Y0, Box.X1, Box.Y1, frame);
		canvas.DrawString(_font, LineBuffer, origin.X + TextCenter - (width >> 1), Box.Y1 - 1);
		CopyBoxBetweenPages(1);
		_painted = canvas.SaveRegion(Box.Offset(origin.X, origin.Y));
		if (Cockpit.CurrentView != Es1CockpitViews.ExternalView) {
			display.Cursor.Show();
		}

		display.DrawPage = drawn;
		return true;
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>PilotMessagePort_ComposeLine</c> (<c>0002f454</c>)(port, <paramref name="record"/>):
	/// <see cref="LineBuffer"/> emptied with no record; otherwise the speaker's name, <c>Squad_PilotName</c> of the
	/// subject's squadmate slot (<see cref="Es1Cockpit.SquadPilotName"/>) or <see cref="NoSpeaker"/> for a subject in no
	/// slot, a null one included, then <see cref="NameSeparator"/> and the first <see cref="TextLimit"/> characters of the
	/// record's text; answers it. A slot whose name is not set, which retail's squadmates never are, reads as empty, an
	/// engine guard.
	/// </summary>
	protected override string SetText(Es1QueuedMessage? record) {
		if (record == null) {
			LineBuffer = string.Empty;
			return LineBuffer;
		}

		int squad = Cockpit.SquadIndexOf(record.Subject);
		string name = squad == -1 ? NoSpeaker : Cockpit.SquadPilotName(squad) ?? string.Empty;
		string text = record.Text ?? string.Empty;
		LineBuffer = name + NameSeparator + (text.Length > TextLimit ? text[..TextLimit] : text);
		return LineBuffer;
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>PilotMessagePort_Erase</c> (<c>0002f7ec</c>): true with no line up; false while a view change
	/// is staged; otherwise each end callback run with the current message, the screen under the box put back inside the
	/// canvas push, the box copied to the page on the screen, the line down, and true.
	/// </summary>
	protected override bool Erase() {
		if (!LineUp) {
			return true;
		}

		if (Cockpit.ViewChangeStaged) {
			return false;
		}

		if (Current is { } current) {
			foreach (var callback in EndCallbacks) {
				callback(current);
			}
		}

		Restore(Cockpit.PushCanvasContext(), _under);
		CopyBoxBetweenPages(1);
		LineUp = false;
		return true;
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>PilotMessagePort_Update</c> (<c>0002f8c4</c>), once a frame from <c>Sim_RenderFrame</c>
	/// after the gunsight (docs/es1/simulation/main-loop.md#the-frame): while a line is up and no view change is staged,
	/// the page drawn made the display's source page and the box painted again (<see cref="Paint"/>); then
	/// <see cref="Es1MessagePort.Tick"/>. It sets no ready latch.
	/// </summary>
	public override void Update() {
		if (LineUp && !Cockpit.ViewChangeStaged) {
			Cockpit.Display.SourcePage = Cockpit.Display.PairDrawn;
			Paint();
		}

		Tick();
	}

	/// <summary>
	/// Slot <c>+0x14</c>, <c>PilotMessagePort_RestoreBackground</c> (<c>0002fb74</c>): while a line is up, the screen under
	/// the box put back inside the canvas push, the box lifted off the draw page.
	/// </summary>
	public override void RestoreBackground() {
		if (LineUp) {
			Restore(Cockpit.PushCanvasContext(), _under);
		}
	}

	/// <summary>
	/// Slot <c>+0x18</c>, <c>PilotMessagePort_RedrawFromSaveUnder</c> (<c>0002fbd0</c>): while a line is up, inside the
	/// canvas push, the screen under the box moved by the canvas origin saved anew from the source page, and the box as
	/// painted put back over it. Unlike the show it tests no view change.
	/// </summary>
	public override void Paint() {
		if (!LineUp) {
			return;
		}

		var canvas = Cockpit.PushCanvasContext();
		_under = canvas.SaveRegion(Box.Offset(canvas.Origin.X, canvas.Origin.Y));
		Restore(canvas, _painted);
	}

	/// <summary><c>Region_Restore</c>(the box's region, its corner, <paramref name="cells"/>): the cells put back at the box's top-left; none puts back nothing.</summary>
	private void Restore(Es1PanelCanvas canvas, ushort[]? cells) {
		if (cells != null) {
			canvas.RestoreRegion(Box, Box.X0, Box.Y0, cells);
		}
	}
}
