using BirthdayChecker.Core;

namespace BirthdayChecker.Tests;

public class TagCatalogTests
{
    private static Entry WithTags(params string[] tags) =>
        new("Test Person", "Birthday", new FixedDate(6, 10)) { Tags = tags };

    private static readonly TagDefinition[] Tags = [new("семья", "#FF0000"), new("друзья", "#00FF00"), new("дача")];

    [Fact]
    public void Merge_DefaultsUntilEdited_ThenUsedTagsLast()
    {
        var merged = TagCatalog.Merge(null, [WithTags("дача", "семья")]);
        Assert.Equal([.. EntryList.DefaultTags, "дача"], merged.Select(d => d.Name));
    }

    [Fact]
    public void Merge_KeepsSavedList_DeletedDefaultStaysDeleted()
    {
        var merged = TagCatalog.Merge([new TagDefinition("дача", "#123456")], [WithTags("Дача", "гараж")]);
        Assert.Equal([new TagDefinition("дача", "#123456"), new TagDefinition("гараж")], merged);
    }

    [Fact]
    public void RowColor_FromTag_CaseInsensitive() =>
        Assert.Equal("#FF0000", TagCatalog.RowColor(WithTags("Семья"), Tags));

    [Fact]
    public void RowColor_EntryColorWins() =>
        Assert.Equal("#ABCDEF", TagCatalog.RowColor(WithTags("семья") with { Color = "#ABCDEF" }, Tags));

    [Fact]
    public void RowColor_SeveralTags_CatalogOrderDecides() =>
        Assert.Equal("#FF0000", TagCatalog.RowColor(WithTags("друзья", "семья"), Tags));

    [Fact]
    public void RowColor_SkipsTagWithoutColor() =>
        Assert.Equal("#00FF00", TagCatalog.RowColor(WithTags("дача", "друзья"), Tags));

    [Fact]
    public void RowColor_NoneWithoutColoredTag() =>
        Assert.Null(TagCatalog.RowColor(WithTags("дача"), Tags));

    [Theory]
    [InlineData("", null, true)]
    [InlineData("  ", null, true)]
    [InlineData("а,б", null, true)]
    [InlineData("а;б", null, true)]
    [InlineData("СЕМЬЯ", null, true)]
    [InlineData("СЕМЬЯ", "дача", true)]
    [InlineData("СЕМЬЯ", "семья", false)]
    [InlineData("гараж", null, false)]
    public void NameProblem(string name, string? renaming, bool problem) =>
        Assert.Equal(problem, TagCatalog.NameProblem(name, Tags, renaming) is not null);

    [Fact]
    public void Rename_KeepsColorAndPlace()
    {
        var renamed = TagCatalog.Rename(Tags, "Семья", "родные");
        Assert.Equal(new TagDefinition("родные", "#FF0000"), renamed[0]);
        Assert.Equal(3, renamed.Count);
    }

    [Fact]
    public void RenameIn_ChangesOnlyEntriesWithTheTag()
    {
        var other = WithTags("дача");
        var result = TagCatalog.RenameIn([WithTags("Семья", "дача"), other], "семья", "родные");
        Assert.Equal(["родные", "дача"], result[0].Tags);
        Assert.Same(other, result[1]);
    }

    [Fact]
    public void RemoveFrom_LeavesEntriesAndOtherTags()
    {
        var result = TagCatalog.RemoveFrom([WithTags("Семья", "дача"), WithTags("семья")], "семья");
        Assert.Equal(["дача"], result[0].Tags);
        Assert.Empty(result[1].Tags);
    }

    [Fact]
    public void AddRecolorRemove_OnTheList()
    {
        var added = TagCatalog.Add(Tags, "гараж");
        Assert.Equal("гараж", added[^1].Name);
        Assert.Null(TagCatalog.Recolor(added, "семья", null)[0].Color);
        Assert.Equal(["друзья", "дача", "гараж"], TagCatalog.Remove(added, "СЕМЬЯ").Select(d => d.Name));
    }

    [Fact]
    public void Settings_RoundTrip_AndDropBadTags()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                {"tags":[{"name":"семья","color":"#FF0000"},{"name":"Семья"},{"name":"а,б"},{"name":""},null,{"name":"дача","color":"red"}]}
                """);
            var loaded = AppSettings.Load(path);
            Assert.Equal([new TagDefinition("семья", "#FF0000"), new TagDefinition("дача")], loaded.Tags!);

            AppSettings.Save(path, loaded);
            Assert.Equal(loaded.Tags!, AppSettings.Load(path).Tags!);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
