FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

WORKDIR "/src/OpenKSeF.Api"
RUN --mount=type=secret,id=nuget_cirfmf_pat,required=true \
    NuGetPackageSourceCredentials_CIRFMF="Username=openksef;Password=$(cat /run/secrets/nuget_cirfmf_pat)" \
    dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8081
ENV ASPNETCORE_URLS=http://+:8081
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OpenKSeF.Api.dll"]
