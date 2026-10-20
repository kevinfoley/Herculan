using HercWorks.Core.Data.File.Msn;
using HercWorks.Core.Io;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Shell;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The preview: the one version of a mission the editor shows, made by the campaign flags its row-1 conditions test, as the
/// load hands them in, and the load's random draws (docs/herculan/handoff-editor-conditions.md#the-preview). A mission opens
/// with every draw its lowest outcome (<see cref="MissionOpener"/>). A flag typed in, Simulate a mission load, which opens
/// the mission again as its open loaded it drawing as the game does, and Back to first options, which opens it again with
/// the lowest draws (<see cref="MissionOpener.OpenAgain"/>), each replace the document's generation
/// (<see cref="MissionDocument.SetGeneration"/>), write the handoff's <c>mission.var</c> as the launch writes it for that
/// generation, and build the editor's state again (<see cref="MissionRebuild"/>), the squad's <c>player.mec</c> and the
/// generated <c>mission.str</c> with it. None changes the document: none is a step of the undo history, marks it unsaved or
/// changes what Save writes.
///
/// <para>Both reopens keep every flag as it stands but those the launch draws, 4 to 6, which take the new load's draws. The
/// requests run when <see cref="RunRequest"/> is called, once the frame's panels are drawn, as the other edits do.</para>
///
/// <para>The alternative a variant table's view shows in place of its draw (<see cref="RequestShown"/>) is part of the
/// preview too: the generation's forced picks (<see cref="MissionGeneration.WithPick"/>), which still draw, so nothing else
/// the generation draws changes. It is kept through a flag change and an edit, ignored once its table or range is gone, and
/// cleared by both reopens, whose generation forces nothing.</para>
/// </summary>
internal sealed class MissionPreview {
	// The flags every load draws (ShellTrainingLaunch's SeedDrawnFlags), below this bound.
	private const int FirstDrawnFlag = 4;
	private const int LastDrawnFlag = 6;
	private const int DrawnFlagBound = 12;

	// The folder inside the handoff's that a reopen opens the mission into, so a failed one leaves the handoff as it was.
	private const string ReopenFolder = "draw";

	private readonly MissionDocument? _source;
	private readonly MissionRebuild _rebuild;
	private readonly PointMover _mover;
	private readonly TheaterChanger _theater;
	private readonly MissionLaunchMode? _mode;
	private readonly string _handoff;

	private (int Flag, short Value)? _flagRequested;

	// A reopen asked for: true to simulate a load, false for the first options.
	private bool? _reopenRequested;
	private (short Table, short? Range)? _shownRequested;

	/// <param name="source">The mission as its <c>.MSN</c>, or null for a plain <c>script.dat</c>, which has no preview to set.</param>
	/// <param name="mode">How the mission was opened, which a reopen opens it by again; null when it cannot be.</param>
	/// <param name="handoffDirectory">Where the open wrote the handoff.</param>
	public MissionPreview(MissionDocument? source, MissionRebuild rebuild, PointMover mover, TheaterChanger theater,
			MissionLaunchMode? mode, string handoffDirectory) {
		_source = source;
		_rebuild = rebuild;
		_mover = mover;
		_theater = theater;
		_mode = mode;
		_handoff = handoffDirectory;
	}

	/// <summary>Whether the mission is held as its <c>.MSN</c>, which has a preview to set.</summary>
	public bool Shown => _source != null;

	/// <summary>Whether the mission was loaded in training mode, a practice mission or <c>INSTANT ACTION</c>, rather than at a campaign position.</summary>
	public bool Training => _rebuild.Squad?.PlayerMachine is not PlayerMachineSource.CareerSquad;

	/// <summary>Whether the draws are a simulated load's, rather than the lowest outcomes an open draws.</summary>
	public bool Simulated { get; private set; }

	/// <summary>
	/// Every campaign flag a type-0 row-1 record of the mission tests, whatever the record's own condition, in index order;
	/// an index past the flag array, which reads 0 whatever is set, and the flags every load draws (<see cref="TestsDrawnFlag"/>)
	/// left out.
	/// </summary>
	public IReadOnlyList<int> TestedFlags =>
		FlagTests.Select(condition => (int)condition.FlagIndexOrRangeLower)
			.Where(flag => flag is < FirstDrawnFlag or > LastDrawnFlag)
			.Distinct()
			.Order()
			.ToList();

	/// <summary>Whether the mission tests a flag every load draws, 4 to 6, which only a reopen sets.</summary>
	public bool TestsDrawnFlag => FlagTests.Any(condition => condition.FlagIndexOrRangeLower is >= FirstDrawnFlag and <= LastDrawnFlag);

	/// <summary>The type-0 row-1 records testing flag <paramref name="flag"/>, in file order.</summary>
	public IReadOnlyList<MissionCondition14> TestsOf(int flag) =>
		FlagTests.Where(condition => condition.FlagIndexOrRangeLower == flag).ToList();

	private IEnumerable<MissionCondition14> FlagTests =>
		(_source?.Mission.Conditions ?? [])
			.Where(condition => condition.Type == 0 && condition.FlagIndexOrRangeLower is >= 0 and < MissionGenerator.CampaignFlagCount);

	/// <summary>The value flag <paramref name="flag"/> has as the generation hands the load the flags.</summary>
	public short Flag(int flag) => _source!.Generation.Flags[flag];

	/// <summary>What flag <paramref name="flag"/> holds, where the retail docs say; null otherwise.</summary>
	public static string? Label(int flag) => flag switch {
		// docs/retail/shell/campaign-loop.md#the-campaign-flag-array-is-the-msn-condition-store
		0 => "Last mission's outcome",
		1 => "Campaign stage",
		2 => "Mission in the stage",
		>= FirstDrawnFlag and <= LastDrawnFlag => $"Random number picked at each load (0 to {DrawnFlagBound - 1})",
		8 => "Hercs scrapped",
		9 => "Pilots lost",
		// docs/retail/formats/msn-mission-file.md#the-debriefs-loss-line
		10 => "Squadmates the player put out of the fight",
		_ => null,
	};

	/// <summary>Why the mission cannot be opened again, or null when it can.</summary>
	public string? WhyNoReopen => _mover.WhyReadOnly ?? (_mode == null ? "This mission's open cannot be made again." : null);

	/// <summary>Why the last change asked for could not be made, or null.</summary>
	public string? Failure { get; private set; }

	/// <summary>Asks for flag <paramref name="flag"/> to be handed the load as <paramref name="value"/>.</summary>
	public void RequestFlag(int flag, short value) => _flagRequested = (flag, value);

	/// <summary>Asks for the mission to be opened again drawing as the game's load does.</summary>
	public void RequestSimulate() => _reopenRequested = true;

	/// <summary>Asks for the mission to be opened again with the lowest draws, as it opened.</summary>
	public void RequestFirstOptions() => _reopenRequested = false;

	/// <summary>The range GUID whose alternative variant table <paramref name="table"/> shows in place of its draw, or null when it shows what it draws.</summary>
	public short? ShownAlternative(short table) => _source?.Generation.Picks.TryGetValue(table, out short range) == true ? range : null;

	/// <summary>How many variant tables show a chosen alternative in place of their draw.</summary>
	public int ChosenTables => _source is { } source ? source.Generation.Picks.Keys.Count(table => VariantEditing.IsTable(source, table)) : 0;

	/// <summary>Asks for variant table <paramref name="table"/> to show range <paramref name="range"/>'s alternative, or what it draws for null.</summary>
	public void RequestShown(short table, short? range) => _shownRequested = (table, range);

	/// <summary>Makes the change asked for since the last call, if any; <see cref="Failure"/> says why one could not be made.</summary>
	public void RunRequest() {
		var flag = _flagRequested;
		bool? reopen = _reopenRequested;
		var shown = _shownRequested;
		_flagRequested = null;
		_reopenRequested = null;
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

		if (reopen is { } simulate) {
			Failure = Reopen(_source, simulate);
			if (Failure == null) {
				Simulated = simulate;
			}
		}
	}

	// Opens the mission again from its .MSN and .ENG as they stand, drawing as the game's load does when <simulate> and the
	// lowest outcomes otherwise, and takes that open's generation with the flags as they stand but 4 to 6, and its squad.
	private string? Reopen(MissionDocument source, bool simulate) {
		if (WhyNoReopen is { } reason) {
			return reason;
		}

		if (new MissionFileTransformer().Write(source.Mission) is not { } msn) {
			return "The mission file does not write.";
		}

		byte[]? text = source.Text == null ? null : new MissionStringFileTransformer().Write(source.Text);
		string directory = Path.Combine(_handoff, ReopenFolder);
		MissionDocument? opened;
		MissionSquad? squad;
		try {
			Clear(directory);
			if (MissionOpener.OpenAgain(_mode!, msn, text, directory, simulate, out opened, out squad, out string? failure) == null) {
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
			flags[index] = opened!.Generation.Flags[index];
		}

		return Apply(opened!.Generation.WithFlags(flags), squad);
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
					// Nothing is rebuilt after a throw, so the preview goes back to the one it was built from.
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
