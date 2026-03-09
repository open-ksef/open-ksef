FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG NUGET_CIRFMF_USER
ARG NUGET_CIRFMF_PAT
WORKDIR /src

COPY . .

RUN if [ -n "$NUGET_CIRFMF_PAT" ]; then \
      dotnet nuget update source CIRFMF --source https://nuget.pkg.github.com/CIRFMF/index.json \
        --username "$NUGET_CIRFMF_USER" --password "$NUGET_CIRFMF_PAT" --store-password-in-clear-text \
        --configfile nuget.config; \
    fi

WORKDIR "/src/OpenKSeF.Worker"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OpenKSeF.Worker.dll"]
