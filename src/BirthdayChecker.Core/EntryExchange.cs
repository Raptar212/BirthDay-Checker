using System.Text;

namespace BirthdayChecker.Core;

/// <summary>
/// Export and import by file extension: .csv (Excel), .txt (tab-separated), .md (table), .json (entries.json format).
/// Every format keeps every field; merging into the list is up to the caller (see <see cref="EntryCsv.Merge"/>).
/// An unknown extension is treated as CSV.
/// </summary>
/// <remarks>
/// JSON and TXT keep text exactly, except that TXT, MD and CSV trim the ends of each field. MD writes any line break
/// as one kind, CSV drops double quotes from a phone (Excel's ="..." guard). CSV, TXT and MD split a tag containing
/// "," or ";", which only a hand-edited JSON can hold.
/// </remarks>
public static class EntryExchange
{
    /// <summary>For the save and open dialogs; the same order as the formats above.</summary>
    public const string Filter = "CSV для Excel (*.csv)|*.csv|Текст с табуляцией (*.txt)|*.txt|Markdown (*.md)|*.md|JSON (*.json)|*.json";

    private const string Br = "<br>";

    /// <summary>Via a temp file: a failed write leaves an older export at <paramref name="path"/> intact.</summary>
    public static void Write(string path, IEnumerable<Entry> entries)
    {
        var (text, encoding) = Extension(path) switch
        {
            ".txt" => (ToTabs(entries), new UTF8Encoding(false)),
            ".md" => (ToMarkdown(entries), new UTF8Encoding(false)),
            ".json" => (EntryStorage.ToJson(entries), new UTF8Encoding(false)),
            _ => (EntryCsv.Export(entries), new UTF8Encoding(true)), // BOM so Excel recognizes UTF-8 and shows Cyrillic correctly
        };
        var tmp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(encoding.GetPreamble());
                stream.Write(encoding.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>The program's own files in its data folder: never an export target (entries.json has one writer, see <see cref="EntryStorage"/>).</summary>
    public static bool IsAppFile(string path, string dataFolder)
    {
        var full = Path.GetFullPath(path);
        var name = Path.GetFileName(full);
        var folder = Path.GetDirectoryName(full);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataFolder));
        return (string.Equals(folder, root, StringComparison.OrdinalIgnoreCase)
                || string.Equals(folder, AppLayout.DataFolder(root), StringComparison.OrdinalIgnoreCase))
            && (name.StartsWith("entries.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
                || name.Equals("state.json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Throws <see cref="InvalidDataException"/> on a broken JSON file, IO exceptions on an unreadable one.</summary>
    public static ImportResult Read(string path) => Extension(path) switch
    {
        ".json" => new ImportResult(EntryStorage.Load(path), []),
        ".txt" => FromTabs(EntryCsv.Decode(File.ReadAllBytes(path))),
        ".md" => FromMarkdown(EntryCsv.Decode(File.ReadAllBytes(path))),
        _ => EntryCsv.Parse(EntryCsv.Decode(File.ReadAllBytes(path))),
    };

    private static string Extension(string path) => Path.GetExtension(path).ToLowerInvariant();

    // ---- TXT: one line per entry, tabs between fields, \t \n \r \\ inside them -------------------

    private static string ToTabs(IEnumerable<Entry> entries) =>
        string.Concat(EntryCsv.ToRows(entries, excel: false).Select(row => string.Join('\t', row.Select(EscapeTabs)) + "\r\n"));

    private static string EscapeTabs(string s) =>
        s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

    private static string UnescapeTabs(string s)
    {
        var result = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 == s.Length)
            {
                result.Append(s[i]);
                continue;
            }
            char next = s[++i];
            // A hand-typed C:\dir must survive: only our own escapes are decoded.
            result.Append(next switch { 't' => "\t", 'n' => "\n", 'r' => "\r", '\\' => "\\", _ => "\\" + next });
        }
        return result.ToString();
    }

    private static ImportResult FromTabs(string text)
    {
        var rows = Lines(text)
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .Select(l => (l.Line, l.Text.Split('\t').Select(UnescapeTabs).ToList()))
            .ToList();
        return EntryCsv.FromRows(rows, excel: false);
    }

    // ---- MD: a table; \| \\ inside cells, <br> for a line break, \<br> for those literal characters ----

    private static string ToMarkdown(IEnumerable<Entry> entries)
    {
        var text = new StringBuilder();
        bool header = true;
        foreach (var row in EntryCsv.ToRows(entries, excel: false))
        {
            text.Append("| ").AppendJoin(" | ", row.Select(EscapeCell)).Append(" |\r\n");
            if (header)
                text.Append('|').Append(string.Concat(row.Select(_ => " --- |"))).Append("\r\n");
            header = false;
        }
        return text.ToString();
    }

    private static string EscapeCell(string s) =>
        s.Replace("\\", "\\\\").Replace("|", "\\|").Replace(Br, "\\" + Br).ReplaceLineEndings(Br);

    private static ImportResult FromMarkdown(string text)
    {
        // Anything around the table (a title, notes) is ignored; inside it, a line that isn't a row is reported, not lost.
        var lines = Lines(text).ToList();
        bool IsRow((int Line, string Text) l) => l.Text.TrimStart().StartsWith('|');
        var table = lines.SkipWhile(l => !IsRow(l)).Reverse().SkipWhile(l => !IsRow(l)).Reverse().ToList();
        var strays = table.Where(l => !IsRow(l) && !string.IsNullOrWhiteSpace(l.Text))
            .Select(l => new ImportIssue(l.Line, "", "строка внутри таблицы не начинается с «|» — пропущена, проверьте запись выше"));
        var rows = table.Where(IsRow)
            .Select(l => (l.Line, Fields: Cells(l.Text.Trim())))
            .Where(r => !r.Fields.All(IsDivider))
            .Select(r => (r.Line, r.Fields))
            .ToList();
        var result = EntryCsv.FromRows(rows, excel: false);
        return new ImportResult(result.Entries, [.. strays, .. result.Issues]);
    }

    private static bool IsDivider(string cell)
    {
        var dashes = cell.Trim().Trim(':');
        return dashes.Length > 0 && dashes.All(c => c == '-');
    }

    /// <summary>"| a | b\|c |" → a, b|c: only unescaped pipes separate cells.</summary>
    private static List<string> Cells(string row)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        for (int i = 1; i < row.Length; i++) // the leading pipe is known to be there
        {
            // A hand-typed C:\dir must survive: only our own escapes are decoded.
            if (row[i] == '\\' && i + 1 < row.Length
                && (row[i + 1] is '\\' or '|' || string.CompareOrdinal(row, i + 1, Br, 0, Br.Length) == 0))
                cell.Append(row[++i]);
            else if (row[i] == '|')
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else if (string.CompareOrdinal(row, i, Br, 0, Br.Length) == 0)
            {
                cell.Append(Environment.NewLine);
                i += Br.Length - 1;
            }
            else
                cell.Append(row[i]);
        }
        if (!string.IsNullOrWhiteSpace(cell.ToString()))
            cells.Add(cell.ToString()); // a row without the closing pipe
        return cells;
    }

    // Not ReplaceLineEndings: it also breaks on U+2028 and the like, which TXT keeps inside a field.
    private static IEnumerable<(int Line, string Text)> Lines(string text) =>
        text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select((line, i) => (i + 1, line));
}
