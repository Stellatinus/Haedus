using Microsoft.Extensions.Logging;
using Haedus.App.Shell;
using Haedus.Core.Audio;
using Haedus.Core.Logging;

namespace Haedus.App;

/// <summary>
/// Isolamento della WebView (ADR-0006): solo l'origine locale di HybridWebView, nessuna
/// navigazione o richiesta verso l'esterno, nessuna nuova finestra, DevTools disattivati in Release
/// (a meno di modalità debug esplicita, issue #38).
/// </summary>
public static class WebViewSecurity
{
    /// <summary>Origine locale con cui HybridWebView serve gli asset su Windows.</summary>
    public const string LocalOrigin = "https://0.0.0.1";

    public static bool IsLocal(string? uri) =>
        uri is not null &&
        (uri.StartsWith(LocalOrigin + "/", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri, LocalOrigin, StringComparison.OrdinalIgnoreCase) ||
         uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
         uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Serve <c>/audio/ID</c> (l'audio originale di una trascrizione) dall'origine locale: il file è risolto solo dal
    /// bridge a partire dall'ID in cronologia, mai da un percorso scelto dalla UI. Supporta Range (206) perché
    /// l'elemento audio di Chromium cerca con richieste a intervalli. Tutto il resto resta com'è (ADR-0006).
    /// </summary>
    /// <returns>null se l'indirizzo non è <c>/audio/ID</c> dell'origine locale: la richiesta prosegue come prima.</returns>
    public static AudioResponse? TryServeAudio(Uri uri, string method, string? rangeHeader, Func<string, (string Path, string ContentType)?> open)
    {
        if (!IsLocal(uri.GetLeftPart(UriPartial.Authority) + "/") || !uri.AbsolutePath.StartsWith("/audio/", StringComparison.Ordinal))
            return null;

        var id = Uri.UnescapeDataString(uri.AbsolutePath["/audio/".Length..]);
        return id.Length == 0 || id.Contains('/', StringComparison.Ordinal) || open(id) is not { } audio
            ? AudioResponse.NotFound
            : AudioResponse.Create(audio.Path, audio.ContentType, method, rangeHeader);
    }

    /// <param name="options">Opzioni diagnostiche: con <see cref="DiagnosticOptions.DebugMode"/> attivo si collega il log della WebView.</param>
    /// <param name="logger">Logger della WebView; usato solo in modalità debug.</param>
    public static void Configure(DiagnosticOptions options, ILogger logger)
    {
#if WINDOWS
        var diagnostics = options.DebugMode ? new WebViewDiagnostics(logger) : null;

        Microsoft.Maui.Handlers.HybridWebViewHandler.Mapper.AppendToMapping("HaedusSecurity", (handler, mappedView) =>
        {
            // L'evento di HybridWebView passa prima di quello che serve gli asset: se la richiesta è gestita qui,
            // HybridWebView non risponde con il suo 404.
            if (mappedView is Microsoft.Maui.Controls.HybridWebView hybrid && handler.MauiContext?.Services.GetService(typeof(Haedus.Bridge.BridgeDispatcher)) is Haedus.Bridge.BridgeDispatcher dispatcher)
            {
                hybrid.WebResourceRequested += (_, e) =>
                {
                    // Un'eccezione che esce da questo evento chiude l'intera app (issue #59): ogni errore diventa una risposta HTTP.
                    try
                    {
                        e.Headers.TryGetValue("Range", out var range);
                        if (TryServeAudio(e.Uri, e.Method, range, dispatcher.OpenHistoryAudio) is not { } response)
                            return;
                        e.Handled = true;
                        e.SetResponse(response.Status, response.Reason, response.Headers, response.Body ?? Stream.Null);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Risposta audio non riuscita");
                        if (IsLocal(e.Uri?.GetLeftPart(UriPartial.Authority) + "/") && e.Uri!.AbsolutePath.StartsWith("/audio/", StringComparison.Ordinal))
                        {
                            e.Handled = true;
                            e.SetResponse(500, "Internal Server Error", new Dictionary<string, string>(), Stream.Null);
                        }
                    }
                };
            }

            var webView = handler.PlatformView;
            webView.CoreWebView2Initialized += (sender, initArgs) =>
            {
                var core = sender.CoreWebView2;
                if (core is null)
                    return;
#if !DEBUG
                // DevTools solo con modalità debug esplicita (variabile d'ambiente o argomento --debug).
                core.Settings.AreDevToolsEnabled = options.DebugMode;
                core.Settings.AreDefaultContextMenusEnabled = false;
#endif
                // Nessun controllo di reputazione verso SmartScreen: l'app naviga solo sull'origine locale (IsLocal).
                core.Settings.IsReputationCheckingRequired = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;

                core.NavigationStarting += (_, e) =>
                {
                    if (!IsLocal(e.Uri))
                        e.Cancel = true;
                };
                core.NewWindowRequested += (_, e) => e.Handled = true;

                // Qualunque richiesta non locale (script, immagini, fetch) viene bloccata.
                core.AddWebResourceRequestedFilter("*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (_, e) =>
                {
                    if (!IsLocal(e.Request.Uri))
                    {
                        diagnostics?.LogBlockedRequest(e.Request.Uri);
                        e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked by Haedus", "");
                    }
                };

                if (diagnostics is not null)
                    _ = diagnostics.AttachAsync(core);
            };
        });
#endif
    }
}
