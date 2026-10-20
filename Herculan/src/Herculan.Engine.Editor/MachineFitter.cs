using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Editor;

/// <summary>
/// Changes the chassis and the weapon fit of a herc: a row-12 record of the <c>.MSN</c>, addressed by the
/// <c>script.dat</c> block-7 slot it was exported to, as one step of <see cref="PointMover"/>'s undo history each. For the
/// player's squad the choices are the shell's: the nine chassis the practice screen's Herc Type steps through, and in each
/// mount the weapons the weapons screen lets the player fit there (docs/herculan/handoff-editor-roster.md#the-players-squad). A herc outside the squad is offered every type of <c>nam\MECHS.NAM</c> (<see cref="RosterFitView"/>).
///
/// <list type="bullet">
/// <item>A chassis change resets the fit to the one a new herc of that type gets (<see cref="NewHercFit"/>): a Human
/// chassis its stock fit (<see cref="ShellTrainingLaunch.StockFit"/>, as a Herc Type choice gives it), each mount below the
/// capacity taking its weapon or <c>None</c> (0) and its guidance, and every slot past it <c>-1</c> with the filler 5, as the
/// retail squad records carry them; a Cybrid the fit <see cref="HercGroups.CybridFit"/> gives, as an add does.</item>
/// <item>A mount offers each weapon of the weapons screen's list (<see cref="ShellWeaponsScreen.RowWeapons"/>) whose chassis
/// layout has a record for it in that socket (<see cref="ShellArmingLayouts.HasSocket"/>). The career's unlock flags and
/// the armory's stock, which also gate that list, are not applied: a mission's machine is fitted from no stock
/// (<c>Herc_FitNewUnit</c>). Only the nine Human chassis have a layout; a Cybrid's mounts offer nothing.</item>
/// <item>A weapon fitted takes the guidance <see cref="ShellHangar.FitGuidance"/> gives it, and a weapon that takes a
/// guidance kind (<see cref="ShellWeaponsScreen.TakesGuidance"/>) offers the four the guidance buttons write.</item>
/// </list>
///
/// <para>Each edit writes its fields through <see cref="RosterFit.Write"/>, all of an edit's in one
/// <see cref="MissionDocument.Change"/>: asked to write the alternative, a field the herc draws from an edited table's
/// alternative goes there, and every other field goes in the record that supplies it, a field a variant supplies that the
/// edit would unset keeping the variant's value, except a launcher's guidance, which is written SARH (0). The squad's edits
/// never write the alternative. Each builds the editor's state again (<see cref="SceneSync"/>), which builds the squad's
/// <c>player.mec</c> again first.</para>
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered, since the rebuild
/// changes what the panels list.</para>
/// </summary>
internal sealed class MachineFitter {
	private const int MechBlock = 7;

	/// <summary>The weapon slots a row-12 record carries.</summary>
	public const int Slots = RosterFit.Slots;

	/// <summary>A slot past the capacity, as the retail squad records carry it.</summary>
	private const short NoSlot = -1;

	private readonly ScriptDat _document;
	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly Func<GameContent> _shellContent;
	private ShellArmingLayouts? _layouts;
	private Func<string?>? _requested;

	/// <param name="document">The generated <c>script.dat</c>, <paramref name="source"/>'s.</param>
	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	/// <param name="shellContent">VSHELL's mount of the install, which holds the stock fits and the arming layouts.</param>
	public MachineFitter(ScriptDat document, MissionDocument source, PointMover mover, SceneSync sync,
			Func<GameContent> shellContent) {
		_document = document;
		_source = source;
		_mover = mover;
		_sync = sync;
		_shellContent = shellContent;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>The chassis the shell lets the player take, in type order.</summary>
	public static IEnumerable<int> Chassis => Enumerable.Range(0, ShellPracticeScreen.HercTypeCount);

	/// <summary><c>Herc_CapacityForType</c> (<c>00410d54</c>): how many mounts a chassis has, which the squad build fits.</summary>
	public static int Capacity(int chassis) => Math.Max(ShellBayMachine.Delivered(chassis).MountCapacity, 0);

	/// <summary>
	/// The block-7 slot of the herc group 0's member slot <paramref name="member"/> names, as the squad build reads it
	/// (<see cref="HercWorks.Core.Io.Transform.Common.MissionGenerator.Herc"/>): the player's, slot 0, a negative ref naming
	/// the first record; a squadmate's only while set, since the squad's positions end at the first unset one. Null
	/// otherwise, and for a ref past the block.
	/// </summary>
	public int? SquadSlot(int member) {
		if (_document.Groups.Length == 0 || member < 0 || member >= _document.Groups[0].MemberRefs.Length
				|| (member > 0 && _document.Groups[0].MemberRefs[member] < 0)) {
			return null;
		}

		int slot = Math.Max((int)_document.Groups[0].MemberRefs[member], 0);
		return slot < _document.Mechs.Length ? slot : null;
	}

	/// <summary>The block-7 record exported at <paramref name="slot"/>, or null past the block.</summary>
	public ScriptMechRecord? Record(int slot) => slot >= 0 && slot < _document.Mechs.Length ? _document.Mechs[slot] : null;

	/// <summary>Why the record at block-7 slot <paramref name="slot"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(int slot) =>
		Record(slot) == null || _source.GuidOf(MechBlock, slot) == -1 ? $"Herc slot {slot} is not in the file." : null;

	/// <summary>
	/// The weapons mount <paramref name="mount"/> of <paramref name="chassis"/> takes, in the weapons screen's order; none
	/// for a chassis with no arming layout.
	/// </summary>
	public IEnumerable<int> WeaponsFor(int chassis, int mount) {
		var layouts = Layouts();
		return ShellWeaponsScreen.RowWeapons.Where(weapon => layouts?.HasSocket(chassis, weapon, mount) == true);
	}

	/// <summary>Whether the install has an arming layout for <paramref name="chassis"/>, which says what each of its mounts takes.</summary>
	public bool HasLayout(int chassis) => Layouts()?.Layout(chassis) != null;

	/// <summary>
	/// The variant alternative an edit of the herc at <paramref name="slot"/> asked to write the alternative changes: the one
	/// its type, or a weapon or an ammunition type of a mount the view edits, is drawn from
	/// (<see cref="RosterFit.DrawnAlternative"/>); null when none is, or the herc cannot be edited.
	/// </summary>
	public MsnRecordId? DrawnAlternative(int slot) =>
		WhyFixed(slot) == null
			? RosterFit.DrawnAlternative(_source, _source.GuidOf(MechBlock, slot), FitFields(slot))
			: null;

	/// <summary>Whether some of the fields the herc's view edits are drawn from an alternative and some are its own.</summary>
	public bool PartlyDrawn(int slot) {
		if (WhyFixed(slot) != null) {
			return false;
		}

		short guid = _source.GuidOf(MechBlock, slot);
		var drawn = FitFields(slot).Select(field => VariantEditing.WhyNoDrawnField(_source, field, guid) == null).ToList();
		return drawn.Contains(true) && drawn.Contains(false);
	}

	/// <summary>
	/// Asks for the herc at <paramref name="slot"/> to become <paramref name="chassis"/> with the fit a new herc of it gets,
	/// written to the alternative it draws them from when <paramref name="alternative"/>.
	/// </summary>
	public void RequestChassis(int slot, int chassis, bool alternative = false) =>
		_requested = () => SetChassis(slot, chassis, alternative);

	/// <summary>Asks for <paramref name="weapon"/> in mount <paramref name="mount"/> of the herc at <paramref name="slot"/>.</summary>
	public void RequestWeapon(int slot, int mount, int weapon, bool alternative = false) => _requested = () => Write(slot,
		[(MsnField.MechWeapon(mount), (short)weapon), (MsnField.MechAmmo(mount), (short)ShellHangar.FitGuidance(weapon))], alternative);

	/// <summary>Asks for guidance kind <paramref name="kind"/> in mount <paramref name="mount"/> of the herc at <paramref name="slot"/>.</summary>
	public void RequestGuidance(int slot, int mount, int kind, bool alternative = false) => _requested = () => Write(slot,
		[(MsnField.MechAmmo(mount), (short)kind)], alternative);

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	/// <summary>
	/// The ten slots' weapons and ammunition types of <paramref name="chassis"/>'s stock fit as a row-12 record carries
	/// them: each mount below the capacity its weapon or <c>None</c> (0) and its guidance, every slot past it <c>-1</c> with
	/// the filler 5. Null when the install has no stock fit for it.
	/// </summary>
	public (short[] Weapons, short[] Guidance)? StockFit(int chassis) {
		if (ShellTrainingLaunch.StockFit(_shellContent(), chassis) is not { } stock) {
			return null;
		}

		int capacity = Capacity(chassis);
		var weapons = new short[Slots];
		var guidance = new short[Slots];
		for (int mount = 0; mount < Slots; mount++) {
			bool fitted = mount < capacity;
			weapons[mount] = fitted ? (short)stock.WeaponAt(mount) : NoSlot;
			guidance[mount] = (short)(fitted ? stock.Mount(mount)?.Guidance ?? ShellWeaponUnit.NoGuidance : ShellWeaponUnit.NoGuidance);
		}

		return (weapons, guidance);
	}

	/// <summary>
	/// The weapons and ammunition types a new herc of <c>MECHS.NAM</c> type <paramref name="type"/> is fitted with: a Human
	/// chassis's stock fit (<see cref="StockFit"/>), a Cybrid's <see cref="HercGroups.CybridFit"/>; null when there are none
	/// to give.
	/// </summary>
	public (short[] Weapons, short[] Ammo)? NewHercFit(int type) =>
		HercGroups.IsCybrid(type) ? HercGroups.CybridFit(type) : StockFit(type);

	private string? SetChassis(int slot, int chassis, bool alternative) {
		if (NewHercFit(chassis) is not var (weapons, guidance)) {
			return HercGroups.IsCybrid(chassis) ? $"There is no herc type {chassis}." : $"The install has no stock fit for chassis {chassis}.";
		}

		return Write(slot, RosterFit.Chassis((short)chassis, weapons, guidance), alternative);
	}

	// Writes each of <fields> whose generated value differs, as one change and one step of the history.
	private string? Write(int slot, IReadOnlyList<(MsnField Field, short Value)> fields, bool alternative) {
		if (WhyFixed(slot) is { } reason) {
			return reason;
		}

		var source = _source;
		short guid = source.GuidOf(MechBlock, slot);
		if (!RosterFit.Differs(source, guid, fields)) {
			return null;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => RosterFit.Write(source, guid, fields, alternative, weapon => ShellWeaponsScreen.TakesGuidance(weapon)));
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		// Nothing written, when every field that differs is one a variant keeps.
		if (change.Rows.Any()) {
			_mover.Record(new RecordsEdit(_sync, change));
		}

		return null;
	}

	// The fields the herc's view edits: its type, and for a chassis with an arming layout each mount's weapon and
	// ammunition type.
	private IEnumerable<MsnField> FitFields(int slot) {
		yield return MsnField.MechType;
		int chassis = Record(slot)!.TypeIndex;
		if (!HasLayout(chassis)) {
			yield break;
		}

		for (int mount = 0; mount < Capacity(chassis) && mount < Slots; mount++) {
			yield return MsnField.MechWeapon(mount);
			yield return MsnField.MechAmmo(mount);
		}
	}

	private ShellArmingLayouts? Layouts() => _layouts ??= ShellArmingLayouts.Load(_shellContent());
}
