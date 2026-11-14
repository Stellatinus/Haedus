namespace Haedus.Core.Maintenance;

/// <summary>
/// Rimozione dei dati interni di Haedus (modelli Whisper, log, impostazioni, file temporanei) durante la
/// disinstallazione. La cronologia in Documenti non viene mai toccata: è un file dell'utente.
/// </summary>
public static class InternalDataRemover
{
    /// <summary>
    /// Dimensione totale in byte dei file sotto la cartella, 0 se non esiste. Symlink e junction non vengono
    /// seguiti: i file del bersaglio sono fuori dal perimetro dei dati interni.
    /// </summary>
    public static long Measure(string root)
    {
        if (!Directory.Exists(root))
            return 0;
        return new DirectoryInfo(root)
            .EnumerateFiles("*", NoLinks)
            .Sum(f => f.Length);
    }

    /// <summary>
    /// Elimina la cartella e restituisce i percorsi che non è stato possibile rimuovere (file in uso, permessi).
    /// Symlink e junction vengono rimossi come semplici collegamenti, senza seguirli né toccare il bersaglio.
    /// Non lancia eccezioni: la disinstallazione deve completarsi comunque.
    /// </summary>
    public static IReadOnlyList<string> Remove(string root)
    {
        var failures = new List<string>();
        if (!Directory.Exists(root))
            return failures;

        if (IsLink(root))
        {
            RemoveLink(root, failures);
            return failures;
        }

        RemoveDirectory(root, failures);
        return failures;
    }

    private static readonly EnumerationOptions NoLinks = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void RemoveDirectory(string dir, List<string> failures)
    {
        // Visita a mano: ogni entry con ReparsePoint (symlink o junction) è un link, mai una cartella da attraversare.
        foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                RemoveLink(entry.FullName, failures);
            else if (entry is DirectoryInfo)
                RemoveDirectory(entry.FullName, failures);
            else
            {
                try
                {
                    File.SetAttributes(entry.FullName, FileAttributes.Normal);
                    File.Delete(entry.FullName);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add(entry.FullName);
                }
            }
        }

        // Le cartelle con file rimasti restano.
        try { Directory.Delete(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void RemoveLink(string path, List<string> failures)
    {
        try
        {
            // Un link a cartella (symlink o junction) si elimina con Directory.Delete non ricorsivo, che rimuove
            // solo il link; un link a file con File.Delete. Il bersaglio non viene mai aperto.
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: false);
            else
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(path);
        }
    }
}
