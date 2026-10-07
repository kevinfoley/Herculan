using HercWorks.Core.Data.File;
using HercWorks.Core.Io.Transform.Common;
using Herculan.Engine.Content;

namespace Herculan.Engine.World;

/// <inheritdoc cref="NameList"/>
/// <remarks>
/// The engine's handle on <c>nam\MECHS.NAM</c> and <c>nam\FLYERS.NAM</c>, parsed by
/// <see cref="NameListTransformer"/>. Cross-checked against the retail install: <c>MECHS.NAM</c>
/// holds exactly 21 names and every one has a matching <c>dat\&lt;name&gt;.DAT</c> and
/// <c>dts\&lt;name&gt;.DTS</c>, which also matches the 0-20 range <c>msn-mission-file.md</c> measured
/// for row #12's type field.
/// </remarks>
public sealed class UnitTypeNames {
	/// <summary>VOL folder both lists live in.</summary>
	public const string ResourceFolder = "nam";

	/// <summary>Mech type names, indexed by <c>script.dat</c> block 7's type field.</summary>
	public const string MechListName = "MECHS.NAM";

	/// <summary>Flyer/vehicle type names, indexed by <c>script.dat</c> block 8's type field.</summary>
	public const string FlyerListName = "FLYERS.NAM";

	private readonly string[] _names;

	private UnitTypeNames(string[] names) {
		_names = names;
	}

	/// <summary>How many types the list declares.</summary>
	public int Count => _names.Length;

	/// <summary>
	/// The resource base name for a type, or null when the index falls outside the list. Out of range
	/// is a real possibility on hand-edited mission data and is not worth throwing over — the caller
	/// draws nothing and says so.
	/// </summary>
	public string? this[int typeIndex] =>
		typeIndex >= 0 && typeIndex < _names.Length ? _names[typeIndex] : null;

	/// <summary>Every name, in type order.</summary>
	public IReadOnlyList<string> Names => _names;

	public static UnitTypeNames LoadMechs(GameContent content) => Load(content, MechListName);

	public static UnitTypeNames LoadFlyers(GameContent content) => Load(content, FlyerListName);

	private static UnitTypeNames Load(GameContent content, string resourceName) =>
		new(new NameListTransformer().Parse(content.ReadRequired(ResourceFolder, resourceName))!.Names);
}
