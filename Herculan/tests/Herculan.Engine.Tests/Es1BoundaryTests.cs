using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Herculan.Es1.Host;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// Holds the line docs/herculan/planning.md's "How the two games share code" draws: a game assembly never references
/// the other game. ES2's game code still shares the <c>Herculan.Engine</c> assembly with the core, so the project
/// reference cannot enforce it for <c>Herculan.Es1</c>, and this reads the assembly's type references instead.
/// </summary>
public class Es1BoundaryTests {
	/// <summary>The namespaces of <c>Herculan.Engine</c> that are ES2's game code rather than the core.</summary>
	private static readonly string[] Es2Namespaces = [
		"Herculan.Engine.Sim", "Herculan.Engine.Cockpit", "Herculan.Engine.Shell", "Herculan.Engine.World",
		"Herculan.Engine.Scene", "Herculan.Engine.Terrain",
	];

	[Fact]
	public void Es1ReferencesNoEs2GameCode() {
		using var stream = File.OpenRead(typeof(Es1ZoneViewer).Assembly.Location);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();

		var referenced = reader.TypeReferences
			.Select(handle => FullName(reader, handle))
			.ToList();
		var offending = referenced
			.Where(name => Es2Namespaces.Any(ns => name.StartsWith(ns + ".", StringComparison.Ordinal)))
			.Distinct()
			.Order()
			.ToList();

		// The positive control: the read finds the core types the assembly is known to use.
		Assert.Contains("Herculan.Engine.Render.SceneRenderer", referenced);
		Assert.Empty(offending);
	}

	/// <summary>A type reference's namespace-qualified name, through the types a nested one sits in.</summary>
	private static string FullName(MetadataReader reader, TypeReferenceHandle handle) {
		var type = reader.GetTypeReference(handle);
		string name = reader.GetString(type.Name);
		if (type.ResolutionScope.Kind == HandleKind.TypeReference) {
			return FullName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + name;
		}

		string ns = reader.GetString(type.Namespace);
		return ns.Length == 0 ? name : ns + "." + name;
	}
}
