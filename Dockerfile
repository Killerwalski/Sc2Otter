FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy solution and project files
COPY *.sln .
COPY src/Sc2Otter.Server/*.csproj ./src/Sc2Otter.Server/
COPY src/Sc2Otter.Core/*.csproj ./src/Sc2Otter.Core/
COPY src/Sc2Otter.Data/*.csproj ./src/Sc2Otter.Data/
COPY src/Sc2Otter.LocalClient/*.csproj ./src/Sc2Otter.LocalClient/
COPY tests/Sc2Otter.Tests/*.csproj ./tests/Sc2Otter.Tests/

RUN dotnet restore

# Copy all source code
COPY . .

# Publish Server project
WORKDIR /source/src/Sc2Otter.Server
RUN dotnet publish -c Release -o /app

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Sc2Otter.Server.dll"]
