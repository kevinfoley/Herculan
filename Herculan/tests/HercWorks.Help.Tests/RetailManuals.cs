using Xunit;

namespace HercWorks.Help.Tests;

/// <summary>
/// The three retail help files and their readmes, for the tests that check against them. Each such test passes
/// vacuously when its file is absent, so the suite does not depend on an install.
/// </summary>
internal static class RetailManuals {
	public static readonly string[] Languages = ["ENGLISH", "FRENCH", "GERMAN"];

	/// <summary>Walks up from this assembly looking for <c>ES2/&lt;language&gt;/ES2GUIDE.HLP</c>.</summary>
	public static byte[]? Read(string language) => Read(language, "ES2GUIDE.HLP");

	/// <summary>The language folder's <c>README.WRI</c>, found the same way.</summary>
	public static byte[]? ReadReadme(string language) => Read(language, "README.WRI");

	private static byte[]? Read(string language, string name) {
		DirectoryInfo? at = new(AppContext.BaseDirectory);
		while (at is not null) {
			string candidate = Path.Combine(at.FullName, "ES2", language, name);
			if (File.Exists(candidate)) {
				return File.ReadAllBytes(candidate);
			}

			at = at.Parent;
		}

		return null;
	}

	public static TheoryData<string> All() {
		var data = new TheoryData<string>();
		foreach (string language in Languages) {
			data.Add(language);
		}

		return data;
	}
}
