namespace HercWorks.Core.Data.File;

/// <summary>
/// FILE - /SIMVOL0/NAM/*.NAM — <c>MECHS.NAM</c> and <c>FLYERS.NAM</c>: a flat run of NUL-terminated
/// ASCII names indexed by unit type, the numbering a mission's row-12 and row-13 type fields and
/// <c>script.dat</c>'s blocks 7 and 8 use. <c>MechType_InitOne</c> (<c>004201a8</c>) joins a mech's
/// name to the <c>dat\</c>, <c>dts\</c> and <c>bnd\</c> prefixes, so the name is its stats file, model
/// and collision data at once.
/// </summary>
public class NameList {
	/// <summary>The names, upper-cased, in type order.</summary>
	public string[] Names { get; set; } = [];

	/// <summary>The name of <paramref name="typeIndex"/>, or null when the list has no such type.</summary>
	public string? this[int typeIndex] =>
		typeIndex >= 0 && typeIndex < Names.Length ? Names[typeIndex] : null;
}
