FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY TurnOva.sln ./
COPY TurnOva.API/TurnOva.API.csproj TurnOva.API/
COPY TurnOva.Application/TurnOva.Application.csproj TurnOva.Application/
COPY TurnOva.Domain/TurnOva.Domain.csproj TurnOva.Domain/
COPY TurnOva.Infrastructure/TurnOva.Infrastructure.csproj TurnOva.Infrastructure/
COPY TurnOva.Tests/TurnOva.Tests.csproj TurnOva.Tests/
RUN dotnet restore TurnOva.sln

COPY . .
RUN dotnet publish TurnOva.API/TurnOva.API.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TurnOva.API.dll"]
