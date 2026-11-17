using System.Security.Cryptography;
using System.Text;

namespace Haedus.Core.Benchmark;

/// <summary>
/// Versione del Test bench: una misura salvata vale finché questa versione non cambia. Non segue la versione dell'app, così un
/// aggiornamento qualsiasi non obbliga a rifare il test. Riguarda il modo di misurare (revisione, rumore): aggiungere un
/// brano NON la cambia, così le misure già fatte restano valide e si provano solo i brani nuovi. Ogni misura porta anche
/// l'impronta del suo brano (<see cref="ClipFingerprint"/>): se un brano cambia (file, testo, rumore) perdono valore
/// solo le misure di quel brano. <see cref="Revision"/> si alza a mano quando cambia qualcosa che cambia i numeri.
/// </summary>
public static class BenchmarkVersion
{
    /// <summary>
    /// Da alzare di 1 quando cambia il modo di misurare: calcolo dell'affidabilità (<see cref="Evaluation.WordErrorRate"/>) o della velocità,
    /// generazione del rumore, preparazione dell'audio, motore di trascrizione o sua versione. Non va alzata per modifiche
    /// all'interfaccia, ai testi o ad altre parti dell'app che non incidono sulle misure.
    /// </summary>
    public const int Revision = 1;

    /// <summary>
    /// Versioni dei motori di trascrizione (Whisper.net e sherpa-onnx) di cui la <see cref="Revision"/> attuale tiene conto.
    /// Un test confronta questa riga con Directory.Packages.props: se aggiorni un motore il test fallisce, e va alzata la
    /// revisione (le velocità cambiano) e riscritta questa riga.
    /// </summary>
    public const string CoveredEngines = "Whisper.net=1.9.1;sherpa-onnx=2.0.1";

    /// <summary>Revisione e impronta del modo di misurare (non dei brani), per esempio <c>1-3fa9c2d1</c>.</summary>
    public static string Current { get; } = ComputeCurrent();

    /// <summary>
    /// Versione in uso fino alla 3.1.0, che comprendeva l'impronta di tutti i brani. Serve solo a riconoscere le misure
    /// salvate allora, fatte con i brani di oggi: <see cref="BenchmarkStore.MigrateLegacy"/> le porta al formato nuovo.
    /// </summary>
    public static string Legacy { get; } = ComputeLegacy();

    /// <summary>Impronta di un brano: file, brano di rumore e testo di riferimento (il rumore globale sta in <see cref="Current"/>).</summary>
    public static string ClipFingerprint(BenchmarkClip clip) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{clip.Id}:{clip.Sha256}:{clip.NoiseSourceId}:{clip.ReferenceText}")))[..12];

    private static string ComputeCurrent()
    {
        var text = $"{Revision}|{BenchmarkClips.NoiseSeed}|{BenchmarkClips.NoiseSnrDb}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("v2|" + text)));
        return $"{Revision}-{hash[..8]}";
    }

    private static string ComputeLegacy()
    {
        var builder = new StringBuilder();
        builder.Append(Revision).Append('|').Append(BenchmarkClips.NoiseSeed).Append('|').Append(BenchmarkClips.NoiseSnrDb);
        foreach (var clip in BenchmarkClips.All.OrderBy(c => c.Id, StringComparer.Ordinal))
            builder.Append('|').Append(clip.Id).Append(':').Append(clip.Sha256).Append(':').Append(clip.NoiseSourceId).Append(':').Append(clip.ReferenceText);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        return $"{Revision}-{hash[..8]}";
    }
}
