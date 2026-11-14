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

    /// <summary>
    /// Nomi host (senza porta, qualunque porta) con cui si può raggiungere il server, oltre a localhost, 127.0.0.1 e [::1].
    /// Difende dal DNS rebinding: un sito ostile il cui nome punta a 127.0.0.1 manda un header Host diverso e viene rifiutato.
    /// Senza token è obbligatorio per usare un nome diverso da localhost (es. <c>Haedus__AllowedHosts__0=haedus.lan</c>).
    /// Con il token vuoto = qualunque host (il cookie d'accesso è legato al nome host, quindi un sito ostile non lo ha);
    /// se valorizzato, il controllo vale anche con il token.
    /// </summary>
    public string[] AllowedHosts { get; set; } = [];

    /// <summary>Dimensione massima di un caricamento (tutti i file insieme).</summary>
    public long MaxUploadBytes { get; set; } = 4L * 1024 * 1024 * 1024;
}
