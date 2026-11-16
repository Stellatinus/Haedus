<p align="center">
  <img src="docs/assets/logo.png" width="160" alt="Logo di Haedus" />
</p>

# Haedus

Tool per voice-to-text basato su whisper (https://github.com/openai/whisper).

Haedus è un'app Windows che trascrive file audio con modelli Whisper installati ed eseguiti **localmente**: audio e testo non lasciano il PC.

## Struttura

| Percorso | Contenuto |
|---|---|
| `src/Haedus.Core` | Dominio: catalogo e selezione modelli, verifica, audio, trascrizione, export, impostazioni, log. Nessuna rete. |
| `src/Haedus.Transcription.Whisper` | Adattatore whisper.cpp (Whisper.net), runtime CPU + NoAvx |
| `src/Haedus.Transcription.Sherpa` | Parakeet, Canary e Cohere con sherpa-onnx ([ADR-0009](docs/adr/0009-motori-multipli-e-gpu.md)) |
| `src/Haedus.Audio.Windows` | Decodifica Media Foundation (Windows) |
| `src/Haedus.Audio.FFmpeg` | Decodifica con ffmpeg del sistema (Linux/Docker) |
| `src/Haedus.Models.Download` | Unico componente con accesso alla rete: download dei modelli con allowlist e SHA-256 |
| `src/Haedus.Bridge` | Contratto JSON versionato core ↔ UI e dispatcher dei comandi |
| `src/Haedus.App` | Shell .NET MAUI (Windows) con HybridWebView |
| `src/Haedus.Updates` | Aggiornamenti dell'app dalle release di GitHub ([ADR-0008](docs/adr/0008-aggiornamenti-da-github.md)) |
| `src/Haedus.Server` | Versione server: la stessa UI nel browser, in Docker o come servizio di Windows ([ADR-0007](docs/adr/0007-versione-server.md)) |
| `ui/` | Interfaccia React + TypeScript (Vite) |
| `tests/` | Test unitari, di architettura (isolamento di rete), server, aggiornamenti e integrazione Whisper |
| `scripts/` | `test-locale.ps1` (controlli), `release-locale.ps1` (release), `installa-servizio.ps1` (servizio di Windows) |
| `tools/pin_catalog.py` | Fissa revisione e SHA-256 dei modelli dal repository Hugging Face |
| `tools/Haedus.Bench` | Confronto tra modelli sullo stesso audio: tempo, velocità e WER |

## Sviluppo

```bash
# Core e test (Linux, macOS o Windows)
dotnet test tests/Haedus.Core.Tests
dotnet test tests/Haedus.Architecture.Tests

# Test con un modello reale (facoltativo)
HAEDUS_TEST_MODEL=/percorso/ggml-base.bin HAEDUS_TEST_AUDIO=/percorso/jfk.wav dotnet test tests/Haedus.Integration.Tests

# UI nel browser con il core simulato
cd ui && npm ci && npm run dev
npm test

# App Windows (richiede Windows e il workload MAUI)
cd ui && npm run build:app && cd ..
dotnet workload install maui-windows
dotnet build src/Haedus.App -c Debug
```

## Integrazione continua e rilascio (GitHub Actions)

| Workflow | Quando | Cosa fa |
|---|---|---|
| `ci.yml` | push su `main`, ogni PR | test .NET (Linux), UI (typecheck, test, build app/server/preview), build e test della soluzione su Windows con il workload MAUI, immagine Docker con prova di porta, token e `/healthz` |
| `release.yml` | a mano (Actions → Release → Run workflow, da `main`) | costruisce l'installer Velopack, i checksum e la release GitHub con le note di `docs/releases/<versione>.md`; poi verifica il feed di aggiornamento e pubblica l'immagine su GHCR. Con «dry_run» (predefinito) non pubblica nulla e lascia l'installer come artefatto |
| CodeQL (impostazione predefinita di GitHub) | push su `main`, PR | analisi statica C# e TypeScript, gestita da Settings → Code security |
| `dependency-audit.yml` | ogni lunedì, a mano | vulnerabilità note nelle dipendenze NuGet e npm |
| Dependabot | ogni settimana | aggiornamenti di NuGet, npm, GitHub Actions e immagini Docker |

Per rilasciare: alza `<Version>` in `Directory.Build.props` e `version` in `ui/package.json`, scrivi `docs/releases/<versione>.md`, mergia su `main` e lancia il workflow **Release**.

Gli stessi controlli si possono eseguire anche dal PC con un solo comando:

```powershell
.\scripts\test-locale.ps1                  # .NET, UI e build dell'app
.\scripts\test-locale.ps1 -Docker -Feed    # in più: immagine Docker con prova del token e feed di aggiornamento reale
```

## Versione server (UI nel browser)

Per le macchine dove la WebView non funziona (VM) o per usare Haedus da un altro dispositivo: il core gira come servizio e la UI si apre nel browser; le trascrizioni in coda continuano anche con la pagina chiusa.

**Con Docker (consigliato)** — Linux, funziona ovunque giri Docker, anche in una VM:

```bash
export HAEDUS_TOKEN="una-frase-lunga-e-segreta"     # PowerShell: $env:HAEDUS_TOKEN = "..."
docker compose up -d --build
# poi apri http://localhost:5185/?token=una-frase-lunga-e-segreta (una volta sola)
```

Il container include ffmpeg per MP3, M4A, FLAC, OGG… (il WAV si legge senza). Modelli, impostazioni, log e cronologia stanno nel volume `haedus-data` (`/data`) e restano agli aggiornamenti; un modello già scaricato si può copiare in `/data/models` (`docker cp ggml-small.bin <container>:/data/models/`) oppure scaricare dalla scheda Modelli. La porta è esposta solo su `127.0.0.1`: per la rete locale cambia in `5185:5185` in `docker-compose.yml`. Il token è obbligatorio, perché dentro il container le richieste non arrivano da localhost.

**Direttamente su Windows:**

```powershell
cd ui; npm ci; npm run build:server; cd ..
dotnet run --project src/Haedus.Server        # http://127.0.0.1:5185, solo da questo PC

# Come servizio di Windows, raggiungibile dalla rete locale (PowerShell come amministratore):
.\scripts\installa-servizio.ps1 -Urls "http://0.0.0.0:5185" -Token "una-frase-lunga-e-segreta"
# Rimozione: .\scripts\installa-servizio.ps1 -Disinstalla
```

Dalla rete serve il token: la prima volta apri `http://indirizzo:5185/?token=…`. Il server non fa HTTPS: fuori dalla LAN usa una VPN o un reverse proxy. Impostazioni in `appsettings.json` (sezione `Haedus`: `DataRoot`, `HistoryDirectory`, `AccessToken`, `AllowedHosts`, `MaxUploadBytes`), sovrascrivibili con variabili `Haedus__AccessToken=…`. L'indirizzo si imposta con `Urls` (variabile `Urls=http://0.0.0.0:5185`).

Protezione da siti ostili (DNS rebinding e CSRF): senza token il server accetta solo l'header Host `localhost`, `127.0.0.1` e `[::1]` (qualunque porta); per usare un altro nome da questo PC (es. `haedus.lan`) impostalo in `Haedus:AllowedHosts` (variabile `Haedus__AllowedHosts__0=haedus.lan`). Con il token qualunque nome host è accettato (il cookie d'accesso è legato al nome host), a meno che `AllowedHosts` non sia valorizzato: allora vale anche con il token. Le richieste che modificano dati e `/api/export` partite da un altro sito (Origin diverso o `Sec-Fetch-Site` cross-site) vengono rifiutate con 403; script e curl senza questi header continuano a funzionare.

## Rilascio

Dal PC Windows, su `main` aggiornato: `.\scripts\test-locale.ps1 -Docker -Feed`, poi `.\scripts\release-locale.ps1 -DryRun` (costruisce l'installer senza pubblicare) e infine `.\scripts\release-locale.ps1`. Lo script usa `Directory.Build.props` per la versione, `docs/releases/<versione>.md` per le note, scarica le release precedenti per creare i delta e pubblica con `gh release create` installer **e feed degli aggiornamenti**. Servono .NET SDK 10 con il workload `maui-windows`, Node 22, Visual Studio, Git e GitHub CLI autenticata.

## Aggiornamenti

L'app installata cerca le nuove versioni nelle release di GitHub del progetto ([ADR-0008](docs/adr/0008-aggiornamenti-da-github.md)): da **Impostazioni → Aggiornamenti** con «Controlla aggiornamenti», oppure da sola all'avvio se attivi la casella (spenta di default). Non viene inviato nessun dato personale. L'indirizzo del repository è in `src/Haedus.Core/ProjectInfo.cs`. Per provare gli aggiornamenti senza GitHub imposta `HAEDUS_UPDATE_SOURCE` con la cartella di un feed Velopack. Il server in Docker si aggiorna ricostruendo l'immagine (`git pull && docker compose up -d --build`).

## Versioni

Le versioni seguono [SemVer 2.0](https://semver.org/lang/it/) (`X.Y.Z[-fase.N]`), richiesto anche da Velopack. La versione si imposta in `Directory.Build.props` e il tag Git è `v` + versione (es. `v1.0.0-beta.1`).

| Parte | Quando cambia |
|---|---|
| **X** | Cambio di versione significativo. Diventa `1` quando le funzionalità di base sono complete. |
| **Y** | Avanzamento normale tra una versione e l'altra. |
| **Z** | Una correzione di bug per versione (0.2.1, 0.2.2, 0.2.3…), ciascuna su una branch dedicata. |
| **-alpha.N / -beta.N / -rc.N** | Pre-release; senza suffisso la versione è stabile. |

Velopack considera `1.0.1` più recente di `1.0.0`, quindi ogni correzione viene proposta come aggiornamento. Eventuali build di prova (interfacce a tema, funzionalità non ancora integrate) vanno pubblicate su un canale separato (`vpk pack --channel <nome>`).

## Licenza

Haedus è distribuito con licenza [Apache-2.0](LICENSE). Le licenze dei componenti di terzi e dei modelli, con le attribuzioni richieste (per esempio CC-BY-4.0 per i modelli NVIDIA), sono in [NOTICE](NOTICE) e in [docs/architecture/licenze.md](docs/architecture/licenze.md).

## Documentazione

- [Specifica di prodotto V1](docs/spec/v1-product-spec.md)
- [Guida utente](docs/guida-utente.md)
- [Decisioni architetturali (ADR)](docs/adr/README.md)
- [Perimetro V1 e non-obiettivi](docs/architecture/perimetro-v1.md)
- [Registro dei rischi](docs/architecture/rischi.md)
- [Matrice licenze](docs/architecture/licenze.md)
- [Codice di condotta](CODE_OF_CONDUCT.md)
- [Politica di sicurezza](SECURITY.md)
- [Spike runtime Whisper](spike/runtime-probe/README.md)
