using HercWorks.Core.Io.Transform.Dbsim;
using Herculan.Engine.Cockpit;
using Herculan.Engine.Content;
using Herculan.Engine.Sim;
using Herculan.Engine.World;

namespace Herculan.Engine.Editor;

/// <summary>
/// The herc, flyer, ground vehicle and structure types the editor offers, each under the name the MFD prints, as the Toolbox
/// lists them and an object's type choice offers them (<see cref="RosterFitView"/>): the mech types of <c>nam\MECHS.NAM</c>;
/// the types of <c>nam\FLYERS.NAM</c> the install has a <c>dat\&lt;type&gt;.DAT</c> for
/// (<see cref="MfdStatusSubject.FlyerTypeName"/>), the others named in a note, since with no data they cannot fly or be shot
/// (docs/retail/simulation/ai-flyers.md); the types of <c>dat\BASES.DAT</c> <see cref="BaseObject.IsGroundVehicle"/> answers
/// true for (<see cref="MfdStatusSubject.BaseName"/>), the other vehicle types (<see cref="BaseType.IsVehicle"/>), for which
/// DBSIM builds no object (docs/retail/simulation/structure-behaviour.md#five-classes-one-switch), named in a note; and the
/// other types of <c>dat\BASES.DAT</c> as structures, under the names the MFD status screen prints
/// (<see cref="BaseType.SilhouetteIndex"/>), an invulnerable one (<see cref="BaseType.Invulnerable"/>) marked
/// <see cref="Invulnerable"/>, since a structure of it is never destroyed (docs/retail/formats/bases-dat.md).
/// </summary>
internal sealed class UnitTypeCatalog {
	/// <summary>What an invulnerable structure type's entry carries after its name.</summary>
	public const string Invulnerable = ", can't be destroyed";

	public UnitTypeCatalog(GameContent content) {
		var entries = new List<(ToolboxKind, int, string)>();
		var mechs = UnitTypeNames.LoadMechs(content);
		for (int i = 0; i < mechs.Count; i++) {
			entries.Add((ToolboxKind.Herc, i, mechs[i] is { Length: > 0 } name ? $"{name.ToUpperInvariant()} ({i})" : $"Type {i}"));
		}

		var flyers = UnitTypeNames.LoadFlyers(content);
		var leftOut = new List<string>();
		for (int i = 0; i < flyers.Count; i++) {
			if (flyers[i] is not { Length: > 0 } flyer) {
				continue;
			}

			if (content.Read("dat", flyer + ".DAT") is not { } data) {
				leftOut.Add(flyer.ToUpperInvariant());
				continue;
			}

			entries.Add((ToolboxKind.Flyer, i, $"{MfdStatusSubject.FlyerTypeName(new FlyerSimDataTransformer().Parse(data), flyer)} ({i})"));
		}

		FlyersLeftOut = leftOut.Count > 0
			? $"Not offered: {string.Join(", ", leftOut)}. The install has no data for them to fly or be shot with."
			: null;

		var types = BaseTypeTable.Load(content);
		var strings = SimStrings.Load(content);
		var vehiclesLeftOut = new List<string>();
		for (int i = 0; i < types.Count; i++) {
			if (types[i] is { IsVehicle: true } vehicle) {
				string? name = MfdStatusSubject.BaseName(vehicle, strings);
				string label = string.IsNullOrWhiteSpace(name) ? $"Type {i}" : $"{name} ({i})";
				if (BaseObject.IsGroundVehicle(i)) {
					entries.Add((ToolboxKind.Vehicle, i, label));
				} else {
					vehiclesLeftOut.Add(label);
				}
			}
		}

		VehiclesLeftOut = vehiclesLeftOut.Count > 0
			? $"Not offered: {string.Join(", ", vehiclesLeftOut)}. DBSIM builds nothing for these types."
			: null;

		for (int i = 0; i < types.Count; i++) {
			if (types[i] is { IsVehicle: false } type) {
				string? name = strings?.Text(MfdLayout.StructureNameGroup, type.SilhouetteIndex);
				string label = string.IsNullOrWhiteSpace(name) ? $"Type {i}" : $"{name} ({i})";
				entries.Add((ToolboxKind.Structure, i, type.Invulnerable ? label + Invulnerable : label));
			}
		}

		Entries = entries;
	}

	/// <summary>A catalog of <paramref name="entries"/> alone, with nothing left out.</summary>
	internal UnitTypeCatalog(IReadOnlyList<(ToolboxKind Kind, int TypeIndex, string Name)> entries) => Entries = entries;

	/// <summary>Every type offered, by kind: the hercs, the flyers, the ground vehicles, then the structures, each in type order.</summary>
	public IReadOnlyList<(ToolboxKind Kind, int TypeIndex, string Name)> Entries { get; }

	/// <summary>The flyer types not offered, which the install has no data for; null when it has data for each.</summary>
	public string? FlyersLeftOut { get; }

	/// <summary>The vehicle types not offered, which DBSIM builds no object for; null when there are none.</summary>
	public string? VehiclesLeftOut { get; }

	/// <summary>The types of <paramref name="kind"/> offered, in type order.</summary>
	public IEnumerable<int> Of(ToolboxKind kind) => Entries.Where(entry => entry.Kind == kind).Select(entry => entry.TypeIndex);

	/// <summary>The name a type of <paramref name="kind"/> is offered under, or <c>Type n</c> for one not offered.</summary>
	public string NameOf(ToolboxKind kind, int typeIndex) =>
		Entries.FirstOrDefault(entry => entry.Kind == kind && entry.TypeIndex == typeIndex).Name ?? $"Type {typeIndex}";

	/// <summary>The types an object of <paramref name="kind"/> is offered as its own: those of its <see cref="KindOf"/>.</summary>
	public IEnumerable<int> ChoicesFor(MissionUnitKind kind, bool isVehicle) => Of(KindOf(kind, isVehicle));

	/// <summary>
	/// The kind an object's type is chosen within: a herc among the hercs, a flyer among the flyers, a ground vehicle among
	/// the ground vehicles and any other structure among the structures. Both are records of row 14, but a structure is
	/// offered no vehicle type and a ground vehicle no structure type, so a convoy stays a convoy and a building stays a
	/// building (the editor's choice).
	/// </summary>
	public static ToolboxKind KindOf(MissionUnitKind kind, bool isVehicle) => kind switch {
		MissionUnitKind.Mech => ToolboxKind.Herc,
		MissionUnitKind.Flyer => ToolboxKind.Flyer,
		_ => isVehicle ? ToolboxKind.Vehicle : ToolboxKind.Structure,
	};
}
