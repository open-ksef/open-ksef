FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

WORKDIR "/src/OpenKSeF.Worker"
RUN --mount=type=secret,id=nuget_cirfmf_pat,required=true \
    NuGetPackageSourceCredentials_CIRFMF="Username=openksef;Password=$(cat /run/secrets/nuget_cirfmf_pat)" \
    dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OpenKSeF.Worker.dll"]
