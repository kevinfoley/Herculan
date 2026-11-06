using HercWorks.Core.Data.File.Msn;
using Herculan.Engine.Sim;

namespace Herculan.Engine.Editor;

/// <summary>
/// The "Squad target handling" choice, in the Mission Settings window and in the player's squad's properties: header word 3
/// (<see cref="HercWorks.Core.Data.File.Msn.Script.ScriptDat.SquadTargetHandling"/>), which picks the player's think's arm
/// (docs/retail/simulation/mission-objectives.md#the-player-thinks-objective-arms), as one step of <see cref="PointMover"/>'s undo
/// history. It is written through <see cref="MissionDocument.SetHeaderWord"/>; no launch writes over it.
///
/// <para>Types 3 and 7 are the two data links; why every other shows as "None" is
/// docs/herculan/editor-design.md#the-players-squad.</para>
///
/// <para>The edit runs when <see cref="RunRequest"/> is called, once the frame's ImGui is rendered.</para>
/// </summary>
internal sealed class SquadTargetHandlingEditor {
	/// <summary>The header word that holds the squad target handling, <c>script.dat</c> offset 6.</summary>
	public const int HandlingWord = 6 / 2;

	/// <summary>The type "None" writes, which runs no arm.</summary>
	public const short NoArm = 2;

	/// <summary>The choices, in the order the window lists them.</summary>
	public static IReadOnlyList<short> Choices { get; } = [
		MechObject.SquadTargetHandlingAnnounce, MechObject.SquadTargetHandlingDataLink, MechObject.SquadTargetHandlingDataLinkAlt, NoArm,
	];

	private readonly MissionDocument _source;
	private readonly PointMover _mover;
	private readonly SceneSync _sync;
	private short? _requested;

	public SquadTargetHandlingEditor(MissionDocument source, PointMover mover, SceneSync sync) {
		_source = source;
		_mover = mover;
		_sync = sync;
	}

	/// <summary>The squad target handling the launch plays, as one of <see cref="Choices"/>.</summary>
	public short Current => Choice(_source.LaunchHeader[HandlingWord]);

	/// <summary>
	/// Whether the launch's handling, or a row-2 record's under a condition, is a data link while no order of the player's
	/// squad names a target (<see cref="MissionCheck.SquadHasOrderTarget"/>), the mission check's
	/// <see cref="MissionCheckKind.DataLinkNoTarget"/>.
	/// </summary>
	public bool DataLinkWithoutTarget =>
		(IsDataLink(Current) || Variations.Any(variation => IsDataLink(variation.Choice)))
		&& !MissionCheck.SquadHasOrderTarget(_source.Layout);

	private static bool IsDataLink(short choice) =>
		choice is MechObject.SquadTargetHandlingDataLink or MechObject.SquadTargetHandlingDataLinkAlt;

	/// <summary>Why the last edit run could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>
	/// Every row-2 record under another condition than the base's that sets the type, as its condition, that condition
	/// described (<see cref="ConditionEditing.Describe(MissionFile, short)"/>), and the choice it switches to
	/// (<see cref="LayoutHeader.Variations"/>), in row order.
	/// </summary>
	public IReadOnlyList<(short Condition, string? Described, short Choice)> Variations {
		get {
			var mission = _source.Mission;
			var patches = mission.SettingsPatches ?? [];
			return (_source.Layout.Header?.Variations ?? [])
				.Where(variation => variation.Words.Contains(HandlingWord))
				.Select(variation => (variation.Condition, ConditionEditing.Describe(mission, variation.Condition),
					Choice(patches[variation.Record.Index].Data[MissionSettingsPatch.HeaderWordsStart + HandlingWord])))
				.ToList();
		}
	}

	/// <summary>Asks for the squad target handling to be <paramref name="choice"/>, one of <see cref="Choices"/>.</summary>
	public void Request(short choice) => _requested = choice;

	/// <summary>Makes the edit asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		if (_requested is not { } choice) {
			return;
		}

		_requested = null;
		Failure = null;
		if (choice == Current) {
			return;
		}

		MsnEdit change;
		try {
			change = _sync.Run(() => _source.SetHeaderWord(HandlingWord, choice));
		} catch (InvalidOperationException ex) {
			Failure = ex.Message;
			return;
		}

		_mover.Record(new RecordsEdit(_sync, change));
	}

	/// <summary>The choice squad target handling <paramref name="type"/> plays as.</summary>
	public static short Choice(short type) =>
		type is MechObject.SquadTargetHandlingAnnounce or MechObject.SquadTargetHandlingDataLink or MechObject.SquadTargetHandlingDataLinkAlt
			? type
			: NoArm;

	/// <summary>A choice as the "Squad target handling" dropdown names it.</summary>
	public static string Label(short choice) => choice switch {
		MechObject.SquadTargetHandlingAnnounce => "Announced when selected",
		MechObject.SquadTargetHandlingDataLink => "Data link",
		MechObject.SquadTargetHandlingDataLinkAlt => "Data link, squad may shoot it",
		_ => "None",
	};

	/// <summary>What a choice does in play, for its tooltip in the dropdown.</summary>
	public static string Note(short choice) => choice switch {
		MechObject.SquadTargetHandlingAnnounce => "The first time the player selects the target of their squad's order, the "
			+ "computer announces that the mission target is detected. This is not an actual objective and has no effect on gameplay.",
		MechObject.SquadTargetHandlingDataLink => $"The player completes a data link by staying within {MissionText.Metres(MechObject.DataLinkRange)} "
			+ "of the target of their squad's order, aimed at it, for about five seconds; turning away or moving off starts it "
			+ "over. Objectives can wait on it. Squadmates do not intentionally shoot the target.",
		MechObject.SquadTargetHandlingDataLinkAlt => "As \"Data link\", but squadmates may shoot the target.",
		_ => "The target of the player's squad's order is not announced, and no data link can be completed.",
	};
}
