using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Herculan.Engine.Updates;

/// <summary>What <see cref="ReleaseCheck.RunAsync"/> found.</summary>
public enum ReleaseCheckStatus {
	/// <summary>No release this build would take is newer than it.</summary>
	UpToDate,
	/// <summary>A newer release is published; <see cref="ReleaseCheckResult.Latest"/> and <see cref="ReleaseCheckResult.Page"/> say which.</summary>
	NewerAvailable,
	/// <summary>The check could not be made; <see cref="ReleaseCheckResult.Error"/> says why.</summary>
	Failed,
}

/// <param name="Latest">The newest release's version, when one was read.</param>
/// <param name="Page">That release's page on GitHub, built here from the version rather than read from the response.</param>
/// <param name="Error">Why the check failed, for <see cref="ReleaseCheckStatus.Failed"/>.</param>
public sealed record ReleaseCheckResult(ReleaseCheckStatus Status, ReleaseVersion? Latest = null, string? Page = null, string? Error = null);

/// <summary>
/// Asks GitHub for HERCULAN's newest releases and compares them with the running build. It only reads: nothing is
/// downloaded or run, and the one thing a caller can do with the answer is open <see cref="ReleaseCheckResult.Page"/>
/// in a browser. This is HERCULAN's own; retail has no update check.
///
/// <para>Which releases count depends on the build. A full release (<c>2.0.0</c>) takes only full releases, so its
/// players are never pointed at an alpha. A pre-release (<c>2.0.0-alpha.1</c>) takes pre-releases too, so its testers
/// hear of the next alpha or beta and of the full release that follows; once they move to a full release they stop
/// hearing of pre-releases. A release counts as a pre-release when GitHub flags it so or its tag has a label, so one
/// published without the flag still reaches only pre-release builds.</para>
///
/// <para>The request is one unauthenticated GET of <see cref="ReleasesUrl"/>, which lists the newest published releases
/// and never a draft. It sends GitHub the caller's address and a User-Agent naming HERCULAN and its version, and nothing
/// else. The response is treated as untrusted: it is capped at <see cref="MaxResponseBytes"/>, only each release's
/// <c>tag_name</c>, <c>draft</c> and <c>prerelease</c> are read, a release whose tag is not a version number is passed
/// over, and the page opened is built from a tag that passed <see cref="TagPattern"/>, under <see cref="ReleasesPage"/>,
/// so no URL from the response is ever opened.</para>
/// </summary>
public static partial class ReleaseCheck {
	/// <summary>
	/// GitHub's list of HERCULAN's releases, newest first. Ten reach back past any run of pre-releases to the last full
	/// release.
	/// </summary>
	public const string ReleasesUrl = "https://api.github.com/repos/kevinfoley/Herculan/releases?per_page=10";

	/// <summary>The repository's releases page; each release's own page is <c>tag/&lt;tag&gt;</c> under it.</summary>
	public const string ReleasesPage = "https://github.com/kevinfoley/Herculan/releases/";

	/// <summary>How long the request may take, connection included, before the check counts as failed.</summary>
	public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	/// <summary>The most of a response read; ten releases' JSON is some tens of kilobytes.</summary>
	public const int MaxResponseBytes = 1024 * 1024;

	private static HttpClient? _client;

	/// <summary>
	/// Makes the check for a build of the current version. Never throws: a network, HTTP or parse failure
	/// comes back as <see cref="ReleaseCheckStatus.Failed"/>. The continuation runs on the thread pool, so a frame loop
	/// polls the task rather than awaiting it.
	/// </summary>
	/// <param name="current">The running build's version.</param>
	/// <param name="cancellation">Cancels the check.</param>
	public static async Task<ReleaseCheckResult> RunAsync(ReleaseVersion current, CancellationToken cancellation = default) {
		try {
			using var response = await Client(current).GetAsync(ReleasesUrl, cancellation).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) {
				return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}");
			}

			string json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
			return Interpret(json, current);
		} catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) {
			return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: ex is TaskCanceledException ? "the request timed out" : ex.Message);
		}
	}

	/// <summary>Compares the releases a JSON document lists with the current version.</summary>
	/// <param name="json">The releases, as JSON.</param>
	/// <param name="current">The running build's version.</param>
	public static ReleaseCheckResult Interpret(string json, ReleaseVersion current) {
		ReleaseVersion? newest = null;
		string? newestTag = null;
		bool anyVersion = false;
		try {
			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) {
				return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: "the response lists no releases");
			}

			foreach (var release in root.EnumerateArray()) {
				if (release.ValueKind != JsonValueKind.Object || IsTrue(release, "draft")
						|| !release.TryGetProperty("tag_name", out var tagElement) || tagElement.ValueKind != JsonValueKind.String
						|| tagElement.GetString() is not { } tag || !TagPattern().IsMatch(tag) || ReleaseVersion.Parse(tag) is not { } version) {
					continue;
				}

				anyVersion = true;
				bool prerelease = version.IsPrerelease || IsTrue(release, "prerelease");
				if ((prerelease && !current.IsPrerelease) || (newest != null && !(version > newest))) {
					continue;
				}

				newest = version;
				newestTag = tag;
			}
		} catch (JsonException ex) {
			return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: ex.Message);
		}

		if (!anyVersion) {
			return new ReleaseCheckResult(ReleaseCheckStatus.Failed, Error: "no release's tag is a version number");
		}

		return newest != null && newest > current
			? new ReleaseCheckResult(ReleaseCheckStatus.NewerAvailable, newest, ReleasesPage + "tag/" + newestTag)
			: new ReleaseCheckResult(ReleaseCheckStatus.UpToDate, newest);
	}

	private static bool IsTrue(JsonElement release, string name) =>
		release.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

	private static HttpClient Client(ReleaseVersion current) {
		if (_client == null) {
			var client = new HttpClient { Timeout = Timeout, MaxResponseContentBufferSize = MaxResponseBytes };
			// GitHub refuses a request without a User-Agent.
			client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HERCULAN", current.ToString()));
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
			if (Interlocked.CompareExchange(ref _client, client, null) != null) {
				client.Dispose();
			}
		}

		return _client;
	}

	// A tag the page URL can be built from as it stands: v, two to four dot-separated numbers, and an optional label of
	// letters, digits, - and dots.
	[GeneratedRegex(@"^v?\d{1,9}(\.\d{1,9}){1,3}(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$")]
	private static partial Regex TagPattern();
}
