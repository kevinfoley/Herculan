using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Data.File.Msn.Script;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;

namespace Herculan.Engine.Editor;

/// <summary>
/// Changes the chassis and the weapon fit of a herc of the player's squad: a row-12 record of the <c>.MSN</c>, addressed by
/// the <c>script.dat</c> block-7 slot it was exported to, as one step of <see cref="PointMover"/>'s undo history each. The
/// choices are the shell's: the nine chassis the practice screen's Herc Type steps through, and in each mount the weapons
/// the weapons screen lets the player fit there (docs/herculan/handoff-editor-mission-editing.md#in-place, "The player's
/// machine").
///
/// <list type="bullet">
/// <item>A chassis change resets the fit to that chassis's stock fit (<see cref="ShellTrainingLaunch.StockFit"/>, as a
/// Herc Type choice gives it), each mount below the capacity taking its weapon or <c>None</c> (0) and its guidance, and
/// every slot past it <c>-1</c> with the filler 5, as the retail squad records carry them.</item>
/// <item>A mount offers each weapon of the weapons screen's list (<see cref="ShellWeaponsScreen.RowWeapons"/>) whose chassis
/// layout has a record for it in that socket (<see cref="ShellArmingLayouts.HasSocket"/>). The career's unlock flags and
/// the armory's stock, which also gate that list, are not applied: a mission's machine is fitted from no stock
/// (<c>Herc_FitNewUnit</c>).</item>
/// <item>A weapon fitted takes the guidance <see cref="ShellHangar.FitGuidance"/> gives it, and a weapon that takes a
/// guidance kind (<see cref="ShellWeaponsScreen.TakesGuidance"/>) offers the four the guidance buttons write.</item>
/// </list>
///
/// <para>Each edit writes the record that supplies the field through <see cref="MissionDocument.SetField"/>, all of an
/// edit's fields in one <see cref="MissionDocument.Change"/>, and builds the editor's state again
/// (<see cref="MissionRebuild"/>), which builds the squad's <c>player.mec</c> again first. A field a variant supplies that
/// the edit would unset keeps the variant's value, except a launcher's guidance, which is written SARH (0): the editor's
/// choice (docs/herculan/handoff-editor-mission-editing.md#gaps-in-what-is-in-place, "The player's machine").</para>
///
/// <para>The edits run when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered, since the rebuild
/// changes what the panels list.</para>
/// </summary>
internal sealed class MachineFitter {
	private const int MechBlock = 7;

	/// <summary>The weapon slots a row-12 record carries.</summary>
	public const int Slots = 10;

	/// <summary>A slot past the capacity, as the retail squad records carry it.</summary>
	private const short NoSlot = -1;

	/// <summary>The guidance kind DBSIM reads the filler 5 as on a launcher.</summary>
	private const short Sarh = 0;

	private readonly ScriptDat _document;
	private readonly MissionDocument? _source;
	private readonly PointMover _mover;
	private readonly MissionRebuild _rebuild;
	private readonly Func<GameContent>? _shellContent;
	private ShellArmingLayouts? _layouts;
	private Func<string?>? _requested;

	/// <param name="document">The generated <c>script.dat</c>, <paramref name="source"/>'s.</param>
	/// <param name="source">The mission as its <c>.MSN</c>, or null for a plain <c>script.dat</c>, which is not edited.</param>
	/// <param name="shellContent">VSHELL's mount of the install, which holds the stock fits and the arming layouts.</param>
	public MachineFitter(ScriptDat document, MissionDocument? source, PointMover mover, MissionRebuild rebuild,
			Func<GameContent>? shellContent) {
		_document = document;
		_source = source;
		_mover = mover;
		_rebuild = rebuild;
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
	/// the first record; a wingman's only while set, since the squad's positions end at the first unset one. Null otherwise,
	/// and for a ref past the block.
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
		_mover.WhyReadOnly is { } readOnly ? readOnly
			: Record(slot) == null || _source!.GuidOf(MechBlock, slot) == -1 ? $"Herc slot {slot} is not in the file."
			: _shellContent == null ? "The install's shell files are not mounted."
			: null;

	/// <summary>
	/// The weapons mount <paramref name="mount"/> of <paramref name="chassis"/> takes, in the weapons screen's order; none
	/// for a chassis with no arming layout.
	/// </summary>
	public IEnumerable<int> WeaponsFor(int chassis, int mount) {
		var layouts = Layouts();
		return ShellWeaponsScreen.RowWeapons.Where(weapon => layouts?.HasSocket(chassis, weapon, mount) == true);
	}

	/// <summary>Asks for the herc at <paramref name="slot"/> to become <paramref name="chassis"/> with its stock fit.</summary>
	public void RequestChassis(int slot, int chassis) => _requested = () => SetChassis(slot, chassis);

	/// <summary>Asks for <paramref name="weapon"/> in mount <paramref name="mount"/> of the herc at <paramref name="slot"/>.</summary>
	public void RequestWeapon(int slot, int mount, int weapon) => _requested = () => Write(slot,
		[(MsnField.MechWeapon(mount), (short)weapon), (MsnField.MechAmmo(mount), (short)ShellHangar.FitGuidance(weapon))]);

	/// <summary>Asks for guidance kind <paramref name="kind"/> in mount <paramref name="mount"/> of the herc at <paramref name="slot"/>.</summary>
	public void RequestGuidance(int slot, int mount, int kind) => _requested = () => Write(slot,
		[(MsnField.MechAmmo(mount), (short)kind)]);

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is { } edit) {
			_requested = null;
			Failure = edit();
		}
	}

	private string? SetChassis(int slot, int chassis) {
		if (_shellContent == null || ShellTrainingLaunch.StockFit(_shellContent(), chassis) is not { } stock) {
			return $"The install has no stock fit for chassis {chassis}.";
		}

		int capacity = Capacity(chassis);
		var fields = new List<(MsnField, short)> { (MsnField.MechType, (short)chassis) };
		for (int mount = 0; mount < Slots; mount++) {
			bool fitted = mount < capacity;
			fields.Add((MsnField.MechWeapon(mount), fitted ? (short)stock.WeaponAt(mount) : NoSlot));
			fields.Add((MsnField.MechAmmo(mount),
				(short)(fitted ? stock.Mount(mount)?.Guidance ?? ShellWeaponUnit.NoGuidance : ShellWeaponUnit.NoGuidance)));
		}

		return Write(slot, fields);
	}

	// Writes each of <fields> whose generated value differs, as one change and one step of the history.
	private string? Write(int slot, IReadOnlyList<(MsnField Field, short Value)> fields) {
		if (WhyFixed(slot) is { } reason) {
			return reason;
		}

		var source = _source!;
		short guid = source.GuidOf(MechBlock, slot);
		if (!fields.Any(field => Current(slot, field.Field) != field.Value)) {
			return null;
		}

		MsnEdit change;
		try {
			change = _rebuild.Run(() => source.Change(() => {
				foreach (var (field, value) in fields) {
					if (Current(slot, field) == value) {
						continue;
					}

					if (value != field.Unset || !source.SuppliedByVariant(field, guid)) {
						source.SetField(field, guid, value);
					}

					// An unset value a variant still supplies something else for.
					if (Current(slot, field) != value && field.Word >= MechRosterEntry144.WeaponSecondaryWord
							&& Launcher(slot, field.Word) && Current(slot, field) != Sarh) {
						source.SetField(field, guid, Sarh);
					}
				}
			}));
		} catch (InvalidOperationException ex) {
			return ex.Message;
		}

		// Nothing written, when every field that differs is one a variant keeps.
		if (change.Rows.Any()) {
			_mover.Record(new RecordsEdit(_rebuild, change));
		}

		return null;
	}

	// Whether the mount whose ammunition type is at row-12 word <ammoWord> holds, as generated now, a weapon that takes a
	// guidance kind.
	private bool Launcher(int slot, int ammoWord) {
		int mount = ammoWord - MechRosterEntry144.WeaponSecondaryWord;
		return Record(slot) is { } record && ShellWeaponsScreen.TakesGuidance(record.WeaponRefs[mount]);
	}

	// The generated value of a row-12 field of the record at block-7 slot <slot>.
	private short Current(int slot, MsnField field) {
		var record = Record(slot)!;
		int word = field.Word;
		if (word == MechRosterEntry144.TypeIndexWord) {
			return record.TypeIndex;
		}

		if (word >= MechRosterEntry144.WeaponSecondaryWord) {
			var ammo = record.WeaponSecondary;
			int mount = word - MechRosterEntry144.WeaponSecondaryWord;
			return mount < ammo.Length ? ammo[mount] : (short)ShellWeaponUnit.NoGuidance;
		}

		return record.WeaponRefs[word - MechRosterEntry144.WeaponRefsWord];
	}

	private ShellArmingLayouts? Layouts() => _layouts ??= _shellContent is { } content ? ShellArmingLayouts.Load(content()) : null;
}
