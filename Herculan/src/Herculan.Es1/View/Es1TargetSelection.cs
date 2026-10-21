using Herculan.Es1.Sim;

namespace Herculan.Es1.View;

/// <summary>
/// The cockpit's target selection (docs/es1/simulation/cockpit-panel.md#the-target-selection): the selection
/// <c>+0x20c</c>, its walk's cursor <c>+0x210</c> into <c>LiveObjectList</c> (<c>000bc416</c>) and the cursor's
/// <c>int16</c> index <c>+0x214</c>, both at the list's start as <c>CockpitView_Ctor</c> leaves them; and
/// <c>TargetSelect_CanTarget</c>, <c>TargetSelect_InForwardCone</c>, <c>TargetSelect_SetObject</c> and
/// <c>TargetSelect_Cycle</c> (<c>ES1DBSIM 0002d624</c>–<c>0002d898</c>), measured from the cockpit's machine
/// <c>+0x1ff</c>. A new selection goes to the roving gunsight <c>+0x1f1</c>'s state block. The cursor is kept as the
/// index alone, the pointer being the list's start plus it.
///
/// <para>With no machine, through which retail reads, nothing can be targeted, and an object with no group, whose side
/// retail reads through, is never targetable: engine guards, as in <see cref="Es1SensorModel"/>.</para>
/// </summary>
public sealed class Es1TargetSelection {
	/// <summary>The radar range, the dword <c>000bd4fc</c>: the last of the scanner's three ranges, which its display's constructor stores.</summary>
	public const int RadarRange = 120000;

	/// <summary>The forward cone's half-width, either way, its bound included.</summary>
	public const int ConeHalfWidth = 8999;

	private readonly Es1Cockpit _cockpit;

	internal Es1TargetSelection(Es1Cockpit cockpit) {
		_cockpit = cockpit;
	}

	/// <summary><c>+0x20c</c>, the selection, or null.</summary>
	public IEs1SimObject? Selection { get; set; }

	/// <summary><c>+0x214</c>, the walk's cursor: the index of the list entry <c>+0x210</c> points at.</summary>
	public short Cursor { get; private set; }

	private List<IEs1SimObject> Live => _cockpit.Globals.LiveObjects;

	/// <summary>
	/// <c>TargetSelect_CanTarget</c> (<c>0002d624</c>)(cockpit, <paramref name="target"/>): neither immobilized
	/// (<c>+0x9e</c>) nor destroyed (<c>+0x93</c>), on another side (its group's <c>+0x14</c>) than the machine's, and
	/// either radar-visible (<c>+0x8f</c>) within <see cref="RadarRange"/> or the machine's contact
	/// (<c>+0xbc</c> + its slot) within <c>Detection_ContactScanRange</c>, each distance strictly less, by
	/// <c>Math_DistanceBetweenPoints</c> from the object to the machine.
	/// </summary>
	public bool CanTarget(IEs1SimObject target) {
		if (_cockpit.Machine is not { } machine || target.Group is not { } group || machine.Group is not { } own) {
			return false;
		}

		int distance = target.Position.ApproxDistanceTo(machine.Position);
		int scanRange = Es1SensorModel.ContactScanRange(_cockpit.Globals);
		return !target.Immobilized && !target.Destroyed && group.Side != own.Side
			&& ((target.RadarVisible && distance < RadarRange)
				|| (Es1SensorTables.Get(machine.Sensors.Contacts, target.ListSlot) && distance < scanRange));
	}

	/// <summary>
	/// <c>TargetSelect_InForwardCone</c> (<c>0002d71c</c>)(cockpit, <paramref name="target"/>): the machine's slot
	/// <c>+0x40</c>, its turret twist, plus <c>Math_HeadingToward</c> from its point to the object's less its heading, as
	/// an <c>int16</c>, within ±<see cref="ConeHalfWidth"/>.
	/// </summary>
	public bool InForwardCone(IEs1SimObject target) {
		if (_cockpit.Machine is not { } machine) {
			return false;
		}

		short bearing = Es1MissionGroup.HeadingToward(machine.Position, target.Position);
		short off = unchecked((short)(machine.TorsoTwistAngle + (short)(bearing - machine.Heading)));
		return off >= -ConeHalfWidth && off <= ConeHalfWidth;
	}

	/// <summary>
	/// <c>TargetSelect_SetObject</c> (<c>0002d78c</c>)(cockpit, <paramref name="target"/>): <see cref="Cycle"/> when there
	/// is no selection; then, from the selection it holds, until the selection is the object and passes both tests, or is
	/// back at the one it began with, the selection steps to the entry at the cursor, the cursor moving on, and past the
	/// list's count the selection is null and the cursor back at the list's start. A selection that ends on the object
	/// goes to the gunsight, and the answer is true. The cursor runs on its own, so the walk can come back to its start
	/// before it reaches the object, and the next call goes on from where this one stopped.
	///
	/// <para>The object can be one the list does not hold, a zone shape the gunsight's pick found
	/// (<see cref="Es1ObjectPick"/>): the walk then never reaches it and stops back at the selection it began with.</para>
	///
	/// <para>A selection that is not in the list, which retail's walk never comes back to, ends the walk after a turn of
	/// it here, an engine guard; the list grows only at the mission start, so it holds every object the selection takes.</para>
	/// </summary>
	public bool SetObject(object target) {
		if (Selection == null) {
			Cycle();
		}

		var start = Selection;
		var live = Live;
		for (int step = 0; step <= live.Count + 1; step++) {
			if (Selection is { } selection && ReferenceEquals(selection, target) && CanTarget(selection)
					&& InForwardCone(selection)) {
				break;
			}

			short at = Cursor++;
			Selection = at < live.Count ? live[at] : null;
			if (Selection == null) {
				Cursor = 0;
			}

			if (ReferenceEquals(Selection, start)) {
				break;
			}
		}

		if (!ReferenceEquals(Selection, target)) {
			return false;
		}

		HandToGunsight();
		return true;
	}

	/// <summary>
	/// <c>TargetSelect_Cycle</c> (<c>0002d898</c>)(cockpit): with no selection the list's first object when it passes
	/// both tests; otherwise the first after the selection, or after that first object, that passes both, walking with
	/// <c>ObjectList_Next</c> (<c>0000dba4</c>, the entry after its slot <c>+0x45</c>, wrapping) and stopping when the
	/// walk is back at its start. A new object becomes the selection and goes to the gunsight. Returns the selection.
	/// With an empty list, which retail never has (the player's machine is in it), nothing changes.
	/// </summary>
	public IEs1SimObject? Cycle() {
		var live = Live;
		if (live.Count == 0) {
			return Selection;
		}

		var at = Selection;
		var start = at;
		bool found = false;
		if (at == null) {
			at = live[0];
			start = at;
			if (CanTarget(at) && InForwardCone(at)) {
				found = true;
				start = null;
			}
		}

		while (!found) {
			at = live[(at!.ListSlot + 1) % live.Count];
			found = ReferenceEquals(at, start) || (CanTarget(at) && InForwardCone(at));
		}

		if (!ReferenceEquals(at, start)) {
			Selection = at;
			HandToGunsight();
		}

		return Selection;
	}

	/// <summary>
	/// The selection given to the roving gunsight: the block <c>Gunsight_GetStateBlock</c> returns, the gunsight's own,
	/// takes it at offset 6 with its byte <c>0x24</c> set, and goes to <c>Gunsight_SetValues</c>.
	/// </summary>
	private void HandToGunsight() {
		var gunsight = _cockpit.Gunsight;
		var block = gunsight.GetStateBlock();
		block.Selection = Selection;
		block.SelectionArmed = true;
		gunsight.SetValues(block);
	}
}
