using Haedus.Core.Errors;
using Haedus.Core.History;

namespace Haedus.Core.Tests;

public sealed class HistoryLibraryTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Titles_and_collections_survive_a_new_instance()
    {
        var library = new HistoryLibrary(_dir.Path);
        library.SetTitle("a", "  Lezione  ");
        var collection = library.SaveCollection(null, "Storia", "green");
        library.SetCollection("a", collection.Id);
        library.SetCollection("b", collection.Id);

        var read = new HistoryLibrary(_dir.Path).Read();

        Assert.Equal("Lezione", read.Titles["a"]);
        Assert.Equal(["a", "b"], Assert.Single(read.Collections).EntryIds);
        Assert.Equal(collection.Id, read.CollectionOf("b"));
    }

    [Fact]
    public void An_entry_belongs_to_one_collection_at_a_time()
    {
        var library = new HistoryLibrary(_dir.Path);
        var first = library.SaveCollection(null, "Uno", "rose");
        var second = library.SaveCollection(null, "Due", "sage");

        library.SetCollection("a", first.Id);
        library.SetCollection("a", second.Id);

        Assert.Equal(second.Id, library.Read().CollectionOf("a"));
        library.SetCollection("a", null);
        Assert.Null(library.Read().CollectionOf("a"));
    }

    [Fact]
    public void Reorder_requires_the_same_entries()
    {
        var library = new HistoryLibrary(_dir.Path);
        var collection = library.SaveCollection(null, "Storia", "ochre");
        library.SetCollection("a", collection.Id);
        library.SetCollection("b", collection.Id);

        Assert.Equal(["b", "a"], library.Reorder(collection.Id, ["b", "a"]).EntryIds);
        var ex = Assert.Throws<HaedusException>(() => library.Reorder(collection.Id, ["b", "b"]));
        Assert.Equal(ErrorCodes.InvalidRequest, ex.Code);
    }

    [Fact]
    public void Forget_removes_title_and_membership_without_creating_the_file()
    {
        var empty = new HistoryLibrary(_dir.Path);
        empty.Forget("a");
        Assert.False(File.Exists(Path.Combine(_dir.Path, HistoryLibrary.FileName)));

        var collection = empty.SaveCollection(null, "Storia", "bordeaux");
        empty.SetTitle("a", "Lezione");
        empty.SetCollection("a", collection.Id);
        empty.Forget("a");

        var read = empty.Read();
        Assert.Empty(read.Titles);
        Assert.Empty(Assert.Single(read.Collections).EntryIds);
    }

    [Fact]
    public void Invalid_names_colors_and_ids_are_rejected()
    {
        var library = new HistoryLibrary(_dir.Path);

        Assert.Throws<HaedusException>(() => library.SaveCollection(null, "  ", "green"));
        Assert.Throws<HaedusException>(() => library.SaveCollection(null, "Storia", "blu"));
        Assert.Throws<HaedusException>(() => library.DeleteCollection("col-assente"));
        Assert.Throws<HaedusException>(() => library.SetCollection("a", "col-assente"));
    }

    private string LibraryPath => Path.Combine(_dir.Path, HistoryLibrary.FileName);

    [Fact]
    public void A_future_version_is_never_overwritten()
    {
        var future = "{\"format\":\"haedus-library\",\"version\":2,\"titles\":{\"a\":\"Nuovo\"},\"collections\":[]}";
        File.WriteAllText(LibraryPath, future);
        var library = new HistoryLibrary(_dir.Path);

        Assert.Throws<HaedusException>(() => library.SetTitle("b", "X"));
        Assert.Throws<HaedusException>(() => library.SaveCollection(null, "Storia", "green"));

        Assert.Equal(future, File.ReadAllText(LibraryPath));
        Assert.False(File.Exists(LibraryPath + ".bak"));
        Assert.Empty(library.Read().Titles);
        library.Forget("a");
        Assert.Equal(future, File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void A_corrupt_file_is_backed_up_once_before_being_replaced()
    {
        File.WriteAllText(LibraryPath, "{ non json");
        var library = new HistoryLibrary(_dir.Path);
        Assert.Empty(library.Read().Titles);

        library.SetTitle("a", "Uno");

        Assert.Equal("{ non json", File.ReadAllText(LibraryPath + ".bak"));
        Assert.Equal("Uno", library.Read().Titles["a"]);

        // Ora il file è valido: un secondo aggiornamento non tocca il .bak.
        library.SetTitle("b", "Due");
        Assert.Equal("{ non json", File.ReadAllText(LibraryPath + ".bak"));
    }

    [Fact]
    public void An_existing_backup_is_not_overwritten()
    {
        File.WriteAllText(LibraryPath + ".bak", "vecchio backup");
        File.WriteAllText(LibraryPath, "[1,2");

        new HistoryLibrary(_dir.Path).SetTitle("a", "Uno");

        Assert.Equal("vecchio backup", File.ReadAllText(LibraryPath + ".bak"));
    }

    [Fact]
    public void An_unreadable_library_path_refuses_writes_but_reads_stay_empty()
    {
        // Una cartella al posto di library.json: la lettura fallisce con un errore di I/O o di permessi.
        Directory.CreateDirectory(LibraryPath);
        var library = new HistoryLibrary(_dir.Path);

        Assert.Empty(library.Read().Titles);
        Assert.Throws<HaedusException>(() => library.SetTitle("a", "X"));
    }

    [Fact]
    public void Legacy_library_format_is_still_readable()
    {
        File.WriteAllText(LibraryPath, "{\"format\":\"" + LegacyData.OldLibraryFormat + "\",\"version\":1,\"titles\":{\"a\":\"Uno\"},\"collections\":[]}");

        Assert.Equal("Uno", new HistoryLibrary(_dir.Path).Read().Titles["a"]);
    }
}
