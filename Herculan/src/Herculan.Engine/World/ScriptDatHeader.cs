using HercWorks.Core.Data.File.Msn.Script;

namespace Herculan.Engine.World;

/// <summary>
/// The 20-byte header of <c>data\script.dat</c>, the mission handoff VSHELL writes and DBSIM reads
/// (see docs/retail/formats/script-dat.md for the 13 record blocks that follow it).
///
/// <para>Two of its fields are what a scene needs before anything else, and both are decoded
/// from <c>DBSim_LoadScriptDat</c> (<c>00424308</c>), which reads the header into one global and then
/// uses it twice: it hands the whole 20 bytes to <c>maybe_World_LoadTheater</c> (which takes the
/// short at 0 and the short at 18 as <c>world&lt;index * 2 + variant&gt;</c>) and passes the short at
/// 2 straight to <c>Terrain_LoadZone</c>.</para>
///
/// <para>The offsets are <see cref="ScriptDat"/>'s; this is the engine's interpretation of them.</para>
/// </summary>
public readonly struct ScriptDatHeader {
	/// <summary>Bytes the header occupies; the original reads exactly this many in one call.</summary>
	public const int Size = ScriptDat.HeaderSize;

	/// <summary>How many difficulty levels there are; see <see cref="Difficulty"/>.</summary>
	public const int DifficultyLevels = 4;

	/// <summary>
	/// The value <see cref="UnlimitedAmmunition"/> and <see cref="PlayerInvulnerable"/> are tested
	/// against. Their readers compare for equality with 1, so a 2 in a hand-edited file is off.
	/// </summary>
	public const short CheatEnabled = 1;

	private ScriptDatHeader(int theaterIndex, int zoneIndex, int objectiveType, int theaterVariant,
			int difficulty, bool unlimitedAmmunition, bool playerInvulnerable,
			int trainingMissionNumber) {
		TheaterIndex = theaterIndex;
		ZoneIndex = zoneIndex;
		ObjectiveType = objectiveType;
		TheaterVariant = theaterVariant;
		Difficulty = difficulty;
		UnlimitedAmmunition = unlimitedAmmunition;
		PlayerInvulnerable = playerInvulnerable;
		TrainingMissionNumber = trainingMissionNumber;
	}

	/// <inheritdoc cref="ScriptDat.TheaterIndex"/>
	/// <remarks>See <see cref="TheaterDescriptor"/>.</remarks>
	public int TheaterIndex { get; }

	/// <inheritdoc cref="ScriptDat.ZoneIndex"/>
	public int ZoneIndex { get; }

	/// <inheritdoc cref="ScriptDat.ObjectiveType"/>
	/// <remarks>
	/// See <see cref="Sim.MechObject.PlayerThink"/> and <see cref="Sim.Ai.AiTargeting.IsTargetable"/>.
	/// </remarks>
	public int ObjectiveType { get; }

	/// <inheritdoc cref="ScriptDat.TheaterVariant"/>
	public int TheaterVariant { get; }

	/// <inheritdoc cref="ScriptDat.Difficulty"/>
	/// <remarks>
	/// Three of the four tables it indexes are ported — see <see cref="Sim.SimWorld.Difficulty"/>.
	///
	/// <para><b>Clamped on the way in.</b> The original indexes those tables with whatever the file
	/// says and would read past them; a hand-edited file is held to the four levels here instead.</para>
	/// </remarks>
	public int Difficulty { get; }

	/// <inheritdoc cref="ScriptDat.UnlimitedAmmunition"/>
	/// <remarks>Both effects are in <see cref="Sim.WeaponMounts"/>.</remarks>
	public bool UnlimitedAmmunition { get; }

	/// <inheritdoc cref="ScriptDat.TrainingMissionNumber"/>
	/// <remarks>The music branch is <see cref="Audio.CdMusic.StartMission"/>.</remarks>
	public int TrainingMissionNumber { get; }

	/// <inheritdoc cref="ScriptDat.PlayerInvulnerable"/>
	public bool PlayerInvulnerable { get; }

	/// <summary>
	/// Reads the header from the start of a <c>script.dat</c>'s bytes; see <see cref="From"/>.
	/// </summary>
	public static ScriptDatHeader Read(ReadOnlySpan<byte> scriptDat) {
		if (scriptDat.Length < Size) {
			throw new InvalidDataException(
				$"script.dat is {scriptDat.Length} bytes; its header alone is {Size}.");
		}

		return From(new ScriptDat { HeaderBytes = scriptDat[..Size].ToArray() });
	}

	/// <summary>
	/// The engine's reading of a parsed <c>script.dat</c>'s header fields. The fields offsets 4 and
	/// 16 hold are left out — <c>DBSim_LoadScriptDat</c> zeroes the one at 4 before use, and 16 is
	/// unread. <see cref="ObjectiveType"/> is not the theater's: it is the mission layer's, and its
	/// reader is <c>Mech_BehaviourPlayerThink</c>.
	///
	/// <para>The two cheat flags are read as <c>== 1</c> rather than as "nonzero", which is how both
	/// of their readers spell the test.</para>
	/// </summary>
	public static ScriptDatHeader From(ScriptDat script) => new(
		script.TheaterIndex,
		script.ZoneIndex,
		script.ObjectiveType,
		script.TheaterVariant,
		System.Math.Clamp((int)script.Difficulty, 0, DifficultyLevels - 1),
		script.UnlimitedAmmunition == CheatEnabled,
		script.PlayerInvulnerable == CheatEnabled,
		script.TrainingMissionNumber);
}
