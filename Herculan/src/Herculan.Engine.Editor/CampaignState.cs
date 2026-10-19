using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Shell;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The campaign state the editor shows a mission in: the campaign flags its row-1 conditions test, as the load hands them
/// in, and the load's random draws (docs/herculan/handoff-editor-conditions.md#the-campaign-state). A flag
/// typed in, or Draw again, which opens the mission again as its open loaded it with new draws
/// (<see cref="MissionOpener.OpenAgain"/>), replaces the document's generation (<see cref="MissionDocument.SetGeneration"/>),
/// writes the handoff's <c>mission.var</c> as the launch writes it for that generation, and builds the editor's state again
/// (<see cref="MissionRebuild"/>), the squad's <c>player.mec</c> and the generated <c>mission.str</c> with it. Neither
/// changes the document: neither is a step of the undo history, marks it unsaved or changes what Save writes.
///
/// <para>Draw again keeps every flag as it stands but those the launch draws, 4 to 6, which take the new load's draws. The requests run when <see cref="RunRequest"/> is called, once the
/// frame's panels are drawn, as the other edits do.</para>
///
/// <para>The alternative a variant table's view shows in place of its draw (<see cref="RequestShown"/>) is part of the state
/// too: the generation's forced picks (<see cref="MissionGeneration.WithPick"/>), which still draw, so nothing else the
/// generation draws changes. It is kept through a flag change and an edit, ignored once its table or range is gone, and
/// cleared by Draw again, whose generation forces nothing.</para>
/// </summary>
internal sealed class CampaignState {
	// The flags every load draws (ShellTrainingLaunch's SeedDrawnFlags).
	private const int FirstDrawnFlag = 4;
	private const int LastDrawnFlag = 6;

	// The folder inside the handoff's that Draw again opens the mission into, so a failed draw leaves the handoff as it was.
	private const string DrawFolder = "draw";

	private readonly MissionDocument? _source;
	private readonly MissionRebuild _rebuild;
	private readonly PointMover _mover;
	private readonly TheaterChanger _theater;
	private readonly MissionLaunchMode? _mode;
	private readonly string _handoff;

	private (int Flag, short Value)? _flagRequested;
	private bool _drawRequested;
	private (short Table, short? Range)? _shownRequested;

	/// <param name="source">The mission as its <c>.MSN</c>, or null for a plain <c>script.dat</c>, which has no state to set.</param>
	/// <param name="mode">How the mission was opened, which Draw again opens it by again; null when it cannot be.</param>
	/// <param name="handoffDirectory">Where the open wrote the handoff.</param>
	public CampaignState(MissionDocument? source, MissionRebuild rebuild, PointMover mover, TheaterChanger theater,
			MissionLaunchMode? mode, string handoffDirectory) {
		_source = source;
		_rebuild = rebuild;
		_mover = mover;
		_theater = theater;
		_mode = mode;
		_handoff = handoffDirectory;
	}

	/// <summary>Whether the mission is held as its <c>.MSN</c>, which has a state to show.</summary>
	public bool Shown => _source != null;

	/// <summary>Whether the mission was loaded in training mode, a practice mission or <c>INSTANT ACTION</c>, rather than at a campaign position.</summary>
	public bool Training => _rebuild.Squad?.PlayerMachine is not PlayerMachineSource.CareerSquad;

	/// <summary>
	/// Every campaign flag a type-0 row-1 record of the mission tests, whatever the record's own condition, in index
	/// order; an index past the flag array, which reads 0 whatever is set, left out.
	/// </summary>
	public IReadOnlyList<int> TestedFlags =>
		(_source?.Mission.Conditions ?? [])
			.Where(condition => condition.Type == 0 && condition.FlagIndexOrRangeLower is >= 0 and < MissionGenerator.CampaignFlagCount)
			.Select(condition => (int)condition.FlagIndexOrRangeLower)
			.Distinct()
			.Order()
			.ToList();

	/// <summary>The value flag <paramref name="flag"/> has as the generation hands the load the flags.</summary>
	public short Flag(int flag) => _source!.Generation.Flags[flag];

	/// <summary>What flag <paramref name="flag"/> holds, where the retail docs say; null otherwise.</summary>
	public static string? Label(int flag) => flag switch {
		// docs/retail/shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store
		0 => "Last mission's outcome",
		1 => "Campaign stage",
		2 => "Mission in the stage",
		>= FirstDrawnFlag and <= LastDrawnFlag => "Drawn at each load (0 to 11)",
		8 => "Hercs scrapped",
		9 => "Pilots lost",
		// docs/retail/formats/msn-mission-file.md#the-debriefs-loss-line
		10 => "Squadmates the player put out of the fight",
		_ => null,
	};

	/// <summary>Why the draws cannot be made again, or null when they can.</summary>
	public string? WhyNoDraw => _mover.WhyReadOnly ?? (_mode == null ? "This mission's open cannot be made again." : null);

	/// <summary>Why the last change asked for could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Asks for flag <paramref name="flag"/> to be handed the load as <paramref name="value"/>.</summary>
	public void RequestFlag(int flag, short value) => _flagRequested = (flag, value);

	/// <summary>Asks for the mission to be opened again with new draws.</summary>
	public void RequestDrawAgain() => _drawRequested = true;

	/// <summary>The range GUID whose alternative variant table <paramref name="table"/> shows in place of its draw, or null when it shows what it draws.</summary>
	public short? ShownAlternative(short table) => _source?.Generation.Picks.TryGetValue(table, out short range) == true ? range : null;

	/// <summary>How many variant tables show a chosen alternative in place of their draw.</summary>
	public int ChosenTables => _source is { } source ? source.Generation.Picks.Keys.Count(table => VariantEditing.IsTable(source, table)) : 0;

	/// <summary>Asks for variant table <paramref name="table"/> to show range <paramref name="range"/>'s alternative, or what it draws for null.</summary>
	public void RequestShown(short table, short? range) => _shownRequested = (table, range);

	/// <summary>Makes the change asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		var flag = _flagRequested;
		bool draw = _drawRequested;
		var shown = _shownRequested;
		_flagRequested = null;
		_drawRequested = false;
		_shownRequested = null;
		if (_source == null || _mover.WhyReadOnly != null) {
			return;
		}

		if (shown is var (table, range) && ShownAlternative(table) != range) {
			Failure = Apply(_source.Generation.WithPick(table, range), squad: null);
		}

		if (flag is var (index, value) && Flag(index) != value) {
			var flags = _source.Generation.Flags.ToArray();
			flags[index] = value;
			Failure = Apply(_source.Generation.WithFlags(flags), squad: null);
		}

		if (draw) {
			Failure = DrawAgain(_source);
		}
	}

	// Opens the mission again from its .MSN and .ENG as they stand, with new draws, and takes that open's generation with
	// the flags as they stand but 4 to 6, and its squad.
	private string? DrawAgain(MissionDocument source) {
		if (WhyNoDraw is { } reason) {
			return reason;
		}

		if (new MissionFileTransformer().Write(source.Mission) is not { } msn) {
			return "The mission file does not write.";
		}

		byte[]? text = source.Text == null ? null : new MissionStringFileTransformer().Write(source.Text);
		string directory = Path.Combine(_handoff, DrawFolder);
		MissionDocument? drawn;
		MissionSquad? squad;
		try {
			Clear(directory);
			if (MissionOpener.OpenAgain(_mode!, msn, text, directory, out drawn, out squad, out string? failure) == null) {
				return failure;
			}
		} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
			return ex.Message;
		} finally {
			try {
				Clear(directory);
			} catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
				Console.Error.WriteLine($"Cannot remove {directory}: {ex.Message}");
			}
		}

		var flags = source.Generation.Flags.ToArray();
		for (int index = FirstDrawnFlag; index <= LastDrawnFlag; index++) {
			flags[index] = drawn!.Generation.Flags[index];
		}

		return Apply(drawn!.Generation.WithFlags(flags), squad);
	}

	// Generates the document with <generation>, writes mission.var as the launch writes it, takes <squad> when given and
	// builds the editor's state again, then the world when the generated header picks another.
	private string? Apply(MissionGeneration generation, MissionSquad? squad) {
		var source = _source!;
		int theater = _theater.Theater, variant = _theater.Variant;
		var generationBefore = source.Generation;
		var squadBefore = _rebuild.Squad;
		try {
			_rebuild.Run(() => {
				source.SetGeneration(generation);
				_rebuild.Squad = squad ?? squadBefore;
				try {
					WriteMissionVar(source.Generator.Flags);
				} catch {
					// Nothing is rebuilt after a throw, so the state the editor shows goes back to the one it was built from.
					source.SetGeneration(generationBefore);
					_rebuild.Squad = squadBefore;
					throw;
				}

				return generation;
			});
		} catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException
				or UnauthorizedAccessException) {
			return ex.Message;
		}

		_mover.Settle();
		_theater.Follow(theater, variant);
		return null;
	}

	// The flag array as the load left it, as the launch writes it beside the handoff (ShellTrainingLaunch.Load).
	private void WriteMissionVar(IReadOnlyList<short> flags) {
		var bytes = new byte[flags.Count * 2];
		Buffer.BlockCopy(flags.ToArray(), 0, bytes, 0, bytes.Length);
		File.WriteAllBytes(CaseInsensitivePath.Combine(_handoff, MissionLoader.CountersFileName), bytes);
	}

	private static void Clear(string directory) {
		if (Directory.Exists(directory)) {
			Directory.Delete(directory, recursive: true);
		}
	}
}
