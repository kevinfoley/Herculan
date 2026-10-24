using HercWorks.Core.Data.File.Gau;
using Herculan.Engine.Content;
using Herculan.Es1.Ttm;
using Herculan.Es1.View;

namespace Herculan.Es1.Panels;

/// <summary>
/// The cockpit clock, the cockpit's <c>+0x1f9</c>, <c>CockpitClock_Ctor</c> (<c>ES1DBSIM 000433cc</c>)'s <c>0x8a</c>
/// bytes on the <c>.GAU</c> block at 1604 (docs/es1/formats/ttm-scripts.md#the-cockpit-clock): a 24-hour display that
/// starts at 00:00 and gains a minute a second, its colon lit every other half second, and the slots of its vtable
/// <c>00094b5a</c> that the cockpit calls. Making it opens the HERC's <c>.TTM</c> script.
///
/// <para>The digits <c>000bd504</c>, <c>000bd508</c> and <c>000bd50c</c> and the colon's toggle <c>00094b56</c> are
/// globals in retail, which the constructor sets but for the toggle; a mission builds one clock, so they are its own
/// here, the toggle clear as in the image.</para>
/// </summary>
public sealed class Es1CockpitClock {
	/// <summary>The counts of the clock <c>000981c4</c>, 60 a second, between two advances: half a second.</summary>
	public const uint AdvanceInterval = 30;

	/// <summary>The hour label's fill colour, which clears the clock's rect under each redraw.</summary>
	private const int HourFill = 0x3f;

	private readonly Es1Cockpit _cockpit;
	private readonly char[] _hours = ['0', '0'];
	private readonly char[] _minutes = ['0', '0'];

	/// <summary>
	/// <c>CockpitClock_Ctor</c>(0, <paramref name="cockpit"/>, <paramref name="block"/>): the gauge base's rect, the
	/// block's rect at 1620 (<c>PanelGauge_Ctor</c>, <c>00031318</c>); <c>ttm\</c> and the first eight characters of
	/// the chassis' cockpit name, the type record <c>+0x58</c> of <c>CockpitViewInstance</c>'s machine, with <c>.ttm</c>
	/// opened with <c>Ttm_Open</c>(manager, 0, 0, path), playing and not looping, and <c>CockpitViewInstance</c>'s
	/// canvas context stored as the script's render context <c>+0x1bd</c>; the digits 00, 00
	/// and the colon; the hour, minute and colon labels <c>+0x7a</c>, <c>+0x7e</c> and <c>+0x82</c> in
	/// <paramref name="font"/> on the rect, the hour's left-aligned with the margin (1, 0) and filled in <c>0x3f</c>, the
	/// minute's right-aligned with (1, 0) and the colon's centred, neither filled; and the next advance
	/// <see cref="AdvanceInterval"/> counts from <paramref name="now"/>.
	///
	/// <para>A failed open leaves no script. Retail then reads the dword before the handle table as the script and stores
	/// the context through it when it is not 0 (docs/es1/formats/ttm-scripts.md#open); here nothing is stored.</para>
	/// </summary>
	/// <param name="cockpit">The cockpit, the gauge base's <c>+0x10</c>; <c>CockpitViewInstance</c> is its globals' cockpit, or it when that is none. A cockpit without a machine opens no script, an engine guard.</param>
	/// <param name="font"><c>ColorSchemePanels</c>' first font, <c>cpblue</c>.</param>
	/// <param name="now">The clock <c>000981c4</c>.</param>
	internal Es1CockpitClock(Es1Cockpit cockpit, Es1GauClockBlock block, Es1TtmManager scripts, HudFont? font, uint now) {
		_cockpit = cockpit;
		Rect = Es1Rect.From(block.Rect);
		var instance = cockpit.Globals.CockpitViewInstance ?? cockpit;
		if (instance.Machine?.Type.CockpitName is { } cockpitName) {
			string name = cockpitName.Length > 8 ? cockpitName[..8] : cockpitName;
			byte handle = scripts.Open(Es1TtmPlayer.Playing, 0, name + ".ttm");
			if (scripts.Script(handle) is { } script) {
				script.Context = instance.PushCanvasContext;
				Script = script;
			}
		}

		Hour = new Es1Label(font);
		Hour.SetRect(1, Rect, 1, 0);
		Hour.FillColor = HourFill;
		Minute = new Es1Label(font);
		Minute.SetRect(Es1Label.RightAligned, Rect, 1, 0);
		Colon = new Es1Label(font);
		Colon.SetRect(Es1Label.Centered, Rect);
		NextAdvance = now + AdvanceInterval;
	}

	/// <summary><c>+0</c>–<c>+0xc</c>, the gauge base's rect.</summary>
	public Es1Rect Rect { get; }

	/// <summary>The script the constructor opened, or none.</summary>
	public Es1TtmPlayer? Script { get; }

	/// <summary><c>+0x7a</c>, <c>+0x7e</c>, <c>+0x82</c>: the hour, minute and colon labels.</summary>
	public Es1Label Hour { get; }

	/// <inheritdoc cref="Hour"/>
	public Es1Label Minute { get; }

	/// <inheritdoc cref="Hour"/>
	public Es1Label Colon { get; }

	/// <summary><c>+0x75</c>: nonzero while the clock's rect is to be copied to the other page.</summary>
	public byte Mark { get; private set; }

	/// <summary><c>+0x86</c>, the count of the clock <c>000981c4</c> past which the next advance comes.</summary>
	public uint NextAdvance { get; private set; }

	/// <summary><c>00094b56</c>: set on every other advance, when the minute goes on and the colon is drawn.</summary>
	public bool ColonLit { get; private set; }

	/// <summary>The time shown, the digits <c>000bd504</c> and <c>000bd508</c>, "HH:MM".</summary>
	public string Time => $"{new string(_hours)}:{new string(_minutes)}";

	/// <summary>
	/// Slot <c>+0xc</c>, <c>CockpitClock_Tick</c> (<c>000435c0</c>), from <c>Cockpit_DrawFrame</c> each frame: marked
	/// while a view change is staged; while marked, the rect from the hour label's first corner to the minute label's
	/// second copied from the page on the screen to the page drawn (<c>Widget_DrawToCockpit</c>(0, rect, 0)) and the mark
	/// cleared; and once <paramref name="now"/> has passed <see cref="NextAdvance"/>, <see cref="Advance"/>, the next
	/// advance <see cref="AdvanceInterval"/> counts on and the mark set.
	/// </summary>
	public void Tick(uint now) {
		if (_cockpit.ViewChangeStaged) {
			Mark = 2;
		}

		if (Mark != 0) {
			_cockpit.DrawToCockpit(new Es1Rect(Hour.Rect.X0, Hour.Rect.Y0, Minute.Rect.X1, Minute.Rect.Y1), 0);
			Mark = 0;
		}

		if (NextAdvance < now) {
			Advance();
			NextAdvance = now + AdvanceInterval;
			Mark = 1;
		}
	}

	/// <summary>Slot <c>+8</c>, <c>CockpitClock_Update</c> (<c>000435b0</c>), from <c>CockpitView_InvalidateWidgets</c>: <see cref="Advance"/>.</summary>
	public void Update() => Advance();

	/// <summary>
	/// <c>CockpitClock_Advance</c> (<c>00043658</c>): <see cref="ColonLit"/> toggled, and when it comes on a minute
	/// added, 00 to 59, the hour then going on from 00 to 24, and the minute after 24:59 making 00:00; then, when
	/// <c>CockpitView_RectCornerVisible</c> passes the hour label's rect, inside the canvas push the hour's and the
	/// minute's text drawn (<c>Label_SetText</c>), the hour's fill clearing the rect first, and the colon's while lit.
	/// </summary>
	public void Advance() {
		ColonLit = !ColonLit;
		if (ColonLit && ++_minutes[1] > '9') {
			_minutes[1] = '0';
			if (++_minutes[0] > '5') {
				_minutes[0] = '0';
				++_hours[1];
				if (_hours[1] < '5' || _hours[0] != '2') {
					if (_hours[1] > '9') {
						_hours[1] = '0';
						_hours[0]++;
					}
				} else {
					_hours[0] = _hours[1] = _minutes[0] = _minutes[1] = '0';
				}
			}
		}

		if (!_cockpit.RectCornerVisible(Hour.Rect)) {
			return;
		}

		var canvas = _cockpit.PushCanvasContext();
		Hour.SetText(canvas, new string(_hours));
		Minute.SetText(canvas, new string(_minutes));
		if (ColonLit) {
			Colon.SetText(canvas, ":");
		}
	}
}
