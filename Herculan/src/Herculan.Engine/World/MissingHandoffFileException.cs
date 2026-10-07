namespace Herculan.Engine.World;

/// <summary>
/// A file of the mission handoff that <see cref="MissionLoader.Load"/> cannot do without is not there —
/// <c>mission.var</c> or <c>player.mec</c>, each of which the original opens under an assert (see
/// docs/retail/formats/script-dat.md#call-chain--confirmed). <see cref="FileNotFoundException.FileName"/> is the path looked for.
/// </summary>
public sealed class MissingHandoffFileException(string path)
	: FileNotFoundException($"{path} is missing.", path);
