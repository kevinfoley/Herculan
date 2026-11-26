using System.Globalization;
using System.Text.RegularExpressions;

namespace Herculan.Engine.Updates;

/// <summary>
/// A HERCULAN release number as Semantic Versioning writes it: a number such as <c>2.0.0</c>, and for a pre-release a
/// label after a <c>-</c>, such as <c>2.0.0-alpha.1</c>. Ordered by SemVer precedence: a labelled version comes before
/// the same number without one, and labels compare one dot-separated part at a time, numbers as numbers and below words,
/// so <c>2.0.0-alpha.2</c> &lt; <c>2.0.0-alpha.10</c> &lt; <c>2.0.0-beta.1</c> &lt; <c>2.0.0</c>.
/// </summary>
public sealed partial record ReleaseVersion : IComparable<ReleaseVersion> {
	public ReleaseVersion(Version number, string? label = null) {
		// Version ranks a missing part (-1) below 0, which would make 0.9.7 older than 0.9.7.0.
		Number = new Version(number.Major, number.Minor, Math.Max(number.Build, 0), Math.Max(number.Revision, 0));
		Label = string.IsNullOrEmpty(label) ? null : label;
	}

	/// <summary>The number, with every part present: <c>0.9.7</c> is held as 0.9.7.0.</summary>
	public Version Number { get; }

	/// <summary>The pre-release label without its <c>-</c> (<c>alpha.1</c>), or null for a full release.</summary>
	public string? Label { get; }

	/// <summary>Whether this is a pre-release: it has a <see cref="Label"/>.</summary>
	public bool IsPrerelease => Label != null;

	/// <summary>
	/// The version in a text. A leading <c>v</c> and anything from a <c>+</c> on (the commit the SDK
	/// appends) are dropped, and missing parts count as 0, so <c>v0.9.7</c>, <c>0.9.7+abc123</c> and <c>0.9.7.0</c> are
	/// all equal. Null when the number is not a version or the label holds anything but letters, digits, <c>-</c> and
	/// dots between non-empty parts.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	public static ReleaseVersion? Parse(string text) {
		string rest = text.StartsWith('v') || text.StartsWith('V') ? text[1..] : text;
		int plus = rest.IndexOf('+');
		if (plus >= 0) {
			rest = rest[..plus];
		}

		string? label = null;
		int dash = rest.IndexOf('-');
		if (dash >= 0) {
			label = rest[(dash + 1)..];
			rest = rest[..dash];
			if (!LabelPattern().IsMatch(label)) {
				return null;
			}
		}

		return Version.TryParse(rest, out var number) ? new ReleaseVersion(number, label) : null;
	}

	public int CompareTo(ReleaseVersion? other) {
		if (other is null) {
			return 1;
		}

		int byNumber = Number.CompareTo(other.Number);
		return byNumber != 0 ? byNumber : CompareLabels(Label, other.Label);
	}

	public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;

	public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;

	/// <summary>As a release is numbered: three parts, or four when the fourth is not 0, then <c>-</c> and the label.</summary>
	public override string ToString() {
		string number = Number.Revision > 0 ? Number.ToString() : Number.ToString(3);
		return Label == null ? number : number + "-" + Label;
	}

	private static int CompareLabels(string? a, string? b) {
		if (a == b) {
			return 0;
		}

		if (a == null) {
			return 1;
		}

		if (b == null) {
			return -1;
		}

		string[] x = a.Split('.');
		string[] y = b.Split('.');
		for (int i = 0; i < Math.Min(x.Length, y.Length); i++) {
			bool xNumeric = ulong.TryParse(x[i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong xValue);
			bool yNumeric = ulong.TryParse(y[i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong yValue);
			int byPart = xNumeric && yNumeric ? xValue.CompareTo(yValue)
				: xNumeric ? -1
				: yNumeric ? 1
				: string.CompareOrdinal(x[i], y[i]);
			if (byPart != 0) {
				return byPart;
			}
		}

		return x.Length.CompareTo(y.Length);
	}

	[GeneratedRegex(@"^[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$")]
	private static partial Regex LabelPattern();
}
