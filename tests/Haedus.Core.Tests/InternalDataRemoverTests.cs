using Haedus.Core.Maintenance;

namespace Haedus.Core.Tests;

public class InternalDataRemoverTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose()
    {
        _dir.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Measure_sums_all_files_under_the_root()
    {
        var root = Path.Combine(_dir.Path, "Haedus.Data");
        Directory.CreateDirectory(Path.Combine(root, "models"));
        File.WriteAllBytes(Path.Combine(root, "models", "ggml-small.bin"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(root, "settings.json"), new byte[24]);

        Assert.Equal(1024, InternalDataRemover.Measure(root));
    }

    [Fact]
    public void Remove_deletes_models_logs_and_settings_but_not_siblings()
    {
        var root = Path.Combine(_dir.Path, "Haedus.Data");
        var history = Path.Combine(_dir.Path, "Documents", "Haedus", "history");
        Directory.CreateDirectory(Path.Combine(root, "models"));
        Directory.CreateDirectory(Path.Combine(root, "logs"));
        Directory.CreateDirectory(history);
        File.WriteAllText(Path.Combine(root, "models", "ggml-small.bin"), "m");
        File.WriteAllText(Path.Combine(root, "logs", "haedus.log"), "l");
        File.WriteAllText(Path.Combine(root, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(history, "trascrizione.txt"), "testo");

        var failures = InternalDataRemover.Remove(root);

        Assert.Empty(failures);
        Assert.False(Directory.Exists(root));
        Assert.True(File.Exists(Path.Combine(history, "trascrizione.txt")));
    }

    [Fact]
    public void Missing_root_is_a_no_op()
    {
        var root = Path.Combine(_dir.Path, "absent");

        Assert.Equal(0, InternalDataRemover.Measure(root));
        Assert.Empty(InternalDataRemover.Remove(root));
    }

    [Fact]
    public void Measure_does_not_follow_directory_symlinks()
    {
        if (OperatingSystem.IsWindows()) return;
        var (root, external, _) = CreateRootWithLinkToExternal();

        Assert.Equal(3, InternalDataRemover.Measure(root));
        Assert.True(Directory.Exists(external));
    }

    [Fact]
    public void Remove_unlinks_directory_symlinks_without_touching_the_target()
    {
        if (OperatingSystem.IsWindows()) return;
        var (root, external, externalFile) = CreateRootWithLinkToExternal();
        File.SetAttributes(externalFile, FileAttributes.ReadOnly);
        try
        {
            var failures = InternalDataRemover.Remove(root);

            Assert.Empty(failures);
            Assert.False(Directory.Exists(root));
            Assert.True(File.Exists(externalFile));
            Assert.Equal("esterno", File.ReadAllText(externalFile));
            Assert.True(File.GetAttributes(externalFile).HasFlag(FileAttributes.ReadOnly));
            Assert.True(Directory.Exists(external));
        }
        finally
        {
            File.SetAttributes(externalFile, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Remove_unlinks_file_symlinks_without_touching_the_target()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(_dir.Path, "Haedus.Data");
        Directory.CreateDirectory(root);
        var target = Path.Combine(_dir.Path, "esterno.txt");
        File.WriteAllText(target, "esterno");
        File.CreateSymbolicLink(Path.Combine(root, "link.txt"), target);

        Assert.Empty(InternalDataRemover.Remove(root));

        Assert.False(Directory.Exists(root));
        Assert.Equal("esterno", File.ReadAllText(target));
    }

    [Fact]
    public void Remove_still_deletes_regular_nested_content()
    {
        var root = Path.Combine(_dir.Path, "Haedus.Data");
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllText(Path.Combine(root, "a", "b", "f.txt"), "x");
        File.WriteAllText(Path.Combine(root, "top.txt"), "y");

        Assert.Empty(InternalDataRemover.Remove(root));

        Assert.False(Directory.Exists(root));
    }

    private (string Root, string External, string ExternalFile) CreateRootWithLinkToExternal()
    {
        var root = Path.Combine(_dir.Path, "Haedus.Data");
        var external = Path.Combine(_dir.Path, "esterno");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);
        File.WriteAllBytes(Path.Combine(root, "settings.json"), new byte[3]);
        var externalFile = Path.Combine(external, "model.bin");
        File.WriteAllText(externalFile, "esterno");
        Directory.CreateSymbolicLink(Path.Combine(root, "models"), external);
        return (root, external, externalFile);
    }
}
