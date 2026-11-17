using Herculan.Engine.Updates;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// How a GitHub release list is read against the running build's version. No network: these feed
/// <see cref="ReleaseCheck.Interpret"/> the JSON directly.
/// </summary>
public class ReleaseCheckTests {
	private static readonly ReleaseVersion Current = new(new Version(0, 9, 7));
	private static readonly ReleaseVersion Alpha = ReleaseVersion.Parse("2.0.0-alpha.2")!;

	private static string List(params string[] releases) => "[" + string.Join(",", releases) + "]";

	[Theory]
	[InlineData("v0.9.7", "0.9.7")]
	[InlineData("0.9.7+abc123", "0.9.7")]
	[InlineData("0.9.7.0", "0.9.7")]
	[InlineData("v2.0.0-alpha.1+abc123", "2.0.0-alpha.1")]
	public void ParseDropsPrefixAndCommit(string text, string expected) =>
		Assert.Equal(expected, ReleaseVersion.Parse(text)!.ToString());

	[Theory]
	[InlineData("2.0.0-alpha.2", "2.0.0-alpha.10")]
	[InlineData("2.0.0-alpha.10", "2.0.0-beta.1")]
	[InlineData("2.0.0-rc.1", "2.0.0")]
	[InlineData("1.1.2", "2.0.0-alpha.1")]
	public void LaterPrereleaseSortsAfter(string earlier, string later) =>
		Assert.True(ReleaseVersion.Parse(later)! > ReleaseVersion.Parse(earlier)!);

	[Fact]
	public void NewerTagIsAnnouncedWithAPageBuiltFromTheTag() {
		var result = ReleaseCheck.Interpret(List("""{"tag_name":"v0.9.8","html_url":"https://example.com/"}""", """{"tag_name":"v0.9.6"}"""), Current);

		Assert.Equal(ReleaseCheckStatus.NewerAvailable, result.Status);
		Assert.Equal("0.9.8", result.Latest!.ToString());
		Assert.Equal(ReleaseCheck.ReleasesPage + "tag/v0.9.8", result.Page);
	}

	[Theory]
	[InlineData("v0.9.7")]
	[InlineData("v0.9.7.0")]
	[InlineData("v0.9.6")]
	public void SameOrOlderTagIsUpToDate(string tag) =>
		Assert.Equal(ReleaseCheckStatus.UpToDate, ReleaseCheck.Interpret(List($$"""{"tag_name":"{{tag}}"}"""), Current).Status);

	/// <summary>A full release's build is never pointed at a pre-release, flagged or only labelled, nor at a draft.</summary>
	[Theory]
	[InlineData("""{"tag_name":"v1.0.0","prerelease":true}""")]
	[InlineData("""{"tag_name":"v1.0.0-beta.1"}""")]
	[InlineData("""{"tag_name":"v1.0.0","draft":true}""")]
	public void FullReleaseBuildSkipsPrereleases(string release) =>
		Assert.Equal(ReleaseCheckStatus.UpToDate, ReleaseCheck.Interpret(List(release, """{"tag_name":"v0.9.7"}"""), Current).Status);

	/// <summary>A pre-release's build hears of the next pre-release and of the full release, whichever is newest.</summary>
	[Theory]
	[InlineData("v2.0.0-beta.1", """{"tag_name":"v2.0.0-beta.1","prerelease":true}""", """{"tag_name":"v2.0.0-alpha.2","prerelease":true}""")]
	[InlineData("v2.0.0", """{"tag_name":"v2.0.0"}""", """{"tag_name":"v2.0.0-rc.1","prerelease":true}""")]
	public void PrereleaseBuildHearsOfLaterReleases(string expected, string first, string second) {
		var result = ReleaseCheck.Interpret(List(first, second), Alpha);

		Assert.Equal(ReleaseCheckStatus.NewerAvailable, result.Status);
		Assert.Equal(ReleaseCheck.ReleasesPage + "tag/" + expected, result.Page);
	}

	/// <summary>The first tag would give a page outside the releases page, so neither the label check nor the tag pattern lets it through.</summary>
	[Theory]
	[InlineData("""[{"tag_name":"v1.0.0-x/../../evil"}]""")]
	[InlineData("""[{"tag_name":100}]""")]
	[InlineData("""{"message":"Not Found"}""")]
	[InlineData("[]")]
	[InlineData("not json")]
	public void UnusableResponseFails(string json) =>
		Assert.Equal(ReleaseCheckStatus.Failed, ReleaseCheck.Interpret(json, Current).Status);
}
