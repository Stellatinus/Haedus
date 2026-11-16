using Microsoft.Extensions.Logging;

namespace Haedus.Core;

/// <summary>
/// Directory dati per utente (ADR-0001): %LOCALAPPDATA%\Haedus.Data con models, temp, logs e settings.json.
/// Sta fuori da %LOCALAPPDATA%\Haedus, la cartella gestita da Velopack: aggiornare o disinstallare l'app
/// ricrea quella cartella e cancellerebbe i dati.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string root, string? modelsDirectory = null)
    {
        Root = Path.GetFullPath(root);
        Models = Path.GetFullPath(modelsDirectory ?? DefaultModels);
        Temp = Path.Combine(Root, "temp");
        Logs = Path.Combine(Root, "logs");
        SettingsFile = Path.Combine(Root, "settings.json");
    }

    public string Root { get; }

    /// <summary>Directory dei modelli in uso: quella scelta nelle impostazioni o, se irraggiungibile, Root\models.</summary>
    public string Models { get; private set; }

    /// <summary>True se la directory dei modelli scelta non era creabile e si è ripiegato su Root\models (issue #92).</summary>
    public bool ModelsFallbackUsed { get; private set; }

    private string DefaultModels => Path.Combine(Root, "models");

    public string PartialModels => Path.Combine(Models, ".partial");

    public string Temp { get; }

    public string Logs { get; }

    public string SettingsFile { get; }

    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haedus.Data");

    /// <summary>Cartella di installazione gestita dall'installer (packId Haedus): i dati non devono stare lì.</summary>
    public static string InstallerRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haedus");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        try
        {
            Directory.CreateDirectory(PartialModels);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // Disco USB o di rete scollegato, percorso non valido o non scrivibile: l'app deve comunque aprirsi.
            if (string.Equals(Models, Path.GetFullPath(DefaultModels), StringComparison.Ordinal))
                throw;
            Models = Path.GetFullPath(DefaultModels);
            ModelsFallbackUsed = true;
            Directory.CreateDirectory(PartialModels);
        }

        Directory.CreateDirectory(Temp);
        Directory.CreateDirectory(Logs);
    }

    /// <summary>Rimuove i file temporanei lasciati da job interrotti (crash o chiusura forzata).</summary>
    public void CleanTemp() => CleanTemp(File.Delete);

    internal void CleanTemp(Action<string> delete)
    {
        if (!Directory.Exists(Temp))
            return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(Temp))
            {
                try
                {
                    delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // File in uso, bloccato dall'antivirus o di sola lettura: verrà rimosso al prossimo avvio.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cartella temp non leggibile: la pulizia è solo un'ottimizzazione, l'avvio non deve fallire.
        }
    }
}

/// <summary>Avvisi di avvio legati alle cartelle dati.</summary>
public static partial class AppPathsLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "La cartella dei modelli scelta non è raggiungibile: si usa quella predefinita")]
    public static partial void LogModelsFallback(this ILogger logger);
}
