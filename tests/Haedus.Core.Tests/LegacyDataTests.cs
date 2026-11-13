using Haedus.Core;
using Haedus.Core.History;

namespace Haedus.Core.Tests;

public sealed class LegacyDataTests
{
    [Fact]
    public void Old_data_and_history_folders_are_moved_to_the_new_names()
    {
        using var dir = new TempDir();
        var local = Path.Combine(dir.Path, "Local");
        var docs = Path.Combine(dir.Path, "Docs");
        Directory.CreateDirectory(Path.Combine(local, "CodexVTT.Data", "models"));
        File.WriteAllText(Path.Combine(local, "CodexVTT.Data", "models", "m.bin"), "x");
        Directory.CreateDirectory(Path.Combine(docs, "CodexVTT", "history"));
        File.WriteAllText(Path.Combine(docs, "CodexVTT", "history", "a.json"), "{}");

        LegacyData.Migrate(local, docs);

        Assert.True(File.Exists(Path.Combine(local, "Haedus.Data", "models", "m.bin")));
        Assert.True(File.Exists(Path.Combine(docs, "Haedus", "history", "a.json")));
        Assert.False(Directory.Exists(Path.Combine(local, "CodexVTT.Data")));
    }

    [Fact]
    public void Existing_new_folders_are_never_overwritten()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "CodexVTT.Data"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "Haedus.Data"));
        File.WriteAllText(Path.Combine(dir.Path, "Haedus.Data", "keep.txt"), "x");

        LegacyData.Migrate(dir.Path, dir.Path);

        Assert.True(File.Exists(Path.Combine(dir.Path, "Haedus.Data", "keep.txt")));
        Assert.True(Directory.Exists(Path.Combine(dir.Path, "CodexVTT.Data")));
    }

    [Fact]
    public void Library_written_with_the_old_format_name_is_still_read()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, HistoryLibrary.FileName),
            """{"format":"codexvtt-library","version":1,"titles":{"a":"Titolo"},"collections":[]}""");

        Assert.Equal("Titolo", new HistoryLibrary(dir.Path).Read().Titles["a"]);
    }
}
