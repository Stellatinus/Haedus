using System.Globalization;
using Haedus.Core.Settings;

namespace Haedus.App;

/// <summary>Testi nativi (dialog di sistema) in italiano o inglese, come la UI.</summary>
public static class NativeText
{
    private static readonly Dictionary<string, (string It, string En)> Texts = new()
    {
        ["pickAudio"] = ("Scegli un file audio", "Choose an audio file"),
        ["pickModel"] = ("Scegli un file modello Whisper (ggml)", "Choose a Whisper model file (ggml)"),
        ["alreadyRunning"] = ("Haedus è già aperto.", "Haedus is already running."),
        ["uninstallTitle"] = ("Disinstalla Haedus", "Uninstall Haedus"),
        ["uninstallStep1"] = (
            "Vuoi eliminare anche i dati interni di Haedus?\n\nVerranno rimossi i modelli Whisper scaricati ({size}), i log, le impostazioni e i file temporanei in %LOCALAPPDATA%\\Haedus.Data.\n\nLa cronologia in Documenti\\Haedus non viene toccata.",
            "Do you want to delete Haedus's internal data too?\n\nThe downloaded Whisper models ({size}), logs, settings and temporary files in %LOCALAPPDATA%\\Haedus.Data will be removed.\n\nThe history in Documents\\Haedus is not touched."),
        ["uninstallStep2"] = (
            "Conferma: i dati interni verranno eliminati definitivamente e non potranno essere recuperati. Continuare?",
            "Confirm: the internal data will be permanently deleted and cannot be recovered. Continue?"),
        ["webView2Missing"] = (
            "Per avviare Haedus serve Microsoft Edge WebView2 Runtime, incluso in Windows 11. Installalo da Windows Update o dal sito Microsoft e riavvia Haedus.",
            "Haedus needs the Microsoft Edge WebView2 Runtime, included in Windows 11. Install it from Windows Update or the Microsoft website and restart Haedus."),
    };

    /// <summary>Lingua scelta nelle impostazioni, altrimenti quella di sistema (italiano se è italiano, altrimenti inglese).</summary>
    public static bool IsEnglish(AppSettings? settings)
    {
        var language = settings?.UiLanguage ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return !string.Equals(language, "it", StringComparison.OrdinalIgnoreCase);
    }

    public static string Get(string key, AppSettings? settings = null)
    {
        var (it, en) = Texts[key];
        return IsEnglish(settings) ? en : it;
    }
}
