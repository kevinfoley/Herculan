using HercWorks.Core.Data.File;
using Herculan.Engine.Content;
using Herculan.Es1.Sim;
using Herculan.Es1.View;

namespace Herculan.Es1.Messages;

/// <summary>
/// The computer's ticker, the cockpit's <c>+0x207</c>: <c>MsgPort</c> with the table <c>00092f0a</c> and the blink latch
/// <c>+0x468</c>, a <c>0x469</c>-byte object (docs/es1/simulation/cockpit-messages.md#the-ticker). Its line is centered
/// in a plain box, blinks when the line asks for it and comes with a tone; nothing scrolls and nothing is spoken.
/// </summary>
public sealed class Es1MessageTicker : Es1MessagePort {
	/// <summary>The characters of a line <c>MessagePort_SetText</c> copies, its <c>strncpy</c>'s count.</summary>
	public const int TextLimit = 0x50;

	/// <summary>The box's fill, the brush {0, 63}.</summary>
	public const byte FillColor = 63;

	/// <summary>The clock's bit a blinking line is drawn on.</summary>
	public const uint BlinkBit = 0x20;

	private readonly HudFont? _font;

	/// <summary>
	/// <c>Cockpit_LoadPanel</c>'s inline build (docs/es1/simulation/cockpit-panel.md#building-it): <c>MessagePort_CtorBase</c>
	/// on <paramref name="rect"/>, the <c>.GAU</c> record + 1684, the table <c>00092f0a</c>, and the blink latch clear.
	/// </summary>
	/// <param name="font"><c>ColorSchemePanels</c>[2], <c>cpred</c> (<c>00092d48</c>), the line's font; none draws no text.</param>
	internal Es1MessageTicker(Es1Cockpit cockpit, Es1Rect rect, Es1MessageTables tables, StringFile? systemStrings,
			HudFont? font)
		: base(cockpit, rect, tables, systemStrings) {
		_font = font;
	}

	/// <summary><c>+0x468</c>, the blink latch.</summary>
	public bool Blink { get; private set; }

	/// <summary><c>MessagePort_TextBuffer</c> (<c>000b0eb4</c>), the line <see cref="SetText"/> last copied.</summary>
	public string TextBuffer { get; private set; } = string.Empty;

	/// <summary>
	/// <c>Computer_PostMessage</c> (<c>0001a154</c>)(<paramref name="id"/>): {id, 0} posted to the ticker of
	/// <c>CockpitViewInstance</c> (docs/es1/simulation/cockpit-messages.md#posting), the post reading the id's low byte.
	/// With no cockpit built, which retail always has, nothing.
	/// </summary>
	public static void ComputerPostMessage(Es1SimGlobals globals, short id) =>
		globals.CockpitViewInstance?.Ticker.Post((byte)id, null);

	/// <summary>
	/// The tone <c>MessagePort_Show</c> plays for <paramref name="id"/> through <c>MessagePort_ToneTable</c>
	/// (<c>000931e4</c>), a <c>Sound_Play</c> id: <c>0x17</c> for <c>0x10</c>, <c>0x12</c>, <c>0x1b</c> and <c>0x1c</c>;
	/// <c>0x18</c> for 8, <c>0xb</c>–<c>0xd</c>, <c>0xf</c>, <c>0x11</c>, <c>0x13</c> and <c>0x14</c>; <c>0x19</c> for 6
	/// and <c>0xe</c>; <c>0x1a</c> for every other id up to <c>0x21</c>; none above.
	/// </summary>
	public static short? ToneFor(short id) => id switch {
		0x10 or 0x12 or 0x1b or 0x1c => 0x17,
		8 or 0xb or 0xc or 0xd or 0xf or 0x11 or 0x13 or 0x14 => 0x18,
		6 or 0xe => 0x19,
		>= 0 and <= 0x21 => 0x1a,
		_ => null,
	};

	/// <summary>
	/// Slot <c>+4</c>, <c>MessagePort_Show</c> (<c>0002f96c</c>): the text set from the current message, the line up,
	/// painted, the box copied to the page on the screen (<see cref="Es1MessagePort.CopyBoxBetweenPages"/>, 1), and the
	/// tone (<see cref="ToneFor"/>), <c>0x1f</c> also setting the blink latch and <c>0x20</c> and <c>0x21</c> clearing it.
	/// Answers true.
	/// </summary>
	protected override bool Show() {
		SetText(Current);
		LineUp = true;
		Paint();
		CopyBoxBetweenPages(1);
		short id = Current?.Id ?? -1;
		if (id == 0x1f) {
			Blink = true;
		} else if (id is 0x20 or 0x21) {
			Blink = false;
		}

		// PLACEHOLDER: Sound_Play (0004de84)(ToneFor(id)), when it gives one; no plan step ports sound yet.
		_ = ToneFor(id);
		return true;
	}

	/// <summary>
	/// Slot <c>+8</c>, <c>MessagePort_SetText</c> (<c>0002f934</c>)(port, <paramref name="record"/>): <see cref="TextBuffer"/>
	/// emptied with no record, otherwise the record's text, its first <see cref="TextLimit"/> characters; answers it.
	/// </summary>
	protected override string SetText(Es1QueuedMessage? record) {
		string text = record?.Text ?? string.Empty;
		TextBuffer = text.Length > TextLimit ? text[..TextLimit] : text;
		return TextBuffer;
	}

	/// <summary>
	/// Slot <c>+0xc</c>, <c>MessagePort_Erase</c> (<c>0002fb18</c>): inside the canvas push the box filled with
	/// {0, <see cref="FillColor"/>}; the box copied to the page on the screen; the line down; answers true, whether or not
	/// a line was up.
	/// </summary>
	protected override bool Erase() {
		var canvas = Cockpit.PushCanvasContext();
		canvas.FillRect(Box.X0, Box.Y0, Box.X1, Box.Y1, FillColor);
		CopyBoxBetweenPages(1);
		LineUp = false;
		return true;
	}

	/// <summary>
	/// Slot <c>+0x10</c>, <c>MessageTicker_Update</c> (<c>0002fab8</c>), once a frame from <c>Cockpit_DrawFrame</c>:
	/// <see cref="Input.Es1Preferences.MessagePortShowEnabled"/> saved and set; while a line is up and no view change is
	/// staged, painted; <see cref="Es1MessagePort.Tick"/>; the ready latch set when the due latch is; and the byte put back.
	/// So a message goes up the frame after it comes due, and the ticker shows whatever option 2 holds.
	/// </summary>
	public override void Update() {
		var preferences = Cockpit.Preferences;
		bool saved = preferences.MessagePortShowEnabled;
		preferences.MessagePortShowEnabled = true;
		if (LineUp && !Cockpit.ViewChangeStaged) {
			Paint();
		}

		Tick();
		if (Due) {
			Ready = true;
		}

		preferences.MessagePortShowEnabled = saved;
	}

	/// <summary>
	/// Slot <c>+0x18</c>, <c>MessageTicker_Paint</c> (<c>0002fa00</c>): inside the canvas push, the box filled with
	/// {0, <see cref="FillColor"/>}; then, while a line is up and the blink latch is clear or the clock's
	/// <see cref="BlinkBit"/> is set, <see cref="TextBuffer"/> drawn in <c>cpred</c> from <i>x</i> = <i>x0</i> +
	/// ((<i>x1</i> − <i>x0</i>) &gt;&gt; 1) − (width &gt;&gt; 1), <i>y</i> = <i>y1</i> − 1, its width
	/// <c>HudFont_MeasureString</c>'s.
	/// </summary>
	public override void Paint() {
		var canvas = Cockpit.PushCanvasContext();
		int width = _font?.Measure(TextBuffer) ?? 0;
		int x = Box.X0 + ((Box.X1 - Box.X0) >> 1) - (width >> 1);
		canvas.FillRect(Box.X0, Box.Y0, Box.X1, Box.Y1, FillColor);
		if (LineUp && (!Blink || (Now & BlinkBit) != 0)) {
			canvas.DrawString(_font, TextBuffer, x, Box.Y1 - 1);
		}
	}
}
