using Haedus.Core.Errors;
using Haedus.Core.History;
using Haedus.Core.Transcription;

namespace Haedus.Core.Tests;

public sealed class TranscriptHistoryTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static TranscriptionResult Result(string audio = "lezione: storia/1.mp3", int minute = 30, string text = "Testo della lezione.") => new(
        audio,
        TimeSpan.FromMinutes(90),
        "it",
        new ModelUsage("small", "Small", "ggml-small.bin", "abc", "Cpu"),
        [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(3), text, 0.9f)],
        TimeSpan.FromMinutes(12),
        new DateTimeOffset(2026, 10, 14, 18, minute, 5, TimeSpan.Zero));

    [Fact]
    public void Default_folder_is_documents_haedus_history()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        Assert.Equal(Path.Combine(documents, "Haedus", "history"), TranscriptHistory.DefaultDirectory);
    }

    [Fact]
    public void Each_transcription_gets_its_own_readable_files()
    {
        var history = new TranscriptHistory(Path.Combine(_dir.Path, "Haedus"));

        var first = history.Save(Result());
        var second = history.Save(Result());

        Assert.NotEqual(first.Id, second.Id);
        Assert.EndsWith("(2)", second.Id, StringComparison.Ordinal);
        Assert.DoesNotContain(':', first.Id);
        Assert.DoesNotContain('/', first.Id);
        Assert.Contains("lezione_ storia_1", first.Id, StringComparison.Ordinal);
        var files = Directory.GetFiles(history.Directory).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(4, files.Count);
        var txt = File.ReadAllText(Path.Combine(history.Directory, first.TextFileName));
        Assert.Contains("Modello: Small", txt, StringComparison.Ordinal);
        Assert.EndsWith("Testo della lezione.\n", txt, StringComparison.Ordinal);
    }

    [Fact]
    public void Edited_segments_replace_the_saved_ones_in_json_and_txt()
    {
        var history = new TranscriptHistory(_dir.Path);
        var saved = history.Save(Result());

        history.UpdateSegments(saved.Id, [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(5), "Testo corretto.", null)]);

        var loaded = history.Load(saved.Id);
        Assert.Equal("Testo corretto.", Assert.Single(loaded.Segments).Text);
        Assert.Equal("ggml-small.bin", loaded.Model.File);
        Assert.EndsWith("Testo corretto.\n", File.ReadAllText(Path.Combine(history.Directory, saved.TextFileName)), StringComparison.Ordinal);
        Assert.Equal("Testo corretto.", Assert.Single(history.List()).Preview);
        Assert.Throws<HaedusException>(() => history.UpdateSegments("assente", loaded.Segments));
    }

    [Fact]
    public void Original_audio_is_found_only_while_the_file_exists_and_survives_edits()
    {
        var history = new TranscriptHistory(_dir.Path);
        var audio = Path.Combine(_dir.Path, "lezione.mp3");
        File.WriteAllText(audio, "x");
        var saved = history.Save(Result(), audioPath: audio);
        var without = history.Save(Result());

        Assert.Equal(audio, history.FindAudio(saved.Id));
        Assert.Null(history.FindAudio(without.Id));

        history.UpdateSegments(saved.Id, [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "Corretto.", null)]);
        Assert.Equal(audio, history.FindAudio(saved.Id));

        File.Delete(audio);
        Assert.Null(history.FindAudio(saved.Id));
        Assert.Throws<HaedusException>(() => history.FindAudio("assente"));
    }

    [Fact]
    public void Audio_is_served_only_for_known_audio_extensions()
    {
        var history = new TranscriptHistory(_dir.Path);
        var secret = Path.Combine(_dir.Path, "segreti.txt");
        File.WriteAllText(secret, "x");

        Assert.Null(history.FindAudio(history.Save(Result(), audioPath: secret).Id));
    }

    [Fact]
    public void List_is_newest_first_and_round_trips_results()
    {
        var history = new TranscriptHistory(_dir.Path);
        history.Save(Result(audio: "a.wav", minute: 10));
        var newest = history.Save(Result(audio: "b.wav", minute: 50, text: new string('x', 400)));
        File.WriteAllText(Path.Combine(_dir.Path, "estraneo.json"), "{\"altro\":1}");
        File.WriteAllText(Path.Combine(_dir.Path, "rotto.json"), "{");

        var entries = history.List();
        var loaded = history.Load(newest.Id);

        Assert.Equal(["b.wav", "a.wav"], entries.Select(e => e.AudioFileName));
        Assert.EndsWith("…", entries[0].Preview, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(90), loaded.AudioDuration);
        Assert.Equal("ggml-small.bin", loaded.Model.File);
        Assert.Equal(TimeSpan.FromSeconds(3), loaded.Segments[0].End);
        Assert.Equal(new string('x', 400), loaded.Text);
    }

    [Fact]
    public void Delete_removes_both_files_and_missing_folder_is_empty()
    {
        var history = new TranscriptHistory(Path.Combine(_dir.Path, "nuova"));
        Assert.Empty(history.List());

        var entry = history.Save(Result(), language: "en");
        Assert.Contains("Model: Small", File.ReadAllText(Path.Combine(history.Directory, entry.TextFileName)), StringComparison.Ordinal);
        history.Delete(entry.Id);

        Assert.Empty(Directory.GetFiles(history.Directory));
        Assert.Equal(ErrorCodes.HistoryNotFound, Assert.Throws<HaedusException>(() => history.Load(entry.Id)).Code);
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<HaedusException>(() => history.Load("../x")).Code);
    }

    private const string ModelJson = "{\"modelId\":\"m\",\"displayName\":\"M\",\"file\":\"f\",\"sha256\":\"s\",\"runtime\":\"Cpu\"}";

    [Theory]
    [InlineData("{\"format\":\"haedus-transcript\",\"version\":1,\"result\":{}}")]
    [InlineData("{\"format\":\"haedus-transcript\",\"version\":1,\"result\":{\"audioFileName\":\"a.wav\",\"model\":null,\"segments\":[]}}")]
    [InlineData("{\"format\":\"haedus-transcript\",\"version\":1,\"result\":{\"audioFileName\":\"a.wav\",\"model\":" + ModelJson + ",\"segments\":null}}")]
    [InlineData("{\"format\":\"haedus-transcript\",\"version\":1,\"result\":{\"audioFileName\":\"a.wav\",\"model\":" + ModelJson + ",\"segments\":[null]}}")]
    public void Incomplete_transcript_json_is_ignored_by_list_and_load(string json)
    {
        var history = new TranscriptHistory(_dir.Path);
        var good = history.Save(Result());
        File.WriteAllText(Path.Combine(_dir.Path, "incompleto.json"), json);

        Assert.Equal([good.Id], history.List().Select(e => e.Id));
        Assert.Equal(ErrorCodes.HistoryNotFound, Assert.Throws<HaedusException>(() => history.Load("incompleto")).Code);
    }

    [Fact]
    public void An_unreadable_file_does_not_break_the_list()
    {
        var history = new TranscriptHistory(_dir.Path);
        var good = history.Save(Result());
        // Una cartella con estensione .json: la lettura lancia UnauthorizedAccessException o IOException.
        Directory.CreateDirectory(Path.Combine(_dir.Path, "cartella.json"));

        Assert.Equal([good.Id], history.List().Select(e => e.Id));
    }

    [Fact]
    public void Library_file_is_never_treated_as_a_transcript()
    {
        var history = new TranscriptHistory(_dir.Path);
        new HistoryLibrary(_dir.Path).SetTitle("a", "Lezione");
        var path = Path.Combine(_dir.Path, HistoryLibrary.FileName);

        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<HaedusException>(() => history.Delete("library")).Code);
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<HaedusException>(() => history.Delete("LIBRARY")).Code);
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<HaedusException>(() => history.Load("library")).Code);
        Assert.Equal(ErrorCodes.InvalidRequest, Assert.Throws<HaedusException>(() => history.UpdateSegments("library", [])).Code);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Delete_refuses_files_that_are_not_valid_transcripts()
    {
        var history = new TranscriptHistory(_dir.Path);
        var other = Path.Combine(_dir.Path, "altro.json");
        File.WriteAllText(other, "{\"altro\":1}");

        Assert.Equal(ErrorCodes.HistoryNotFound, Assert.Throws<HaedusException>(() => history.Delete("altro")).Code);
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Legacy_transcript_format_is_still_readable()
    {
        var history = new TranscriptHistory(_dir.Path);
        var entry = history.Save(Result());
        var path = Path.Combine(_dir.Path, entry.Id + ".json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("haedus-transcript", LegacyData.OldTranscriptFormat, StringComparison.Ordinal));

        Assert.Single(history.List());
    }
}
