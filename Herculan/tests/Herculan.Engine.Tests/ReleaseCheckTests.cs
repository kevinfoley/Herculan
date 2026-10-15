using Herculan.Engine.Updates;
using Xunit;

namespace Herculan.Engine.Tests;

/// <summary>
/// How a GitHub release response is read against the running build's version. No network: these feed
/// <see cref="ReleaseCheck.Interpret"/> the JSON directly.
/// </summary>
public class ReleaseCheckTests {
	private static readonly Version Current = new(0, 9, 7);

	[Theory]
	[InlineData("v0.9.7", 0, 9, 7)]
	[InlineData("0.9.7+abc123", 0, 9, 7)]
	[InlineData("0.9.8-beta", 0, 9, 8)]
	public void ParseVersionDropsPrefixAndSuffix(string text, int major, int minor, int build) =>
		Assert.Equal(new Version(major, minor, build, 0), ReleaseCheck.ParseVersion(text));

	[Fact]
	public void NewerTagIsAnnouncedWithAPageBuiltFromTheTag() {
		var result = ReleaseCheck.Interpret("""{"tag_name":"v0.9.8","html_url":"https://example.com/"}""", Current);

		Assert.Equal(ReleaseCheckStatus.NewerAvailable, result.Status);
		Assert.Equal("0.9.8", ReleaseCheck.Display(result.Latest!));
		Assert.Equal(ReleaseCheck.ReleasesPage + "tag/v0.9.8", result.Page);
	}

	[Theory]
	[InlineData("v0.9.7")]
	[InlineData("v0.9.7.0")]
	[InlineData("v0.9.6")]
	public void SameOrOlderTagIsUpToDate(string tag) =>
		Assert.Equal(ReleaseCheckStatus.UpToDate, ReleaseCheck.Interpret($$"""{"tag_name":"{{tag}}"}""", Current).Status);

	[Fact]
	public void PrereleaseIsNotAnnounced() =>
		Assert.Equal(ReleaseCheckStatus.UpToDate,
			ReleaseCheck.Interpret("""{"tag_name":"v1.0.0","prerelease":true}""", Current).Status);

	[Theory]
	[InlineData("""{"tag_name":"v1.0.0/../../evil"}""")]
	[InlineData("""{"message":"Not Found"}""")]
	[InlineData("not json")]
	public void UnusableResponseFails(string json) =>
		Assert.Equal(ReleaseCheckStatus.Failed, ReleaseCheck.Interpret(json, Current).Status);
}
