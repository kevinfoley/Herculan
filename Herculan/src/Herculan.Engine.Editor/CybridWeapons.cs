using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io.Transform.Common;
using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Content;
using Herculan.Engine.Shell;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// What a Cybrid herc's hardpoints offer in the editor, by the user's choice
/// (docs/herculan/handoff-editor-roster.md#a-cybrids-weapons): its hardpoints are the fit slots its chassis's <c>gl\&lt;NAME&gt;.GL</c>
/// hardpoints address (docs/retail/formats/gun-layout-gl.md#record), and each offers the weapons the install's retail
/// missions fit there on that chassis (<see cref="RetailMountFits"/>), read from the archives as they shipped, not from a
/// mission saved over one. A weapon chosen takes the ammunition type those records carry with it there most often.
///
/// <para>Built once per editor run, the first time a Cybrid's hardpoints are drawn: every mission the shell's archives hold
/// (<see cref="ArchivedMissions"/>), and each chassis's hardpoint list from the simulator's. With no mission there, nothing is
/// offered and the hardpoints are read-only.</para>
///
/// <para>The Cybrid Loadouts setting's Unrestricted (<see cref="EditorSettings.CybridLoadouts"/>, the user's choice) offers
/// any HERC weapon instead (<see cref="Offered"/>).</para>
/// </summary>
internal sealed class CybridWeapons {
	private const string HardpointFolder = "gl";
	private const string HardpointExtension = ".GL";

	/// <summary>The <c>MECHS.NAM</c> name of the one chassis that carries <see cref="PitbullWeapons"/>.</summary>
	private const string Pitbull = "PITBULL";

	/// <summary>
	/// BEMP, BPBW and BMSL. Retail fits BEMP and BMSL on PITBULL alone, and BPBW on nothing; by the user's choice
	/// they stay on PITBULL, and it takes no other weapon.
	/// </summary>
	private static readonly short[] PitbullWeapons = [19, 20, 21];

	private readonly GameContent _simulator;
	private readonly ArchivedMissions _missions;
	private readonly EditorSettings? _settings;
	private readonly Dictionary<int, IReadOnlyList<int>> _hardPoints = new();
	private UnitTypeNames? _names;
	private RetailMountFits? _fits;
	private bool _scanned;

	/// <param name="simulator">DBSIM's mount of the install, which holds the chassis names and hardpoint lists.</param>
	/// <param name="missions">The missions the install's archives hold.</param>
	/// <param name="settings">Whose Cybrid Loadouts choice <see cref="Offered"/> follows; Vanilla without.</param>
	public CybridWeapons(GameContent simulator, ArchivedMissions missions, EditorSettings? settings = null) {
		_simulator = simulator;
		_missions = missions;
		_settings = settings;
	}

	/// <summary>Whether the Cybrid Loadouts setting is Unrestricted, so <see cref="Offered"/> is not retail's fits.</summary>
	public bool Unrestricted => _settings?.CybridLoadouts == CybridLoadouts.Unrestricted;

	/// <summary>Whether the install's archives hold any mission to read the fits from.</summary>
	public bool HasMissions => Fits() != null;

	/// <summary>The fit slots <c>MECHS.NAM</c> type <paramref name="type"/>'s hardpoints address, ascending; none without a hardpoint list.</summary>
	public IReadOnlyList<int> Hardpoints(int type) {
		if (_hardPoints.TryGetValue(type, out var slots)) {
			return slots;
		}

		_names ??= UnitTypeNames.LoadMechs(_simulator);
		var layout = _names[type] is { } name && _simulator.Read(HardpointFolder, name + HardpointExtension) is { } bytes
			? new GunLayoutTransformer().Parse(bytes)
			: null;
		slots = layout?.Hardpoints?.Select(hardpoint => (int)hardpoint.LoadoutSlot).Distinct().Order().ToList() ?? [];
		_hardPoints[type] = slots;
		return slots;
	}

	/// <summary>
	/// The weapon ids retail missions fit in fit slot <paramref name="slot"/> of type <paramref name="type"/>, <c>0</c> where
	/// one leaves it empty, in the weapons screen's order; none without missions.
	/// </summary>
	public IReadOnlyList<short> WeaponsFor(int type, int slot) =>
		Fits()?.Weapons(type, slot).OrderBy(WeaponsScreenOrder).ToList() ?? [];

	/// <summary>
	/// The weapon ids fit slot <paramref name="slot"/> of type <paramref name="type"/> offers: under Vanilla those retail
	/// missions fit there (<see cref="WeaponsFor"/>), under Unrestricted <see cref="UnrestrictedOffer"/>.
	/// </summary>
	public IReadOnlyList<short> Offered(int type, int slot) => Unrestricted ? UnrestrictedOffer(type) : WeaponsFor(type, slot);

	/// <summary>
	/// The weapon ids each hardpoint of type <paramref name="type"/> offers under Unrestricted, whichever choice the setting
	/// holds: <see cref="PitbullWeapons"/> on PITBULL, and on every other chassis every weapon of the weapons screen's list but
	/// those, a list that holds every other weapon a retail Cybrid carries; <c>0</c> last on both.
	/// </summary>
	public IReadOnlyList<short> UnrestrictedOffer(int type) {
		_names ??= UnitTypeNames.LoadMechs(_simulator);
		return _names[type] == Pitbull
			? [.. PitbullWeapons, 0]
			: ShellWeaponsScreen.RowWeapons.Select(weapon => (short)weapon).Where(weapon => !PitbullWeapons.Contains(weapon)).ToList();
	}

	/// <summary>
	/// The ammunition type retail records carry most often with <paramref name="weapon"/> in fit slot <paramref name="slot"/>
	/// of type <paramref name="type"/>, the filler 5 for a weapon no retail record fits there.
	/// </summary>
	public short AmmoFor(int type, int slot, short weapon) => Fits()?.Ammo(type, slot, weapon) ?? RosterFit.Filler;

	// The weapons screen's order, then a weapon it does not list, by id, and no weapon last.
	private static int WeaponsScreenOrder(short weapon) {
		int row = ShellWeaponsScreen.RowOfWeapon(weapon);
		return weapon == 0 ? int.MaxValue : row >= 0 ? row : ShellWeaponsScreen.RowWeapons.Length + weapon;
	}

	private RetailMountFits? Fits() {
		if (_scanned) {
			return _fits;
		}

		_scanned = true;
		var missions = _missions.All.Select(mission => mission.Mission).ToList();
		_fits = missions.Count > 0 ? RetailMountFits.Scan(missions) : null;
		return _fits;
	}
}
