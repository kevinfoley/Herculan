using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Herculan.Engine.Updates;

/// <summary>What <see cref="ReleaseCheck.RunAsync"/> found.</summary>
public enum ReleaseCheckStatus {
	/// <summary>The newest published release is this build's version or older.</summary>
	UpToDate,
	/// <summary>A newer release is published; <see cref="ReleaseCheckResult.Latest"/> and <see cref="ReleaseCheckResult.Page"/> say which.</summary>
	NewerAvailable,
	/// <summary>The check could not be made; <see cref="ReleaseCheckResult.Error"/> says why.</summary>
	Failed,
}

/// <param name="Latest">The newest release's version, when one was read.</param>
/// <param name="Page">That release's page on GitHub, built here from the version rather than read from the response.</param>
/// <param name="Error">Why the check failed, for <see cref="ReleaseCheckStatus.Failed"/>.</param>
public sealed record ReleaseCheckResult(ReleaseCheckStatus Status, Version? Latest = null, string? Page = null, string? Error = null);

/// <summary>
/// Asks GitHub for HERCULAN's newest release and compares it with the running build. It only reads: nothing is
/// downloaded or run, and the one thing a caller can do with the answer is open <see cref="ReleaseCheckResult.Page"/>
/// in a browser. This is HERCULAN's own; retail has no update check.
///
/// <para>The request is one unauthenticated GET of <see cref="LatestReleaseUrl"/>, which answers with the newest
/// published release and never a draft or a pre-release. It sends GitHub the caller's address and a User-Agent naming
/// HERCULAN and its version, and nothing else. The response is treated as untrusted: it is capped at
/// <see cref="MaxResponseBytes"/>, only <c>tag_name</c>, <c>draft</c> and <c>prerelease</c> are read, a tag that is not
/// a plain version number is refused, and the page opened is built from the parsed version under
/// <see cref="ReleasesPage"/>, so no URL from the response is ever opened.</para>
/// </summary>
public static partial class ReleaseCheck {
	/// <summary>GitHub's newest-published-release endpoint for HERCULAN's repository.</summary>
	public const string LatestReleaseUrl = "https://api.github.com/repos/kevinfoley/Herculan/releases/latest";

	/// <summary>The repository's releases page; each release's own page is <c>tag/v&lt;version&gt;</c> under it.</summary>
	public const string ReleasesPage = "https://github.com/kevinfoley/Herculan/releases/";

	/// <summary>How long the request may take, connection included, before the check counts as failed.</summary>
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	/// <summary>The most of a response read; a release's JSON is a few kilobytes.</summary>
	public const int MaxResponseBytes = 1024 * 1024;

	private static HttpClient? _client;

	/// <summary>
	/// Makes the check for a build of version <paramref name="current"/>. Never throws: a network, HTTP or parse failure
	/// comes back as <see cref="ReleaseCheckStatus.Failed"/>. The continuation runs on the thread pool, so a frame loop
	/// polls the task rather than awaiting it.
	/// </summary>
	public static async Task<ReleaseCheckResult> RunAsync(Version current, CancellationToken cancellation = default) {
		try {
			using var response = await Client(current).GetAsync(LatestReleaseUrl, cancellation).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) {
				return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}");
			}

			string json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
			return Interpret(json, current);
		} catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) {
			return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: ex is TaskCanceledException ? "the request timed out" : ex.Message);
		}
	}

	/// <summary>Compares the release <paramref name="json"/> describes with <paramref name="current"/>.</summary>
	public static ReleaseCheckResult Interpret(string json, Version current) {
		string? tag;
		try {
			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
					|| !root.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String) {
				return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: "the response names no release");
			}

			if (IsTrue(root, "draft") || IsTrue(root, "prerelease")) {
				return new ReleaseCheckResult(ReleaseCheckStatus.UpToDate);
			}

			tag = tagElement.GetString();
		} catch (JsonException ex) {
			return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: ex.Message);
		}

		if (tag == null || !TagPattern().IsMatch(tag) || ParseVersion(tag) is not { } latest) {
			return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: "the release's tag is not a version number");
		}

		return latest > Normalize(current)
			? new ReleaseCheckResult(ReleaseCheckStatus.NewerAvailable, latest, ReleasesPage + "tag/" + tag)
			: new ReleaseCheckResult(ReleaseCheckStatus.UpToDate, latest);
	}

	/// <summary>
	/// The version in <paramref name="text"/>: a leading <c>v</c> and anything from a <c>-</c> or <c>+</c> on
	/// (a pre-release label, or the commit the SDK appends) are dropped, and missing parts count as 0, so
	/// <c>v0.9.7</c>, <c>0.9.7+abc123</c> and <c>0.9.7.0</c> are all equal. Null when the rest is not a version.
	/// </summary>
	public static Version? ParseVersion(string text) {
		string number = text.StartsWith('v') || text.StartsWith('V') ? text[1..] : text;
		int suffix = number.IndexOfAny(['-', '+']);
		if (suffix >= 0) {
			number = number[..suffix];
		}

		return Version.TryParse(number, out var version) ? Normalize(version) : null;
	}

	/// <summary><paramref name="version"/> as a release is numbered: three parts, or four when the fourth is not 0.</summary>
	public static string Display(Version version) => version.Revision > 0 ? version.ToString() : Normalize(version).ToString(3);

	// Version ranks a missing part (-1) below 0, which would make 0.9.7 older than 0.9.7.0.
	private static Version Normalize(Version version) =>
		new(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));

	private static bool IsTrue(JsonElement root, string name) =>
		root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

	private static HttpClient Client(Version current) {
		if (_client == null) {
			var client = new HttpClient { Timeout = Timeout, MaxResponseContentBufferSize = MaxResponseBytes };
			// GitHub refuses a request without a User-Agent.
			client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HERCULAN", Normalize(current).ToString(3)));
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
			if (Interlocked.CompareExchange(ref _client, client, null) != null) {
				client.Dispose();
			}
		}

		return _client;
	}

	// A tag the page URL can be built from as it stands: v and two to four dot-separated numbers.
	[GeneratedRegex(@"^v?\d{1,9}(\.\d{1,9}){1,3}$")]
	private static partial Regex TagPattern();
}
