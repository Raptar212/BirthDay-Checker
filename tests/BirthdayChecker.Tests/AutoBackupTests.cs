using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public sealed class AutoBackupTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 6, 10);
    private readonly string _dir = Directory.CreateTempSubdirectory("bdc-backup-").FullName;

    private string Entries => AppLayout.EntriesPath(_dir);
    private string Folder => AppLayout.BackupFolder(_dir);
    private string[] Copies => Directory.GetFiles(Folder).Select(p => Path.GetFileName(p)!).ToArray();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void SaveSample(string name = "Иван") => EntryStorage.Save(Entries, [new Entry(name, "Праздник", new FixedDate(1, 1))]);

    [Fact]
    public void FirstRun_CreatesFolderAndNamedCopy()
    {
        SaveSample();
        var path = AutoBackup.RunIfDue(Entries, Folder, 7, Today);
        Assert.Equal(Path.Combine(Folder, "backup-BDC-2026-06-10.json"), path);
        Assert.Single(EntryStorage.Load(path!));
        Assert.Equal(["backup-BDC-2026-06-10.json"], Copies);
    }

    [Fact]
    public void BeforePeriod_NoNewCopy_AfterPeriod_OldOneIsReplaced()
    {
        SaveSample();
        AutoBackup.RunIfDue(Entries, Folder, 7, Today);

        Assert.Null(AutoBackup.RunIfDue(Entries, Folder, 7, Today.AddDays(6)));
        Assert.Equal(["backup-BDC-2026-06-10.json"], Copies);

        Assert.NotNull(AutoBackup.RunIfDue(Entries, Folder, 7, Today.AddDays(7)));
        Assert.Equal(["backup-BDC-2026-06-17.json"], Copies);
    }

    [Fact]
    public void HandNamedFile_IsNotDeleted()
    {
        SaveSample();
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "backup-BDC-before-edit.json"), "mine");

        AutoBackup.RunIfDue(Entries, Folder, 7, Today);

        Assert.Equal("mine", File.ReadAllText(Path.Combine(Folder, "backup-BDC-before-edit.json")));
        Assert.True(File.Exists(Path.Combine(Folder, "backup-BDC-2026-06-10.json")));
    }

    [Fact]
    public void ZeroPeriod_IsOff()
    {
        SaveSample();
        Assert.Null(AutoBackup.RunIfDue(Entries, Folder, 0, Today));
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void BrokenEntries_KeepTheGoodCopy()
    {
        SaveSample();
        AutoBackup.RunIfDue(Entries, Folder, 1, Today);
        File.WriteAllText(Entries, "{ broken");

        Assert.Throws<InvalidDataException>(() => AutoBackup.RunIfDue(Entries, Folder, 1, Today.AddDays(1)));

        Assert.Equal(["backup-BDC-2026-06-10.json"], Copies);
    }

    [Fact]
    public void EmptyList_KeepsTheGoodCopy()
    {
        SaveSample();
        AutoBackup.RunIfDue(Entries, Folder, 1, Today);
        EntryStorage.Save(Entries, []);

        Assert.Null(AutoBackup.RunIfDue(Entries, Folder, 1, Today.AddDays(1)));

        Assert.Equal(["backup-BDC-2026-06-10.json"], Copies);
    }

    [Fact]
    public void CopyDatedInTheFuture_IsReplaced()
    {
        SaveSample();
        AutoBackup.RunIfDue(Entries, Folder, 30, Today.AddYears(1));
        Assert.NotNull(AutoBackup.RunIfDue(Entries, Folder, 30, Today));
        Assert.Equal(["backup-BDC-2026-06-10.json"], Copies);
    }

    [Fact]
    public void MoveOldFiles_MovesServiceFiles_KeepsEntriesAndSettings_NeverOverwrites()
    {
        File.WriteAllText(Entries, "entries");
        File.WriteAllText(AppLayout.SettingsPath(_dir), "settings");
        File.WriteAllText(Path.Combine(_dir, "state.json"), "old state");
        File.WriteAllText(Path.Combine(_dir, "errors.log"), "old log");
        File.WriteAllText(Entries + ".bak", "bak");
        File.WriteAllText(Entries + ".bak-20260101-000000", "kept bak");
        Directory.CreateDirectory(AppLayout.DataFolder(_dir));
        File.WriteAllText(AppLayout.ErrorLogPath(_dir), "new log");

        AppLayout.MoveOldFiles(_dir);

        Assert.Equal("old state", File.ReadAllText(AppLayout.StatePath(_dir)));
        Assert.Equal("bak", File.ReadAllText(EntryStorage.BackupPath(Entries)));
        Assert.Equal("kept bak", File.ReadAllText(EntryStorage.BackupPath(Entries) + "-20260101-000000"));
        Assert.Equal("new log", File.ReadAllText(AppLayout.ErrorLogPath(_dir)));
        Assert.True(File.Exists(Entries));
        Assert.True(File.Exists(AppLayout.SettingsPath(_dir)));
        Assert.False(File.Exists(Path.Combine(_dir, "state.json")));
    }
}
