# ============================================================================
# Multi-stage build — FidelidadeTransacao.API
# Stage 1: build + publish
# Stage 2: runtime mínimo (aspnet:8.0)
# ============================================================================

# ── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia apenas os .csproj primeiro — aproveita cache de layers do Docker
# Se nenhum .csproj mudar, o restore não roda novamente
COPY ["src/FidelidadeTransacao.API/FidelidadeTransacao.API.csproj",             "src/FidelidadeTransacao.API/"]
COPY ["src/FidelidadeTransacao.Application/FidelidadeTransacao.Application.csproj", "src/FidelidadeTransacao.Application/"]
COPY ["src/FidelidadeTransacao.Infrastructure/FidelidadeTransacao.Infrastructure.csproj", "src/FidelidadeTransacao.Infrastructure/"]
COPY ["src/FidelidadeTransacao.Domain/FidelidadeTransacao.Domain.csproj",       "src/FidelidadeTransacao.Domain/"]

RUN dotnet restore "src/FidelidadeTransacao.API/FidelidadeTransacao.API.csproj"

# Copia o restante do código-fonte
COPY src/ src/

# Publica em modo Release — sem símbolos de debug, binários otimizados
RUN dotnet publish "src/FidelidadeTransacao.API/FidelidadeTransacao.API.csproj" \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Usuário não-root — OWASP: Least Privilege
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

# Copia apenas os binários publicados (sem SDK, sem código-fonte)
COPY --from=build /app/publish .

# Porta padrão do ASP.NET Core
EXPOSE 8080

# ASPNETCORE_URLS garante que a app escuta na porta correta dentro do container
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "FidelidadeTransacao.API.dll"]
