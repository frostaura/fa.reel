# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/backend/ ./

RUN dotnet restore FrostAura.Reel.Api/FrostAura.Reel.Api.csproj
RUN dotnet publish FrostAura.Reel.Api/FrostAura.Reel.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# curl is needed by the compose healthcheck (the aspnet image ships without it).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish ./

# The MiniLM embedding ONNX model is fetched into /app/models at M2 (kept out of git and
# out of early images; see docs/architecture once the embedding subsystem lands).

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "FrostAura.Reel.Api.dll"]
