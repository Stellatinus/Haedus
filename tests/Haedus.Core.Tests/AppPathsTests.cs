using Haedus.Core;

namespace Haedus.Core.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void Default_data_root_is_outside_the_folder_managed_by_the_installer()
    {
        // Un percorso relativo che sale di un livello ("..") è un fratello della cartella dell'installer, non un figlio.
        var relative = Path.GetRelativePath(AppPaths.InstallerRoot, AppPaths.DefaultRoot);

        Assert.StartsWith("..", relative);
    }

    [Fact]
    public void Temp_files_of_interrupted_jobs_are_removed_at_startup()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(Path.Combine(dir.Path, "dati"));
        paths.EnsureCreated();
        var leftover = Path.Combine(paths.Temp, "job.pcm");
        File.WriteAllText(leftover, "x");

        paths.CleanTemp();

        Assert.False(File.Exists(leftover));
    }

    [Theory]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(IOException))]
    public void Clean_temp_does_not_throw_when_a_file_cannot_be_deleted(Type exceptionType)
    {
        using var dir = new TempDir();
        var paths = new AppPaths(Path.Combine(dir.Path, "dati"));
        paths.EnsureCreated();
        File.WriteAllText(Path.Combine(paths.Temp, "bloccato.pcm"), "x");
        File.WriteAllText(Path.Combine(paths.Temp, "libero.pcm"), "x");
        var attempts = 0;

        paths.CleanTemp(_ =>
        {
            attempts++;
            throw (Exception)Activator.CreateInstance(exceptionType)!;
        });

        // Un file non eliminabile non ferma la pulizia degli altri.
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Clean_temp_does_not_throw_with_a_read_only_file()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(Path.Combine(dir.Path, "dati"));
        paths.EnsureCreated();
        var file = Path.Combine(paths.Temp, "sola-lettura.pcm");
        File.WriteAllText(file, "x");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var ex = Record.Exception(paths.CleanTemp);

            Assert.Null(ex);
        }
        finally
        {
            if (File.Exists(file))
                File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Unreachable_models_directory_falls_back_to_the_default_one()
    {
        using var dir = new TempDir();
        var blocker = Path.Combine(dir.Path, "file.txt");
        File.WriteAllText(blocker, "x");
        var root = Path.Combine(dir.Path, "dati");
        var paths = new AppPaths(root, Path.Combine(blocker, "models"));

        var ex = Record.Exception(paths.EnsureCreated);

        Assert.Null(ex);
        Assert.True(paths.ModelsFallbackUsed);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "models"), paths.Models);
        Assert.True(Directory.Exists(paths.PartialModels));
    }

    [Fact]
    public void Reachable_models_directory_is_kept()
    {
        using var dir = new TempDir();
        var custom = Path.Combine(dir.Path, "miei-modelli");
        var paths = new AppPaths(Path.Combine(dir.Path, "dati"), custom);

        paths.EnsureCreated();

        Assert.False(paths.ModelsFallbackUsed);
        Assert.Equal(Path.GetFullPath(custom), paths.Models);
    }
}
