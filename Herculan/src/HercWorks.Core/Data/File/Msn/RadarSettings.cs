namespace HercWorks.Core.Data.File.Msn;

/// <summary>
/// The standing radar setting of a herc (<see cref="MechRosterEntry144.AiRadarActive"/>) as its GUID's base states it, the values
/// records under other conditions set over it, and the edit of the base's value, by <see cref="BaseFieldValues"/>' rules: off
/// is the field's unset value. What the setting does is docs/retail/simulation/sensors.md#how-an-ai-machines-radar-is-set.
/// </summary>
public static class RadarSettings {
	private const int HercRow = 12;

	/// <summary>Whether herc GUID <paramref name="guid"/>'s base states its radar on; throws for a GUID no record carries.</summary>
	public static bool Current(MissionDocument document, short guid) => BaseFieldValues.Current(document, MsnField.MechRadar, guid) != 0;

	/// <inheritdoc cref="BaseFieldValues.Overrides"/>
	public static IReadOnlyList<FieldOverride> Overrides(MissionDocument document, short guid) =>
		BaseFieldValues.Overrides(document, MsnField.MechRadar, guid);

	/// <summary>
	/// Why herc GUID <paramref name="guid"/>'s radar setting cannot be set, or null when it can: as for its starting condition
	/// (<see cref="StartingConditions.WhyFixed"/>), a machine of the player's squad is built from <c>player.mec</c>, not its record.
	/// </summary>
	public static string? WhyFixed(MissionDocument document, short guid) => ActionWiring.WhyNoObjectAction(document, HercRow, guid);

	/// <summary>
	/// Sets herc GUID <paramref name="guid"/>'s radar setting in its base (<see cref="BaseFieldValues.Set"/>). Throws
	/// <see cref="InvalidOperationException"/> where <see cref="WhyFixed"/> answers, and for off where a template the base picks
	/// turns it on.
	/// </summary>
	public static MsnEdit Set(MissionDocument document, short guid, bool on) {
		if (WhyFixed(document, guid) is { } reason) {
			throw new InvalidOperationException(reason);
		}

		return BaseFieldValues.Set(document, MsnField.MechRadar, guid, (short)(on ? 1 : 0),
			"The random table it picks from turns its radar on, so it can't be turned off here.");
	}
}
