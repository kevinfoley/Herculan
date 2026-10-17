using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;
using Herculan.Engine.Numerics;

namespace Herculan.Engine.Shell;

/// <summary>Where the Herc the player pilots in a mission comes from.</summary>
public enum PlayerMachineSource {
	/// <summary>The mission's own group-0 machine: practice rows 0 to 3, and every mission loaded as <c>INSTANT ACTION</c>.</summary>
	Mission,

	/// <summary>A stock fit of the practice screen's Herc Type chassis: practice rows 4 to 7.</summary>
	HercType,

	/// <summary>The career's squad, from the install's <c>DATA\player.mec</c>: a campaign mission.</summary>
	CareerSquad,
}

/// <summary>
/// Which Hercs the squad pilots in a handoff <see cref="MissionFileLaunch"/> wrote, and how its <c>player.mec</c> was built, so the mission
/// editor can build it again from an edited mission: where the player's machine comes from, and for a mission loaded in
/// training mode the training load's squad build (<see cref="ShellTrainingLaunch.BuildSquad"/>) over the same new career,
/// its draws made again from VSHELL's generator as it stood when the open began.
/// </summary>
public sealed class MissionSquad {
	private readonly Func<MissionGenerator, byte[]?>? _export;

	private MissionSquad(PlayerMachineSource player, int chassis, Func<MissionGenerator, byte[]?>? export) {
		PlayerMachine = player;
		HercTypeChassis = chassis;
		_export = export;
	}

	/// <summary>Where the player's machine comes from.</summary>
	public PlayerMachineSource PlayerMachine { get; }

	/// <summary>The Herc Type option's chassis, which the player pilots under <see cref="PlayerMachineSource.HercType"/>; <c>-1</c> otherwise.</summary>
	public int HercTypeChassis { get; }

	/// <summary>
	/// The <c>player.mec</c> the launch would write for <paramref name="mission"/>, a generation of the mission opened; null
	/// for a campaign mission, whose squad is the career's whatever the mission holds, or when the install lacks a file the
	/// career needs.
	/// </summary>
	public byte[]? PlayerMec(MissionGenerator mission) => _export?.Invoke(mission);

	/// <summary>
	/// A training-mode load on <paramref name="row"/> (<see cref="ShellTrainingLaunch.NewCareer"/>, then
	/// <see cref="ShellTrainingLaunch.BuildSquad"/> and <see cref="ShellMissionLaunch.ExportPlayerMec"/>), whose
	/// <see cref="ShellTrainingLaunch.NewCareer"/> drew from <paramref name="start"/>, copied.
	/// </summary>
	internal static MissionSquad Training(GameContent content, SimulatorPreferences options, int row, bool instantAction,
			SimRandom start) {
		var begun = start.Copy();
		return new MissionSquad(
			ShellTrainingLaunch.PilotsMissionMachine(row, instantAction) ? PlayerMachineSource.Mission : PlayerMachineSource.HercType,
			ShellTrainingLaunch.PilotsMissionMachine(row, instantAction) ? -1 : options[ShellPracticeScreen.HercTypeOption],
			mission => {
				var random = begun.Copy();
				int Roll(short bound) => random.NextBelow(bound);
				if (ShellTrainingLaunch.NewCareer(content, options, row, Roll, held: null, out _) is not var (_, hangar)) {
					return null;
				}

				ShellTrainingLaunch.BuildSquad(content, options, row, instantAction, mission, hangar);
				return ShellMissionLaunch.ExportPlayerMec(hangar);
			});
	}

	/// <summary>A campaign-mode load, whose squad is the career's.</summary>
	internal static MissionSquad Campaign() => new(PlayerMachineSource.CareerSquad, -1, null);
}
