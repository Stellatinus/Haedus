using System.Text.Json;
using Haedus.Core.Hardware;
using Haedus.Core.Models;

namespace Haedus.Core.Benchmark;

/// <summary>
/// Misure di velocità in benchmark.json nella cartella dati, accanto a settings.json: sono dati misurati, non preferenze.
/// Non escono mai dal PC (ADR-0006). Le misure si invalidano da sole se cambia l'hardware, il modello o il runtime.
/// </summary>
public sealed class BenchmarkStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly Lock _gate = new();

    public BenchmarkStore(string path) => _path = path;

    /// <summary>Impronta dell'hardware: se cambia (nuovo PC, nuova scheda, GPU attivata o no) le misure vecchie non valgono.</summary>
    public static string Fingerprint(HardwareProfile hardware) =>
        string.Join('|',
            hardware.Architecture,
            hardware.LogicalCores,
            (int)hardware.CpuFeatures,
            hardware.TotalMemoryBytes / (1L << 30),
            hardware.GpuAcceleration ? "gpu" : "cpu",
            string.Join(',', (hardware.GpuDetails?.Select(g => g.Name) ?? hardware.Gpus ?? []).Order(StringComparer.Ordinal)));

    /// <summary>Identità del file del modello: l'hash fissato dal catalogo, altrimenti nome e dimensione.</summary>
    public static string ModelIdentity(ModelDescriptor model) => model.Sha256 ?? $"{model.File}:{model.SizeBytes}";

    public BenchmarkFile Read()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                    return BenchmarkFile.Empty;
                var file = JsonSerializer.Deserialize<BenchmarkFile>(File.ReadAllText(_path), Options);
                return file is { Version: BenchmarkFile.CurrentVersion } ? file : BenchmarkFile.Empty;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // File illeggibile: le misure si rifanno, nessun errore per l'utente.
                return BenchmarkFile.Empty;
            }
        }
    }

    /// <summary>
    /// Salva una misura. Se l'impronta dell'hardware è diversa da quella del file le misure precedenti vengono scartate;
    /// per lo stesso modello, runtime e brano la nuova misura sostituisce la vecchia.
    /// </summary>
    public void Record(string fingerprint, BenchmarkEntry entry)
    {
        lock (_gate)
        {
            var current = Read();
            var kept = current.Fingerprint == fingerprint
                ? current.Entries.Where(e => !(e.ModelId == entry.ModelId && e.Runtime == entry.Runtime && e.ClipId == entry.ClipId)).ToList()
                : [];
            kept.Add(entry);
            Write(new BenchmarkFile(BenchmarkFile.CurrentVersion, fingerprint, kept));
        }
    }

    /// <summary>Cancella tutte le misure: un nuovo test sostituisce quelli precedenti, anche di altri runtime o versioni.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
    }

    /// <summary>Toglie le misure che soddisfano la condizione (anche quelle non più valide) e lascia le altre: serve a rifare solo certe prove.</summary>
    public void RemoveWhere(Func<BenchmarkEntry, bool> predicate)
    {
        lock (_gate)
        {
            var current = Read();
            if (!current.Entries.Any(predicate))
                return;
            Write(current with { Entries = current.Entries.Where(e => !predicate(e)).ToList() });
        }
    }

    /// <summary>
    /// Porta al formato attuale le misure salvate con la versione precedente (fino alla 3.1.0), che valevano per l'insieme dei
    /// brani di allora: sono quelli di oggi, quindi restano valide e da qui in poi ogni misura vale per il suo brano.
    /// Da chiamare all'avvio, prima che l'elenco dei brani cambi.
    /// </summary>
    public void MigrateLegacy()
    {
        lock (_gate)
        {
            var current = Read();
            if (!current.Entries.Any(e => e.BenchmarkVersion == BenchmarkVersion.Legacy))
                return;
            var upgraded = current.Entries.Select(e =>
            {
                if (e.BenchmarkVersion != BenchmarkVersion.Legacy)
                    return e;
                var clip = e.ClipId is null ? null : BenchmarkClips.Find(e.ClipId);
                return e with { BenchmarkVersion = BenchmarkVersion.Current, ClipFingerprint = clip is null ? null : BenchmarkVersion.ClipFingerprint(clip) };
            }).ToList();
            Write(current with { Entries = upgraded });
        }
    }

    /// <summary>Tutte le misure con il loro stato rispetto all'hardware, ai modelli installati e alla versione dell'app.</summary>
    public IReadOnlyList<BenchmarkView> Views(string fingerprint, Func<string, string?> identityOf, string benchmarkVersion)
    {
        var file = Read();
        return file.Entries.Select(e => new BenchmarkView(e, Classify(file, fingerprint, identityOf, benchmarkVersion, e))).ToList();
    }

    /// <summary>
    /// La misura valida per un modello, se c'è: con più brani o più runtime vale il runtime della misura più recente e i numeri
    /// sono combinati sui suoi brani (<see cref="Combine"/>).
    /// </summary>
    public BenchmarkEntry? Lookup(string fingerprint, string modelId, Func<string, string?> identityOf, string benchmarkVersion) =>
        Combine(Views(fingerprint, identityOf, benchmarkVersion).Where(v => v.IsValid && v.Entry.ModelId == modelId).Select(v => v.Entry).ToList());

    /// <summary>
    /// Riassume le misure di un modello in una sola: stesso runtime della più recente, velocità sul totale dell'audio e tempo
    /// fisso medio. L'affidabilità non entra qui (dipende dalla lingua, vedi <see cref="BenchmarkReliability"/>).
    /// </summary>
    public static BenchmarkEntry? Combine(IReadOnlyList<BenchmarkEntry> entries)
    {
        if (entries.Count == 0)
            return null;
        var newest = entries.OrderByDescending(e => e.MeasuredAt).First();
        var same = entries.Where(e => e.Runtime == newest.Runtime && e.Speed > 0).ToList();
        if (same.Count == 0)
            return newest;
        var audio = same.Sum(e => e.AudioSeconds);
        var work = same.Sum(e => e.AudioSeconds / e.Speed);
        return newest with
        {
            AudioSeconds = audio,
            LoadSeconds = same.Average(e => e.LoadSeconds),
            Speed = work > 0 ? audio / work : newest.Speed,
            Accuracy = null,
            ClipId = null,
            Language = null,
            Scenario = null,
        };
    }

    private static BenchmarkStaleReason Classify(BenchmarkFile file, string fingerprint, Func<string, string?> identityOf, string benchmarkVersion, BenchmarkEntry entry)
    {
        if (file.Fingerprint != fingerprint)
            return BenchmarkStaleReason.HardwareChanged;
        if (identityOf(entry.ModelId) != entry.ModelIdentity)
            return BenchmarkStaleReason.ModelChanged;
        if (entry.BenchmarkVersion != benchmarkVersion)
            return BenchmarkStaleReason.TestChanged;
        // Una misura su un brano ufficiale vale finché quel brano è lo stesso (file, testo, rumore).
        if (entry.ClipId is not null && (BenchmarkClips.Find(entry.ClipId) is not { } clip || entry.ClipFingerprint != BenchmarkVersion.ClipFingerprint(clip)))
            return BenchmarkStaleReason.TestChanged;
        return BenchmarkStaleReason.None;
    }

    private void Write(BenchmarkFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Options));
        File.Move(temp, _path, overwrite: true);
    }
}
