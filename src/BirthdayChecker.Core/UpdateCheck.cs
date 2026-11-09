using System.Security.Cryptography;
using System.Text.Json;

namespace BirthdayChecker.Core;

/// <summary>A release on GitHub: its version, the file to download (installer or APK) and the file's SHA-256 (hex).</summary>
public sealed record UpdateRelease(Version Version, Uri Installer, string Sha256);

/// <summary>
/// The update check of the PC (GitHub's "latest release": phone releases are pre-releases, never "latest")
/// and of the phone (the newest "mobile-v" release in the list). No network here — the apps download, this decides.
/// </summary>
public static class UpdateCheck
{
    public const string LatestUrl = "https://api.github.com/repos/Raptar212/BirthDay-Checker/releases/latest";

    /// <summary>The newest releases, both apps mixed; the phone picks its own from them.</summary>
    public const string ReleasesUrl = "https://api.github.com/repos/Raptar212/BirthDay-Checker/releases?per_page=30";

    /// <summary>Only a file from this repository's releases is ever downloaded.</summary>
    private const string DownloadPrefix = "https://github.com/Raptar212/BirthDay-Checker/releases/download/";

    private static readonly Kind Desktop = new("v", v => $"BirthdayChecker-Setup-{v}.exe", AllowPrerelease: false);
    private static readonly Kind Mobile = new("mobile-v", v => $"BirthdayChecker-Mobile-{v}.apk", AllowPrerelease: true);

    /// <summary>Tag prefix, file name by version, and whether a pre-release counts (the phone's always are).</summary>
    private sealed record Kind(string TagPrefix, Func<string, string> FileName, bool AllowPrerelease);

    /// <summary>
    /// The release in GitHub's JSON, or null when it is not a usable desktop release: a draft or pre-release,
    /// a tag other than "v1.2.3", no installer, an installer from elsewhere or without a SHA-256.
    /// </summary>
    public static UpdateRelease? Parse(string json) => Load(json, root => Read(root, Desktop, out _));

    /// <summary>
    /// The version of a desktop release even when <see cref="Parse"/> refuses its installer, so a manual check
    /// can say "a new version exists, but without an installer" instead of "this is the latest".
    /// </summary>
    public static Version? TagVersion(string json)
    {
        Version? version = null;
        Load(json, root => Read(root, Desktop, out version));
        return version;
    }

    /// <summary>
    /// The newest phone release in GitHub's release list ("mobile-v1.2.3" with its APK and SHA-256); drafts and the
    /// PC's releases are skipped. Null when there is none.
    /// </summary>
    public static UpdateRelease? ParseMobile(string json) => Load(json, root =>
        root.ValueKind != JsonValueKind.Array ? null
            : root.EnumerateArray().Select(r => Read(r, Mobile, out _)).OfType<UpdateRelease>().MaxBy(r => r.Version));

    /// <summary>
    /// The newest phone version tagged in the list even without a usable APK, as <see cref="TagVersion"/> for the PC.
    /// </summary>
    public static Version? MobileTagVersion(string json) => Load(json, root =>
        root.ValueKind != JsonValueKind.Array ? null
            : root.EnumerateArray().Select(r => { Read(r, Mobile, out var v); return v; }).OfType<Version>().Max());

    private static T? Load<T>(string json, Func<JsonElement, T?> read) where T : class
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return read(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UpdateRelease? Read(JsonElement root, Kind kind, out Version? version)
    {
        version = null;
        if (root.ValueKind != JsonValueKind.Object || Bool(root, "draft") || (!kind.AllowPrerelease && Bool(root, "prerelease")))
            return null;
        if (Str(root, "tag_name") is not { } tag || !tag.StartsWith(kind.TagPrefix, StringComparison.Ordinal)
            || ParseVersion(tag[kind.TagPrefix.Length..]) is not { } v)
            return null;
        version = v;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        var expectedName = kind.FileName(tag[kind.TagPrefix.Length..]);
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

    /// <summary>Compares major.minor.patch only: the assembly's 2.2.0.0 equals the tag's 2.2.0.</summary>
    public static bool IsNewer(Version latest, Version current) => Normalize(latest) > Normalize(current);

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
