FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["OpenKSeF.Portal/OpenKSeF.Portal.csproj", "OpenKSeF.Portal/"]
COPY ["OpenKSeF.Domain/OpenKSeF.Domain.csproj", "OpenKSeF.Domain/"]
RUN dotnet restore "OpenKSeF.Portal/OpenKSeF.Portal.csproj"

COPY . .
WORKDIR "/src/OpenKSeF.Portal"
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OpenKSeF.Portal.dll"]
