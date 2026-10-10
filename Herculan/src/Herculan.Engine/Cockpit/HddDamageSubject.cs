using Herculan.Engine.Content;
using Herculan.Engine.Sim;
using HercWorks.Core.Data.File;

namespace Herculan.Engine.Cockpit;

/// <summary>
/// Whose herc the Heads-Down Display's damage detail is inspecting, and how the screen captions it —
/// the display's five-slot subject selector (<c>HDDisplay+0x55c</c>) over its machine array
/// (<c>+0x534</c>) and name array (<c>+0x548</c>). The left and right arrows step it; see
/// docs/retail/simulation/heads-down-display.md#subject.
/// </summary>
/// <param name="Slot">
/// The selector: <see cref="PlayerSlot"/>, a squad slot plus one, or <see cref="TargetSlot"/>.
/// </param>
/// <param name="Caption">The subject caption's text, or empty when the string table is absent.</param>
/// <param name="Subject">
/// The machine, read the way the status screen reads one. Not <see cref="MfdStatusSubject.Present"/>
/// on the target slot while nothing is selected or the selection is not a HERC.
/// </param>
/// <param name="Hardpoints">
/// That machine's hardpoints by <c>.GL</c> slot — see <see cref="DamageHardpoint.Build"/>. Its weapons
/// view prints row <c>n</c> from entry <c>n</c>, and every view draws entry <c>n</c>'s icon where the
/// doll's <c>.PDG</c> hardpoint <c>n</c> places it. A null entry, an empty hardpoint, leaves its row
/// blank rather than showing a name left by a previous update.
/// </param>
/// <param name="NoData">
/// What the screen prints in place of a doll while there is no subject — <see cref="NoSubjectGroup"/>'s
/// <c>NO TARGET SELECTED</c> or <c>NO INFO AVAILABLE</c>. Null while there is one.
/// </param>
public readonly record struct HddDamageSubject(
	int Slot,
	string Caption,
	MfdStatusSubject Subject,
	IReadOnlyList<DamageHardpoint?> Hardpoints,
	string? NoData) {

	/// <summary>Selector entries: the player, three squad slots, the target.</summary>
	public const int SlotCount = 5;

	/// <summary>The player's own machine — where the constructor starts the selector.</summary>
	public const int PlayerSlot = 0;

	/// <summary>The current selection, when it is a HERC.</summary>
	public const int TargetSlot = 4;

	/// <summary><c>STRINGS0.STR</c> group holding the target slot's caption, <c>TARGET</c>.</summary>
	public const int TargetNameGroup = 18;

	/// <summary>
	/// Group holding <c>NO TARGET SELECTED</c> and <c>NO INFO AVAILABLE</c> — <c>HddDisplay_Update</c>
	/// (<c>00449bd0</c>) picks the first with nothing selected and the second for a selection that is
	/// not a HERC.
	/// </summary>
	public const int NoSubjectGroup = 19;

	/// <summary>Colour id the target's caption sits on — palette 13, yellow.</summary>
	public const int TargetPlateColorId = 15;

	/// <summary>The player, before anything is known about the mission.</summary>
	public static HddDamageSubject Default { get; } = new(
		PlayerSlot, string.Empty, MfdStatusSubject.None, Array.Empty<DamageHardpoint?>(), null);

	/// <summary>
	/// The selector one step on — <c>HddDisplay_NextSubject</c> (<c>0044b988</c>) for +1,
	/// <c>HddDisplay_PrevSubject</c> (<c>0044b9e0</c>) for -1. Both wrap
	/// and skip a squad slot with no machine in it. The player's slot always holds one, and the target
	/// slot is never skipped, empty or not, so it is where a step lands on its way round.
	/// </summary>
	/// <param name="slot">The current selector.</param>
	/// <param name="direction">+1 for the right arrow, -1 for the left.</param>
	/// <param name="squadSeated">Whether squad slot <c>n</c> (0-2) has a machine in it.</param>
	public static int Step(int slot, int direction, Func<int, bool> squadSeated) {
		do {
			slot = (slot + direction + SlotCount) % SlotCount;
		} while (slot != PlayerSlot && slot != TargetSlot && !squadSeated(slot - 1));

		return slot;
	}

	/// <summary>
	/// Reads the subject in <paramref name="slot"/>. The squad slots keep their machine once it is
	/// destroyed, as the display's array does, so a dead squadmate can still be inspected.
	/// </summary>
	/// <param name="slot">The selector.</param>
	/// <param name="player">The machine being flown — slot 0, and what a subject is measured from.</param>
	/// <param name="squad">The squad's machines by comm-box slot.</param>
	/// <param name="pilotName">A comm box's pilot name by slot — the caption a squadmate gets.</param>
	/// <param name="selected">The current selection.</param>
	/// <param name="strings">For the captions and the no-subject text.</param>
	public static HddDamageSubject For(int slot, SimObject? player, IReadOnlyList<SimObject?> squad,
			Func<int, string> pilotName, SimObject? selected, StringFile? strings) {
		SimObject? machine = slot switch {
			PlayerSlot => player,
			TargetSlot => selected?.TargetClass == TargetClass.Herc ? selected : null,
			_ => slot - 1 < squad.Count ? squad[slot - 1] : null,
		};

		string caption = slot switch {
			PlayerSlot => strings?.Text(MfdLayout.SelfNameGroup, 0),
			TargetSlot => strings?.Text(TargetNameGroup, 0),
			_ => pilotName(slot - 1),
		} ?? string.Empty;

		return new HddDamageSubject(slot, caption,
			MfdStatusSubject.For(machine, player, strings),
			machine is MechObject mech ? DamageHardpoint.Build(mech.Weapons) : Array.Empty<DamageHardpoint?>(),
			machine != null ? null : strings?.Text(NoSubjectGroup, selected == null ? 0 : 1));
	}

	/// <summary>
	/// The caption's font — <c>HddDamageScreen_SetSubjectCaption</c> (<c>0044ba2c</c>) gives the player
	/// <c>ColorSchemePanels[3]</c> and everyone else <c>[2]</c>.
	/// </summary>
	public string CaptionFont => Slot == PlayerSlot ? HddLayout.SubjectFont : HddLayout.PilotNameFont;

	/// <summary>
	/// The colour id the caption sits on: the player's blue, a squadmate's own comm-box colour, or the
	/// target's yellow.
	/// </summary>
	public int CaptionColorId => Slot switch {
		PlayerSlot => HudColorTable.HeadsDownSubjectPlateId,
		TargetSlot => TargetPlateColorId,
		_ => HudColorTable.PilotColorId(Slot - 1),
	};

	/// <summary>
	/// Whether the title indicator is lit — <c>HDDisplay+0x51f</c>, which <c>HddDisplay_HandleWidgetPress</c>
	/// (<c>0044a178</c>) sets on each subject step from the new subject's locally-piloted flag
	/// (<c>mech+0xa3</c>). Only the player's own machine carries it, so the indicator is yellow while the
	/// player is the subject and goes back to id 13 for anyone else.
	/// </summary>
	public bool IndicatorLit => Slot == PlayerSlot;
}
