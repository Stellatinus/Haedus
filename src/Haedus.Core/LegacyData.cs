using Haedus.Core.Settings;

namespace Haedus.Core;

/// <summary>
/// Il progetto si chiamava CodexVTT: chi aveva già dati (modelli, impostazioni, cronologia) li ritrova dopo il cambio di nome.
/// Le cartelle del vecchio nome vengono spostate (stesso volume: un'operazione sola) solo se quelle nuove non esistono ancora.
/// </summary>
public static class LegacyData
{
    public const string OldDataFolder = "CodexVTT.Data";
    public const string OldDocumentsFolder = "CodexVTT";

    /// <summary>Formati dei file scritti prima del cambio di nome: restano leggibili.</summary>
    public const string OldLibraryFormat = "codexvtt-library";

    public const string OldTranscriptFormat = "codexvtt-transcript";

    /// <summary>Sposta i dati del vecchio nome, se ci sono; non lancia mai: in caso di errore si riparte dalle cartelle nuove.</summary>
    public static void Migrate(string? localAppData = null, string? documents = null)
    {
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        documents ??= Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        var newRoot = Path.Combine(localAppData, "Haedus.Data");
        TryMove(Path.Combine(localAppData, OldDataFolder), newRoot);

        // La cronologia si sposta solo se è quella predefinita: una cartella scelta dall'utente non si tocca.
        var customHistory = false;
        try
        {
            customHistory = !string.IsNullOrWhiteSpace(new SettingsStore(Path.Combine(newRoot, "settings.json")).Load().HistoryDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // Impostazioni illeggibili: si tratta come predefinita.
        }

        if (!customHistory)
            TryMove(Path.Combine(documents, OldDocumentsFolder), Path.Combine(documents, "Haedus"));
    }

    private static void TryMove(string from, string to)
    {
        try
        {
            if (Directory.Exists(from) && !Directory.Exists(to))
                Directory.Move(from, to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cartella in uso o senza permessi: i dati restano dove sono.
        }
    }
}
