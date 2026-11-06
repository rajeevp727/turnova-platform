# Build from repository root: docker build -f infrastructure/render/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/backend/TurnOva/TurnOva.API/TurnOva.API.csproj
RUN dotnet publish src/backend/TurnOva/TurnOva.API/TurnOva.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TurnOva.API.dll"]
