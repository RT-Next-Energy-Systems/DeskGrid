using System.IO;
using DeskGrid.Updates;

namespace DeskGrid.Tests;

public sealed class UpdateCheckerTests
{
    private static readonly string Hash = new('a', 64);

    private static readonly string Release = $$"""
        {
          "tag_name": "v0.2.0",
          "html_url": "https://github.com/Ryan970714/DeskGrid/releases/tag/v0.2.0",
          "draft": false,
          "prerelease": false,
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.test/notes.txt" },
            { "name": "DeskGrid-0.2.0-win-x64.exe", "browser_download_url": "https://example.test/a.exe", "digest": "sha256:{{Hash}}" }
          ]
        }
        """;

    [Fact]
    public void ParseRelease_FindsTheVersionTheExeAndItsDigest()
    {
        var release = UpdateChecker.ParseRelease(Release);

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 2, 0), release.Version);
        Assert.Equal("v0.2.0", release.Tag);
        Assert.Equal("https://example.test/a.exe", release.ExeUrl);
        Assert.Equal(Hash, release.Sha256);
    }

    [Fact]
    public void ParseRelease_WithoutAssets_HasNoDownload()
    {
        var release = UpdateChecker.ParseRelease("""{ "tag_name": "v1.0.0", "html_url": "u", "assets": [] }""");

        Assert.NotNull(release);
        Assert.Null(release.ExeUrl);
        Assert.Null(release.Sha256);
    }

    [Fact]
    public void ParseRelease_ExeWithoutDigest_HasNoChecksum()
    {
        var release = UpdateChecker.ParseRelease(
            """{ "tag_name": "v1.0.0", "html_url": "u", "assets": [ { "name": "DeskGrid-1.0.0-win-x64.exe", "browser_download_url": "x" } ] }""");

        Assert.NotNull(release);
        Assert.Equal("x", release.ExeUrl);
        Assert.Null(release.Sha256);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{ "tag_name": null }""")]
    [InlineData("""{ "tag_name": 5 }""")]
    public void ParseRelease_UnexpectedShape_IsNoRelease(string json) =>
        Assert.Null(UpdateChecker.ParseRelease(json));

    [Fact]
    public void ParseRelease_OddAssets_AreSkipped()
    {
        var release = UpdateChecker.ParseRelease(
            """{ "tag_name": "v1.0.0", "assets": [ 7, {}, { "name": null }, { "name": "DeskGrid-1.0.0-win-x64.exe" } ] }""");

        Assert.NotNull(release);
        Assert.Equal("", release.PageUrl);
        Assert.Null(release.ExeUrl);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v1.0.0", "html_url": "u", "draft": true }""")]
    [InlineData("""{ "tag_name": "v1.0.0", "html_url": "u", "prerelease": true }""")]
    [InlineData("""{ "tag_name": "nightly", "html_url": "u" }""")]
    public void ParseRelease_IgnoresDraftsPrereleasesAndOddTags(string json) =>
        Assert.Null(UpdateChecker.ParseRelease(json));

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("1.0.0", "0.9.9", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.0", "0.1.0.0", false)]
    [InlineData("0.1.0", "0.2.0", false)]
    public void IsNewer_ComparesMajorMinorBuild(string latest, string current, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Fact]
    public void ParseDigest_AcceptsOnlySha256Hashes()
    {
        Assert.Equal(Hash, UpdateChecker.ParseDigest($"sha256:{Hash.ToUpperInvariant()}"));
        Assert.Null(UpdateChecker.ParseDigest("sha1:" + new string('a', 40)));
        Assert.Null(UpdateChecker.ParseDigest("sha256:short"));
        Assert.Null(UpdateChecker.ParseDigest(null));
    }
}

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void WriteScript_IsUtf8WithByteOrderMark_AndQuotesPaths()
    {
        var script = _temp.Combine("apply-update.ps1");
        const string Target = @"C:\Users\王小明\O'Neil\DeskGrid.exe";

        UpdateInstaller.WriteScript(script, 1234, @"C:\Temp\DeskGrid-0.3.0.exe", Target);

        var bytes = File.ReadAllBytes(script);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("Wait-Process -Id 1234", text);
        Assert.Contains(@"-Destination 'C:\Users\王小明\O''Neil\DeskGrid.exe'", text);
        Assert.Contains(@"Start-Process -FilePath 'C:\Users\王小明\O''Neil\DeskGrid.exe'", text);
    }

    [Fact]
    public async Task HasHash_ComparesTheSha256_AndLetsTheFileBeDeletedAfterwards()
    {
        var file = _temp.CreateFile("DeskGrid-9.9.9.exe");
        File.WriteAllText(file, "abc");
        const string Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        Assert.True(await UpdateInstaller.HasHashAsync(file, Abc, TestContext.Current.CancellationToken));
        Assert.False(await UpdateInstaller.HasHashAsync(file, new string('0', 64), TestContext.Current.CancellationToken));

        File.Delete(file); // A mismatch deletes the download: nothing may still hold it open.
        Assert.False(File.Exists(file));
    }
}
