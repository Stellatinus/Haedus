# Haedus Server (ADR-0007): la UI nel browser e il core in un container Linux.
# Build:  docker build -t haedus-server .
# Uso:    docker compose up -d   (vedi docker-compose.yml)

# 1) UI React per la versione server
FROM node:26-bookworm-slim AS ui
WORKDIR /src/ui
COPY ui/package.json ui/package-lock.json ./
RUN npm ci
COPY ui/ ./
# vite.config.ts importa solo da ui/src; l'output va in ../src/Haedus.Server/wwwroot
RUN npm run build:server

# 2) Server .NET
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY --from=ui /src/src/Haedus.Server/wwwroot src/Haedus.Server/wwwroot
RUN dotnet publish src/Haedus.Server/Haedus.Server.csproj -c Release -r linux-x64 --self-contained false -o /app

# 3) Immagine finale: runtime ASP.NET + ffmpeg (decodifica audio, ADR-0007)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg libgomp1 curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./

# Tutti i dati (modelli, impostazioni, log, cronologia) in un unico volume.
# "Urls" (e non ASPNETCORE_URLS) perché appsettings.json ha lo stesso nome e ha la precedenza sulle variabili ASPNETCORE_*.
ENV Urls=http://0.0.0.0:5185 \
    Haedus__DataRoot=/data \
    Haedus__HistoryDirectory=/data/history \
    DOTNET_gcServer=0
VOLUME /data
EXPOSE 5185

# Dall'esterno del container le richieste non arrivano da localhost: senza Haedus__AccessToken il server rifiuta tutto.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://127.0.0.1:5185/healthz || exit 1
ENTRYPOINT ["dotnet", "Haedus.Server.dll"]
