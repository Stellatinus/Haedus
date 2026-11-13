namespace Haedus.Server;

/// <summary>Impostazioni della versione server (sezione "Haedus" di appsettings.json, variabili Haedus__* o argomenti --Haedus:*).</summary>
public sealed class ServerOptions
{
    public const string Section = "Haedus";

    /// <summary>Cartella dati (modelli, log, settings.json). Vuota = %LOCALAPPDATA%\Haedus.Data dell'account del processo.</summary>
    public string? DataRoot { get; set; }

    /// <summary>Cartella predefinita della cronologia. Vuota = Documenti\Haedus\history dell'account del processo.</summary>
    public string? HistoryDirectory { get; set; }

    /// <summary>
    /// Token d'accesso. Senza token il server risponde solo a richieste dallo stesso PC (localhost);
    /// con il token si apre <c>http://indirizzo/?token=…</c> una volta e il browser lo ricorda in un cookie.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>Dimensione massima di un caricamento (tutti i file insieme).</summary>
    public long MaxUploadBytes { get; set; } = 4L * 1024 * 1024 * 1024;
}
