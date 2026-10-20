namespace Herculan.Engine.Editor;

/// <summary>
/// What a campaign flag holds, where the retail docs say, for a flag test's view to label its flag field with
/// (docs/herculan/handoff-editor-conditions.md#conditions). The UI calls a flag a campaign variable.
/// </summary>
internal static class CampaignVariableLabels {
	// The flags every load draws (ShellTrainingLaunch's SeedDrawnFlags), below this bound.
	private const int FirstDrawnFlag = 4;
	private const int LastDrawnFlag = 6;
	private const int DrawnFlagBound = 12;

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
}
