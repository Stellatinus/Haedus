using Microsoft.Extensions.Logging;
using Haedus.Audio.Windows;
using Haedus.Bridge;
using Haedus.Core;
using Haedus.Core.Audio;
using Haedus.Core.Hardware;
using Haedus.Core.History;
using Haedus.Core.Logging;
using Haedus.Core.Models;
using Haedus.Core.Settings;
using Haedus.Core.Transcription;
using Haedus.Models.Download;
using Haedus.Transcription.Sherpa;
using Haedus.Transcription.Whisper;

namespace Haedus.App;

public static class MauiProgram
{
    /// <summary>Durata massima dell'audio in V1 (specifica #2, REQ-012).</summary>
    public static readonly TimeSpan MaxAudioDuration = TimeSpan.FromHours(4);

    public static MauiApp CreateMauiApp()
    {
        LegacyData.Migrate();
        var settingsStore = new SettingsStore(Path.Combine(AppPaths.DefaultRoot, "settings.json"));
        var paths = new AppPaths(AppPaths.DefaultRoot, settingsStore.Load().ModelsDirectory);
        paths.EnsureCreated();
        paths.CleanTemp();

        // Livello del log: argomenti (--debug), poi HAEDUS_LOG_LEVEL / HAEDUS_DEBUG, poi settings.json (issue #38).
        var diagnostics = DiagnosticOptions.Resolve(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable, settingsStore.Load().DiagnosticLogLevel);
        var fileLogs = new FileLoggerProvider(paths.Logs, diagnostics.MinimumLevel);

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(fileLogs);
#if DEBUG
        builder.Logging.AddDebug();
#endif

        var services = builder.Services;
        services.AddSingleton(paths);
        services.AddSingleton(settingsStore);
        services.AddSingleton(diagnostics);
        services.AddSingleton(_ => ModelCatalog.LoadEmbedded());
        services.AddSingleton<ModelStore>();
        services.AddSingleton<IHardwareProbe, HardwareProbe>();

        // Unico punto di accesso alla rete (ADR-0006).
        services.AddSingleton(HostAllowlist.ModelHosts);
        services.AddSingleton<IModelDownloader>(sp => new ModelDownloader(
            ModelDownloader.CreateHttpClient(),
            sp.GetRequiredService<ModelStore>(),
            sp.GetRequiredService<HostAllowlist>(),
            sp.GetRequiredService<ILogger<ModelDownloader>>()));

        services.AddSingleton<Haedus.Core.Benchmark.IReferenceFileProvider>(sp => new ReferenceFileDownloader(
            ModelDownloader.CreateHttpClient(), Path.Combine(paths.Root, "benchmark")));
        services.AddSingleton(_ => new AudioLibrary([new WavDecoder(), new MediaFoundationDecoder()], MaxAudioDuration));
        // Motori (ADR-0009): Whisper (con GPU se possibile) e sherpa-onnx per Parakeet, Canary e Cohere.
        var sherpa = new SherpaEngine();
        services.AddSingleton<ITranscriptionEngine>(_ => new EngineRegistry(new Dictionary<string, ITranscriptionEngine>
        {
            ["whisper"] = new WhisperEngine(settingsStore.Load().UseGpu),
            [SherpaEngine.Transducer] = sherpa,
            [SherpaEngine.Canary] = sherpa,
            [SherpaEngine.Cohere] = sherpa,
        }));
        services.AddSingleton(sp => new TranscriptionService(
            sp.GetRequiredService<ModelStore>(),
            sp.GetRequiredService<AudioLibrary>(),
            sp.GetRequiredService<ITranscriptionEngine>(),
            sp.GetRequiredService<ILogger<TranscriptionService>>()));

        // Cronologia in una cartella visibile: Documenti\Haedus\history, modificabile nelle impostazioni.
        services.AddSingleton(new HistoryLocation(TranscriptHistory.DefaultDirectory, Path.Combine(AppPaths.DefaultRoot, "history-recovery")));

        services.AddSingleton<WindowsShellServices>();
        services.AddSingleton<IShellServices>(sp => sp.GetRequiredService<WindowsShellServices>());
        // Aggiornamenti da GitHub (ADR-0008): solo su richiesta o, se l'utente lo attiva, all'avvio.
        services.AddSingleton<IUpdateService>(sp => new Haedus.Updates.VelopackUpdateService(
            ProjectInfo.RepositoryUrl, sp.GetRequiredService<ILogger<Haedus.Updates.VelopackUpdateService>>()));
        services.AddSingleton<BridgeDispatcher>();
        services.AddSingleton<MainPage>();

        WebViewSecurity.Configure(diagnostics, new LoggerFactory([fileLogs]).CreateLogger("Haedus.WebView"));
        return builder.Build();
    }
}
