using System.Security.Cryptography;
using System.Text.Json;

namespace BirthdayChecker.Core;

/// <summary>A desktop release on GitHub: its version, the installer to download and the installer's SHA-256 (hex).</summary>
public sealed record UpdateRelease(Version Version, Uri Installer, string Sha256);

/// <summary>
/// The PC's update check: GitHub's "latest release" (phone releases are pre-releases, never "latest"),
/// its installer and the daily check time. No network here — the app downloads, this decides.
/// </summary>
public static class UpdateCheck
{
    public const string LatestUrl = "https://api.github.com/repos/Raptar212/BirthDay-Checker/releases/latest";

    /// <summary>Only an installer from this repository's releases is ever downloaded.</summary>
    private const string DownloadPrefix = "https://github.com/Raptar212/BirthDay-Checker/releases/download/";

    /// <summary>
    /// The release in GitHub's JSON, or null when it is not a usable desktop release: a draft or pre-release,
    /// a tag other than "v1.2.3", no installer, an installer from elsewhere or without a SHA-256.
    /// </summary>
    public static UpdateRelease? Parse(string json) => Read(json, out _);

    /// <summary>
    /// The version of a desktop release even when <see cref="Parse"/> refuses its installer, so a manual check
    /// can say "a new version exists, but without an installer" instead of "this is the latest".
    /// </summary>
    public static Version? TagVersion(string json)
    {
        Read(json, out var version);
        return version;
    }

    private static UpdateRelease? Read(string json, out Version? version)
    {
        version = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Bool(root, "draft") || Bool(root, "prerelease"))
                return null;
            if (Str(root, "tag_name") is not { } tag || !tag.StartsWith('v') || ParseVersion(tag[1..]) is not { } v)
                return null;
            version = v;
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;
            var expectedName = $"BirthdayChecker-Setup-{tag[1..]}.exe";
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object || Str(asset, "name") != expectedName)
                    continue;
                if (Str(asset, "browser_download_url") is not { } url || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal)
                    || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    return null;
                if (Str(asset, "digest") is not { } digest || !digest.StartsWith("sha256:", StringComparison.Ordinal)
                    || digest.Length != 7 + 64 || !digest[7..].All(Uri.IsHexDigit))
                    return null;
                return new UpdateRelease(v, uri, digest[7..].ToLowerInvariant());
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Compares major.minor.patch only: the assembly's 2.2.0.0 equals the tag's 2.2.0.</summary>
    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

    /// <summary>The next daily check at <paramref name="hour"/>:00, today if it is still ahead.</summary>
    public static DateTime NextDaily(DateTime now, int hour)
    {
        var today = now.Date.AddHours(hour);
        return today > now ? today : today.AddDays(1);
    }

    /// <summary>True when the file's SHA-256 is <paramref name="sha256"/> (hex, any case).</summary>
    public static bool HashMatches(Stream file, string sha256) =>
        Convert.ToHexStringLower(SHA256.HashData(file)) == sha256.ToLowerInvariant();

    /// <summary>"2.3.0" → 2.3.0; three plain numbers exactly, anything else is null.</summary>
    private static Version? ParseVersion(string text) =>
        text.Split('.') is { Length: 3 } parts && parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit))
            && Version.TryParse(text, out var v) ? v : null;

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
