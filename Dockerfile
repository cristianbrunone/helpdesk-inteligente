# syntax=docker/dockerfile:1
# Imagens do backend (ADD §12): um Dockerfile com três alvos (api, worker e migrator) que compartilham
# o mesmo restore e a mesma compilação. O docker-compose escolhe o alvo com "target".

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0.401-alpine3.23 AS build
WORKDIR /src

# CA extra opcional, para redes com inspeção TLS (proxy corporativo). Sem CA_EXTRA_PEM, o segredo é um
# arquivo vazio e nada muda. Vale só nesta etapa de build: as imagens finais não herdam o certificado.
# A imagem Alpine do SDK só traz o bundle (sem update-ca-certificates): a CA é anexada direto a ele.
RUN --mount=type=secret,id=ca_extra,target=/run/secrets/ca_extra \
    if [ -s /run/secrets/ca_extra ]; then \
      cat /run/secrets/ca_extra >> /etc/ssl/certs/ca-certificates.crt; \
    fi

# Restore numa camada própria: só é refeito quando um .csproj ou uma versão de pacote muda.
# O .editorconfig entra porque marca as migrations como código gerado (sem ele o build falha).
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Directory.Build.props src/
COPY src/HelpDesk.Domain/HelpDesk.Domain.csproj src/HelpDesk.Domain/
COPY src/HelpDesk.Application/HelpDesk.Application.csproj src/HelpDesk.Application/
COPY src/HelpDesk.Infrastructure/HelpDesk.Infrastructure.csproj src/HelpDesk.Infrastructure/
COPY src/HelpDesk.Api/HelpDesk.Api.csproj src/HelpDesk.Api/
COPY src/HelpDesk.Worker/HelpDesk.Worker.csproj src/HelpDesk.Worker/
COPY src/HelpDesk.Migrator/HelpDesk.Migrator.csproj src/HelpDesk.Migrator/
RUN dotnet restore src/HelpDesk.Api/HelpDesk.Api.csproj \
 && dotnet restore src/HelpDesk.Worker/HelpDesk.Worker.csproj \
 && dotnet restore src/HelpDesk.Migrator/HelpDesk.Migrator.csproj

COPY src/ src/
# Prompts versionados: a Infrastructure os copia para a saída da API e do Worker (lidos em tempo de execução).
COPY prompts/ prompts/
RUN dotnet publish src/HelpDesk.Api/HelpDesk.Api.csproj -c Release --no-restore -o /out/api -p:UseAppHost=false \
 && dotnet publish src/HelpDesk.Worker/HelpDesk.Worker.csproj -c Release --no-restore -o /out/worker -p:UseAppHost=false \
 && dotnet publish src/HelpDesk.Migrator/HelpDesk.Migrator.csproj -c Release --no-restore -o /out/migrator -p:UseAppHost=false

# ---------- API ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-alpine3.23 AS api
WORKDIR /app
COPY --from=build /out/api .
# Usuário não-root das imagens oficiais do .NET; a API escuta na 8080 (ASPNETCORE_HTTP_PORTS).
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HelpDesk.Api.dll"]

# ---------- Worker ----------
FROM mcr.microsoft.com/dotnet/runtime:10.0.12-alpine3.23 AS worker
WORKDIR /app
COPY --from=build /out/worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "HelpDesk.Worker.dll"]

# ---------- Migrator (one-shot, ADR-0015) ----------
FROM mcr.microsoft.com/dotnet/runtime:10.0.12-alpine3.23 AS migrator
WORKDIR /app
COPY --from=build /out/migrator .
USER $APP_UID
ENTRYPOINT ["dotnet", "HelpDesk.Migrator.dll"]
