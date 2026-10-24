using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace HercWorks.Query;

/// <summary>
/// Which bytes of a fixed-length file each member of its HercWorks.Core model is read from, and the name-or-offset
/// lookup over those members that <c>chassis --field</c> and <c>gau --block</c> share.
///
/// <para>Nothing here tables a layout: <see cref="Probe"/> parses a zero file with one byte set at a time and sees which
/// members change, so it follows the reader.</para>
/// </summary>
internal static class RecordLayout {
	/// <summary>
	/// The model's members a parse sets (public, writable instance properties), each with the offsets whose byte changes it,
	/// in declaration order; members no byte changes are left out. Values are compared by their JSON, so a member that is a
	/// class of its own compares by content.
	/// </summary>
	public static IReadOnlyList<(PropertyInfo Property, IReadOnlyList<int> Offsets)> Probe(Type model, Func<byte[], object?> parse, int length) {
		var properties = model.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).ToList();
		var zero = Snapshot(parse(new byte[length])!);
		var touched = properties.ToDictionary(p => p, _ => new List<int>());
		for (int k = 0; k < length; k++) {
			var probe = new byte[length];
			probe[k] = 1;
			var parsed = Snapshot(parse(probe)!);
			foreach (var property in properties) {
				if (zero[property] != parsed[property]) {
					touched[property].Add(k);
				}
			}
		}

		return properties.Where(p => touched[p].Count > 0).Select(p => (p, (IReadOnlyList<int>)touched[p])).ToList();

		Dictionary<PropertyInfo, string> Snapshot(object parsed) => properties.ToDictionary(p => p, p => JsonSerializer.Serialize(p.GetValue(parsed)));
	}

	/// <summary>Parses a decimal or <c>0x</c> hex number.</summary>
	public static bool TryParseNumber(string text, out int value) => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
		? int.TryParse(text[2..], NumberStyles.HexNumber, null, out value)
		: int.TryParse(text, out value);

	/// <summary>
	/// The one of <paramref name="items"/> whose name <paramref name="spec"/> is, matched whole and then as a prefix,
	/// ignoring case. Sets <paramref name="error"/> when it names none, or several.
	/// </summary>
	/// <param name="noMatch">The error when nothing matches.</param>
	public static T? MatchName<T>(IReadOnlyList<T> items, Func<T, string> name, string spec, string noMatch, out string? error) where T : class {
		error = null;
		var exact = items.Where(i => string.Equals(name(i), spec, StringComparison.OrdinalIgnoreCase)).ToList();
		var matches = exact.Count > 0 ? exact : items.Where(i => name(i).StartsWith(spec, StringComparison.OrdinalIgnoreCase)).ToList();
		if (matches.Count == 1) {
			return matches[0];
		}

		error = matches.Count == 0 ? noMatch : $"{spec} names {string.Join(", ", matches.Select(name))}; name one.";
		return null;
	}
}
