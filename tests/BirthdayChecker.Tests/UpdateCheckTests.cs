using System.Text;
using System.Text.Json.Nodes;
using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public sealed class UpdateCheckTests
{
    private const string Hash = "c81b562c2c4ab6a5d3a23c4e57b0c08ea668e735277b24da811bd340c5f74e54";
    private const string Download = "https://github.com/Raptar212/BirthDay-Checker/releases/download/";

    /// <summary>The shape of GitHub's "latest release" answer, cut to the fields the check reads.</summary>
    private static JsonObject Release(string tag = "v2.3.0") => new()
    {
        ["tag_name"] = tag,
        ["draft"] = false,
        ["prerelease"] = false,
        ["assets"] = new JsonArray(
            new JsonObject { ["name"] = "notes.txt", ["browser_download_url"] = Download + tag + "/notes.txt" },
            new JsonObject
            {
                ["name"] = $"BirthdayChecker-Setup-{tag[1..]}.exe",
                ["browser_download_url"] = $"{Download}{tag}/BirthdayChecker-Setup-{tag[1..]}.exe",
                ["digest"] = "sha256:" + Hash.ToUpperInvariant(),
            }),
    };

    private static JsonObject Installer(JsonObject release) => (JsonObject)release["assets"]![1]!;

    [Fact]
    public void Parse_TakesVersionInstallerAndHash()
    {
        var r = UpdateCheck.Parse(Release().ToJsonString())!;
        Assert.Equal(new Version(2, 3, 0), r.Version);
        Assert.Equal(Download + "v2.3.0/BirthdayChecker-Setup-2.3.0.exe", r.Installer.ToString());
        Assert.Equal(Hash, r.Sha256);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    public void Parse_SkipsDraftsAndPreReleases(string flag)
    {
        var json = Release();
        json[flag] = true;
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("mobile-v0.57.0")]
    [InlineData("2.3.0")]
    [InlineData("v2.3")]
    [InlineData("v2.3.0.1")]
    [InlineData("v2.3.0-beta")]
    [InlineData("v+2.3.0")]
    public void Parse_SkipsOtherTags(string tag) => Assert.Null(UpdateCheck.Parse(Release(tag).ToJsonString()));

    [Fact]
    public void Parse_SkipsReleaseWithoutInstaller()
    {
        var json = Release();
        Installer(json)["name"] = "BirthdayChecker-Mobile-2.3.0.apk";
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("https://example.com/BirthdayChecker-Setup-2.3.0.exe")]
    [InlineData("http://github.com/Raptar212/BirthDay-Checker/releases/download/v2.3.0/BirthdayChecker-Setup-2.3.0.exe")]
    [InlineData("https://github.com/someone/BirthDay-Checker/releases/download/v2.3.0/BirthdayChecker-Setup-2.3.0.exe")]
    public void Parse_RefusesInstallerFromElsewhere(string url)
    {
        var json = Release();
        Installer(json)["browser_download_url"] = url;
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("md5:0123456789abcdef0123456789abcdef")]
    [InlineData("sha256:1234")]
    [InlineData("sha256:zz1b562c2c4ab6a5d3a23c4e57b0c08ea668e735277b24da811bd340c5f74e54")]
    public void Parse_RefusesInstallerWithoutHash(string? digest)
    {
        var json = Release();
        Installer(json)["digest"] = digest;
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
    }

    [Fact]
    public void TagVersion_KnownEvenWithoutInstaller()
    {
        var json = Release();
        Installer(json)["digest"] = null;
        Assert.Null(UpdateCheck.Parse(json.ToJsonString()));
        Assert.Equal(new Version(2, 3, 0), UpdateCheck.TagVersion(json.ToJsonString()));

        json["prerelease"] = true; // a phone release is never the PC's version
        Assert.Null(UpdateCheck.TagVersion(json.ToJsonString()));
        Assert.Null(UpdateCheck.TagVersion(Release("mobile-v0.57.0").ToJsonString()));
        Assert.Null(UpdateCheck.TagVersion("not json"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"message\":\"Not Found\"}")]
    [InlineData("{\"tag_name\":\"v2.3.0\",\"assets\":{}}")]
    public void Parse_GarbageIsNull(string json) => Assert.Null(UpdateCheck.Parse(json));

    /// <summary>A phone release as GitHub lists it: always a pre-release, an APK beside it.</summary>
    private static JsonObject MobileRelease(string version, bool draft = false) => new()
    {
        ["tag_name"] = "mobile-v" + version,
        ["draft"] = draft,
        ["prerelease"] = true,
        ["assets"] = new JsonArray(new JsonObject
        {
            ["name"] = $"BirthdayChecker-Mobile-{version}.apk",
            ["browser_download_url"] = $"{Download}mobile-v{version}/BirthdayChecker-Mobile-{version}.apk",
            ["digest"] = "sha256:" + Hash,
        }),
    };

    [Fact]
    public void ParseMobile_PicksNewestPhoneReleaseOfTheList()
    {
        // Not in version order, mixed with the PC's releases and a newer draft.
        var list = new JsonArray(Release("v9.0.0"), MobileRelease("0.57.0"), MobileRelease("0.61.0"), MobileRelease("0.9.0"),
            MobileRelease("1.0.0", draft: true));
        var r = UpdateCheck.ParseMobile(list.ToJsonString())!;
        Assert.Equal(new Version(0, 61, 0), r.Version);
        Assert.Equal(Download + "mobile-v0.61.0/BirthdayChecker-Mobile-0.61.0.apk", r.Installer.ToString());
        Assert.Equal(Hash, r.Sha256);
    }

    [Fact]
    public void ParseMobile_SkipsReleaseWithoutUsableApk()
    {
        var broken = MobileRelease("0.62.0");
        ((JsonObject)broken["assets"]![0]!)["browser_download_url"] = "https://example.com/BirthdayChecker-Mobile-0.62.0.apk";
        var list = new JsonArray(broken, MobileRelease("0.57.0"));
        Assert.Equal(new Version(0, 57, 0), UpdateCheck.ParseMobile(list.ToJsonString())!.Version);
        Assert.Equal(new Version(0, 62, 0), UpdateCheck.MobileTagVersion(list.ToJsonString())); // so a manual check can say so
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[{\"tag_name\":\"v2.3.0\"}]")]
    public void ParseMobile_NoPhoneReleaseIsNull(string json)
    {
        Assert.Null(UpdateCheck.ParseMobile(json));
        Assert.Null(UpdateCheck.MobileTagVersion(json));
    }

    [Fact]
    public void Parse_IgnoresPhoneRelease()
    {
        var phone = MobileRelease("0.61.0");
        phone["prerelease"] = false; // even if one were ever published as "latest"
        Assert.Null(UpdateCheck.Parse(phone.ToJsonString()));
        Assert.Null(UpdateCheck.TagVersion(phone.ToJsonString()));
    }

    [Theory]
    [InlineData("2.3.0", "2.2.0.0", true)]
    [InlineData("2.2.1", "2.2.0.0", true)]
    [InlineData("3.0.0", "2.10.5.0", true)]
    [InlineData("2.2.0", "2.2.0.0", false)] // the assembly's fourth number is not a newer version
    [InlineData("2.2.0", "2.2.0", false)]
    [InlineData("2.1.9", "2.2.0.0", false)]
    [InlineData("2.10.0", "2.9.0.0", true)] // numbers, not text
    public void IsNewer_ComparesThreeNumbers(string latest, string current, bool newer) =>
        Assert.Equal(newer, UpdateCheck.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Theory]
    [InlineData("2026-11-05 09:30", 12, "2026-11-05 12:00")]
    [InlineData("2026-11-05 12:00", 12, "2026-11-06 12:00")]
    [InlineData("2026-11-05 15:00", 12, "2026-11-06 12:00")]
    [InlineData("2026-12-31 23:30", 0, "2027-01-01 00:00")]
    [InlineData("2026-11-05 00:00", 0, "2026-11-06 00:00")]
    public void NextDaily_TodayIfAheadElseTomorrow(string now, int hour, string next) =>
        Assert.Equal(DateTime.Parse(next), UpdateCheck.NextDaily(DateTime.Parse(now), hour));

    [Fact]
    public void HashMatches_ChecksSha256()
    {
        using var file = new MemoryStream(Encoding.ASCII.GetBytes("abc"));
        const string abc = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        Assert.True(UpdateCheck.HashMatches(file, abc));
        file.Position = 0;
        Assert.False(UpdateCheck.HashMatches(file, Hash));
    }

    [Fact]
    public void Settings_UpdateDefaultsAndBadHour()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bdc-settings-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}");
            var defaults = AppSettings.Load(path);
            Assert.True(defaults.CheckUpdates);
            Assert.Equal(12, defaults.UpdateHour);

            File.WriteAllText(path, "{\"checkUpdates\":false,\"updateHour\":24}");
            var bad = AppSettings.Load(path);
            Assert.False(bad.CheckUpdates);
            Assert.Equal(12, bad.UpdateHour);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
