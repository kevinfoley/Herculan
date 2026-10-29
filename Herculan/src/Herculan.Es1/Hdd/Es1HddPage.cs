using Herculan.Es1.View;

namespace Herculan.Es1.Hdd;

/// <summary>
/// One of the Heads-Down Display's two pages, on the pages' base <c>HDDisplay_Ctor</c> (<c>ES1DBSIM 0003c228</c>)
/// (docs/es1/simulation/heads-down-display.md#the-display-object--hdddisplay_ctor-0003c258): the paint stage
/// <see cref="Stage"/> at <c>+8</c> and the slots of its table at <c>+0xc</c>, <c>HDDisplayVtable</c>
/// (<c>000944fe</c>), which the display calls. The base's repaint and update are 0, its hide and show do nothing and
/// its key slot answers 0; a page's own table is its object file's.
/// </summary>
public abstract class Es1HddPage {
	/// <summary>
	/// The page's constructor (display, <paramref name="column"/>, <paramref name="screen"/>) as the display calls it,
	/// with what it is handed kept. What the constructor passes on to <c>HDDisplay_Ctor</c> is
	/// docs/es1/simulation/heads-down-display.md#open.
	/// </summary>
	protected Es1HddPage(Es1HddDisplay display, Es1Rect column, Es1Rect screen) {
		Display = display;
		Column = column;
		Screen = screen;
	}

	/// <summary>The display that owns the page.</summary>
	public Es1HddDisplay Display { get; }

	/// <summary>The column rect the display hands the page: the order column for the command screen, the damage column for the damage screen.</summary>
	public Es1Rect Column { get; }

	/// <summary>The display's screen rect.</summary>
	public Es1Rect Screen { get; }

	/// <summary><c>+8</c>, the display's paint stage, 2 or 1, which <see cref="Es1HddDisplay.Update"/> stores before the page's update.</summary>
	public int Stage { get; set; }

	/// <summary>Slot <c>+4</c>, the repaint, which the display's repaint and its full-repaint stage call; 0 in the base's table.</summary>
	public virtual void Repaint() {
	}

	/// <summary>Slot <c>+8</c>, the update, which the display's paint stages call with <see cref="Stage"/> set; 0 in the base's table.</summary>
	public virtual void Update() {
	}

	/// <summary>Slot <c>+0xc</c>, the hide, <c>HDDisplay_HideNoOp</c> (<c>0003c248</c>) in the base's table: nothing.</summary>
	public virtual void Hide() {
	}

	/// <summary>Slot <c>+0x10</c>, the show, <c>HDDisplay_ShowNoOp</c> (<c>0003c24b</c>) in the base's table: nothing.</summary>
	public virtual void Show() {
	}

	/// <summary>Slot <c>+0x14</c>, the key slot, <c>HDDisplay_KeyDispatchNone</c> (<c>0003c24f</c>) in the base's table: 0.</summary>
	public virtual short KeyDispatch(ushort code) => 0;
}

/// <summary>
/// Page 1, the damage screen, <c>00042164</c>(0, display, the damage column, the screen rect)
/// (docs/es1/simulation/heads-down-display.md#pages-and-visibility), with the field the display's title reads.
/// PLACEHOLDER: the screen's object file is unread (docs/es1/simulation/heads-down-display.md#open), so it is the pages'
/// base with its slots and <see cref="TitleIndex"/> at 0, so the title reads <c>" STRUCT DAMAGE"</c>; the rest of the
/// cockpit after plan step 8 (docs/herculan/plan-es1-first-mission.md).
/// </summary>
public sealed class Es1HddDamageScreen : Es1HddPage {
	internal Es1HddDamageScreen(Es1HddDisplay display, Es1Rect damageColumn, Es1Rect screen)
		: base(display, damageColumn, screen) {
	}

	/// <summary><c>+0x74</c>, the entry of <c>STRINGS0.STR</c> group 11 that <see cref="Es1HddDisplay.SetTitle"/> shows on page 1.</summary>
	public int TitleIndex { get; set; }
}
