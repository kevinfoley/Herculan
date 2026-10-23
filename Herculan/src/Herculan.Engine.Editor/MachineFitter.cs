using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Editor;

/// <summary>
/// Changes the chassis and the weapon fit of a herc: a row-12 record of the <c>.MSN</c>, by its GUID, any the layout has, as
/// one step of <see cref="PointMover"/>'s undo history each. For the player's squad the choices are the shell's: the nine
/// chassis the practice screen's Herc Type steps through, and in each mount the weapons the weapons screen lets the player
/// fit there (docs/herculan/handoff-editor-roster.md#the-players-squad). A herc outside the squad is offered every type of
/// <c>nam\MECHS.NAM</c> (<see cref="RosterFitView"/>).
///
/// <list type="bullet">
/// <item>A chassis change resets the fit to the one a new herc of that type gets (<see cref="NewHercFit"/>): a Human
/// chassis its stock fit (<see cref="ShellTrainingLaunch.StockFit"/>, as a Herc Type choice gives it), each mount below the
/// capacity taking its weapon or <c>None</c> (0) and its guidance, and every slot past it <c>-1</c> with the filler 5, as the
/// retail squad records carry them; a Cybrid the fit <see cref="HercGroups.CybridFit"/> gives, as an add does.</item>
/// <item>A Human chassis's mounts are its first <see cref="Capacity"/> fit slots, and each offers each weapon of the weapons
/// screen's list (<see cref="ShellWeaponsScreen.RowWeapons"/>) whose chassis layout has a record for it in that socket
/// (<see cref="ShellArmingLayouts.HasSocket"/>). The career's unlock flags and the armory's stock, which also gate that list,
/// are not applied: a mission's machine is fitted from no stock (<c>Herc_FitNewUnit</c>). A weapon fitted takes the guidance
/// <see cref="ShellHangar.FitGuidance"/> gives it.</item>
/// <item>A Cybrid's mounts are its hardpoints, and each offers the weapons retail missions fit there
/// (<see cref="CybridWeapons"/>, the user's choice); a weapon fitted takes the ammunition type those records carry with
/// it.</item>
/// <item>A weapon that takes a guidance kind (<see cref="ShellWeaponsScreen.TakesGuidance"/>) offers the four the guidance
/// buttons write, on either kind of chassis.</item>
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
	/// <summary>The weapon slots a row-12 record carries.</summary>
	public const int Slots = RosterFit.Slots;

	/// <summary>A slot past the capacity, as the retail squad records carry it.</summary>
	private const short NoSlot = -1;

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private readonly Func<GameContent> _shellContent;
	private readonly CybridWeapons _cybrid;
	private ShellArmingLayouts? _layouts;
	private Func<string?>? _requested;

	/// <param name="source">The mission as its <c>.MSN</c>.</param>
	/// <param name="shellContent">VSHELL's mount of the install, which holds the stock fits and the arming layouts.</param>
	/// <param name="cybrid">What a Cybrid's hardpoints offer.</param>
	public MachineFitter(MissionDocument source, PointMover mover, SceneSync sync, Func<GameContent> shellContent,
			CybridWeapons cybrid) {
		_source = source;
		_mover = mover;
		_sync = sync;
		_shellContent = shellContent;
		_cybrid = cybrid;
	}

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>The chassis the shell lets the player take, in type order.</summary>
	public static IEnumerable<int> Chassis => Enumerable.Range(0, ShellPracticeScreen.HercTypeCount);

	/// <summary><c>Herc_CapacityForType</c> (<c>00410d54</c>): how many mounts a chassis has, which the squad build fits.</summary>
	public static int Capacity(int chassis) => Math.Max(ShellBayMachine.Delivered(chassis).MountCapacity, 0);

	/// <summary>Row-12 GUID <paramref name="guid"/>'s base, as the layout shows it, or null when no record carries it.</summary>
	public MechRosterEntry144? Record(short guid) => _source.Layout.Entry<MechRosterEntry144>(guid)?.Fields;

	/// <summary>Why the herc of row-12 GUID <paramref name="guid"/> cannot be edited, or null when it can.</summary>
	public string? WhyFixed(short guid) => Record(guid) == null ? "Its record is not in the mission." : null;

	/// <summary>
	/// The fit slots <paramref name="chassis"/>'s view edits, ascending: a Human chassis's first <see cref="Capacity"/>, a
	/// Cybrid's hardpoints (<see cref="CybridWeapons.Hardpoints"/>).
	/// </summary>
	public IReadOnlyList<int> Mounts(int chassis) => HercGroups.IsCybrid(chassis)
		? _cybrid.Hardpoints(chassis).Where(slot => slot < Slots).ToList()
		: Enumerable.Range(0, Math.Min(Capacity(chassis), Slots)).ToList();

	/// <summary>
	/// The weapons fit slot <paramref name="mount"/> of <paramref name="chassis"/> offers: a Human chassis's in the weapons
	/// screen's order, none without an arming layout; a Cybrid's those retail missions fit there.
	/// </summary>
	public IEnumerable<int> WeaponsFor(int chassis, int mount) {
		if (HercGroups.IsCybrid(chassis)) {
			return _cybrid.WeaponsFor(chassis, mount).Select(weapon => (int)weapon);
		}

		var layouts = Layouts();
		return ShellWeaponsScreen.RowWeapons.Where(weapon => layouts?.HasSocket(chassis, weapon, mount) == true);
	}

	/// <summary>
	/// Why fit slot <paramref name="mount"/> of <paramref name="chassis"/> offers no weapon, so its weapon is not changed in the
	/// view, or null when it offers some.
	/// </summary>
	public string? WhyNoWeapons(int chassis, int mount) {
		if (WeaponsFor(chassis, mount).Any()) {
			return null;
		}

		return !HercGroups.IsCybrid(chassis) ? "The install has no arming layout for this chassis, so its weapons are not changed here."
			: !_cybrid.HasMissions ? "The install's archives hold no mission to take this chassis's weapons from, so they are not changed here."
			: "No retail mission fits a weapon on this hardpoint, so it is not changed here.";
	}

	/// <summary>Whether a retail mission fits <paramref name="weapon"/> in fit slot <paramref name="mount"/> of Cybrid <paramref name="chassis"/>.</summary>
	public bool FromRetail(int chassis, int mount, int weapon) => _cybrid.WeaponsFor(chassis, mount).Contains((short)weapon);

	/// <summary>
	/// The variant alternative an edit of the herc of row-12 GUID <paramref name="guid"/> asked to write the alternative
	/// changes: the one its type, or a weapon or an ammunition type of a mount the view edits, is drawn from
	/// (<see cref="RosterFit.DrawnAlternative"/>); null when none is, or the herc cannot be edited.
	/// </summary>
	public MsnRecordId? DrawnAlternative(short guid) =>
		WhyFixed(guid) == null ? RosterFit.DrawnAlternative(_source, guid, FitFields(guid)) : null;

	/// <summary>Whether some of the fields the herc's view edits are drawn from an alternative and some are its own.</summary>
	public bool PartlyDrawn(short guid) {
		if (WhyFixed(guid) != null) {
			return false;
		}

		var drawn = FitFields(guid).Select(field => VariantEditing.WhyNoDrawnField(_source, field, guid) == null).ToList();
		return drawn.Contains(true) && drawn.Contains(false);
	}

	/// <summary>
	/// Asks for the herc of row-12 GUID <paramref name="guid"/> to become <paramref name="chassis"/> with the fit a new herc of
	/// it gets, written to the alternative it draws them from when <paramref name="alternative"/>.
	/// </summary>
	public void RequestChassis(short guid, int chassis, bool alternative = false) =>
		_requested = () => SetChassis(guid, chassis, alternative);

	/// <summary>Asks for <paramref name="weapon"/> in fit slot <paramref name="mount"/> of the herc of row-12 GUID <paramref name="guid"/>.</summary>
	public void RequestWeapon(short guid, int mount, int weapon, bool alternative = false) => _requested = () => Write(guid,
		[(MsnField.MechWeapon(mount), (short)weapon), (MsnField.MechAmmo(mount), AmmoFor(guid, mount, weapon))], alternative);

	/// <summary>Asks for guidance kind <paramref name="kind"/> in fit slot <paramref name="mount"/> of the herc of row-12 GUID <paramref name="guid"/>.</summary>
	public void RequestGuidance(short guid, int mount, int kind, bool alternative = false) => _requested = () => Write(guid,
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

	// The ammunition type <weapon> is fitted with in slot <mount>: what the weapons screen gives it on a Human chassis, what
	// retail missions carry with it there on a Cybrid's.
	private short AmmoFor(short guid, int mount, int weapon) =>
		Record(guid) is { } herc && HercGroups.IsCybrid(herc.TypeIndex)
			? _cybrid.AmmoFor(herc.TypeIndex, mount, (short)weapon)
			: (short)ShellHangar.FitGuidance(weapon);

	private string? SetChassis(short guid, int chassis, bool alternative) {
		if (NewHercFit(chassis) is not var (weapons, guidance)) {
			return HercGroups.IsCybrid(chassis) ? $"There is no herc type {chassis}." : $"The install has no stock fit for chassis {chassis}.";
		}

		return Write(guid, RosterFit.Chassis((short)chassis, weapons, guidance), alternative);
	}

	// Writes each of <fields> whose value differs, as one change and one step of the history.
	private string? Write(short guid, IReadOnlyList<(MsnField Field, short Value)> fields, bool alternative) {
		if (WhyFixed(guid) is { } reason) {
			return reason;
		}

		var source = _source;
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

	// The fields the herc's view edits: its type, and each mount's weapon and ammunition type.
	private IEnumerable<MsnField> FitFields(short guid) {
		yield return MsnField.MechType;
		foreach (int mount in Mounts(Record(guid)!.TypeIndex)) {
			yield return MsnField.MechWeapon(mount);
			yield return MsnField.MechAmmo(mount);
		}
	}

	private ShellArmingLayouts? Layouts() => _layouts ??= ShellArmingLayouts.Load(_shellContent());
}
