using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BirthdayChecker.Core;

/// <summary>
/// Entries file (JSON, next to the exe). Save is atomic: validate, write *.tmp, flush to disk, then swap,
/// keeping the previous version as data\*.bak. Load never modifies the file.
/// </summary>
/// <remarks>
/// ponytail: single writer assumed (only the editor saves); two processes saving at once can lose the
/// older one's changes — add a lock file if the tray ever needs to write. One backup level only.
/// </remarks>
public static class EntryStorage
{
    /// <summary>Written by Save. 2 added the optional entry "color", 3 the optional "customFields"; older files still load as is.</summary>
    public const int FormatVersion = 3;

    private const int OldestReadableVersion = 1;

    private const Reminder AllReminders = (Reminder)((1 << 13) - 1);

    // Strict on purpose: the file is hand-editable, and anything the model doesn't know would be
    // silently dropped by the next Save. Any new field therefore requires bumping FormatVersion.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Cyrillic readable in the file
        AllowOutOfOrderMetadataProperties = true,              // tolerate hand edits that move "kind"
        RespectRequiredConstructorParameters = true,           // a missing ctor field is an error, not default(T)
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record FileModel(int FormatVersion, List<Entry> Entries);

    private sealed record VersionProbe(int FormatVersion);

    /// <summary>The previous version of <paramref name="path"/>, in the service folder beside it.</summary>
    public static string BackupPath(string path)
    {
        var full = Path.GetFullPath(path);
        return Path.Combine(AppLayout.DataFolder(Path.GetDirectoryName(full)!), Path.GetFileName(full) + ".bak");
    }

    /// <summary>
    /// Missing file (and no leftover *.bak / *.tmp) gives an empty list. Invalid content throws
    /// <see cref="InvalidDataException"/>; OS errors (locked, no access) surface as <see cref="IOException"/> /
    /// <see cref="UnauthorizedAccessException"/>. Callers must not Save after a failed Load.
    /// </summary>
    public static IReadOnlyList<Entry> Load(string path)
    {
        if (!File.Exists(path))
        {
            foreach (var leftover in new[] { BackupPath(path), path + ".tmp" })
                if (File.Exists(leftover))
                    throw new InvalidDataException(
                        $"Файл записей «{path}» отсутствует, но есть «{leftover}». Восстановите его переименованием.");
            return [];
        }

        var bytes = ReadShared(path);
        // Lenient probe first, so a newer file reports "format N" instead of an unknown-field error.
        int version = Parse<VersionProbe>(bytes, path, JsonSerializerOptions.Web)?.FormatVersion ?? 0;
        if (version is < OldestReadableVersion or > FormatVersion)
            throw new InvalidDataException(
                $"Файл записей «{path}» в формате {version}, программа понимает формат {FormatVersion}. Файл не изменён.");

        var model = Parse<FileModel>(bytes, path, Options)
            ?? throw new InvalidDataException($"Файл записей «{path}» пуст.");
        Validate(model.Entries, path);
        return model.Entries;
    }

    /// <summary>Throws <see cref="InvalidDataException"/> before touching disk if any entry would not load back.</summary>
    public static void Save(string path, IEnumerable<Entry> entries)
    {
        var list = entries.ToList();
        Validate(list, path);

        var backup = BackupPath(path);
        bool exists = File.Exists(path);
        if (!exists && File.Exists(backup))
            throw new InvalidDataException(
                $"Файл записей «{path}» отсутствует, но есть «{backup}». Восстановите его перед сохранением.");

        var tmp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new FileModel(FormatVersion, list), Options);
                stream.Flush(flushToDisk: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            if (exists)
                File.Replace(tmp, path, backup);
            else
                File.Move(tmp, path);
        }
        catch
        {
            // If Replace already moved the old file to .bak but failed to move .tmp in, .tmp is the
            // only copy of the newest data — keep it for manual recovery.
            if (File.Exists(path) || !File.Exists(backup))
                try { File.Delete(tmp); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>The same document Save writes, for export to a file of the user's choice (no .bak beside it).</summary>
    public static string ToJson(IEnumerable<Entry> entries)
    {
        var list = entries.ToList();
        Validate(list, "экспорт");
        return JsonSerializer.Serialize(new FileModel(FormatVersion, list), Options);
    }

    private static ReadOnlyMemory<byte> ReadShared(string path)
    {
        // Share delete/write so a concurrent Save's File.Replace isn't blocked by this reader.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray().AsMemory();
        // Notepad and other editors may add a UTF-8 BOM, which the span-based reader rejects.
        return bytes.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes[3..] : bytes;
    }

    private static T? Parse<T>(ReadOnlyMemory<byte> utf8, string path, JsonSerializerOptions options)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(utf8.Span, options);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            throw new InvalidDataException($"Файл записей «{path}» повреждён: {e.Message}", e);
        }
    }

    private static void Validate(List<Entry> entries, string path)
    {
        if (entries is null)
            throw new InvalidDataException($"В файле записей «{path}» нет списка записей.");

        var ids = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            string? problem = entry switch
            {
                null => "пустая запись",
                { Name: var n } when string.IsNullOrWhiteSpace(n) => "запись без имени",
                { EventType: var t } when string.IsNullOrWhiteSpace(t) => $"у «{entry.Name}» нет типа события",
                { Date: null } => $"у «{entry.Name}» нет даты",
                { Id: var id } when id == Guid.Empty => $"у «{entry.Name}» пустой id",
                { Id: var id } when !ids.Add(id) => $"у «{entry.Name}» повторяется id {entry.Id}",
                { Tags: var tags } when tags is null || tags.Any(string.IsNullOrWhiteSpace) => $"у «{entry.Name}» пустой тег",
                _ when (entry.Reminders & ~AllReminders) != 0 => $"у «{entry.Name}» неизвестные флаги оповещения",
                { Color: { } c } when !Entry.IsColor(c) => $"у «{entry.Name}» цвет «{c}» не в формате #RRGGBB",
                { CustomFields: { } f } when !ColumnCatalog.AreValid(f) => $"у «{entry.Name}» неверное название или пустое значение своего столбца",
                _ => null,
            };
            if (problem is not null)
                throw new InvalidDataException($"Файл записей «{path}»: {problem}.");
        }
    }
}
