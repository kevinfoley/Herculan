namespace Herculan.Engine.Content;

/// <summary>
/// The language one program runs in, which picks the folder or extension of each translated resource it reads
/// (docs/retail/retail-builds.md, "How a language is chosen"). The shell's and the simulator's are set apart, from their
/// own command lines, so each mount names its own. The simulator's fourth, Spanish (<c>-E</c>), is left out: no build
/// ships its data, and no launcher passes it.
/// </summary>
public enum GameLanguage {
	English,
	French,
	German,
}
