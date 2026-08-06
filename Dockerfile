# Build stage - Use .NET 10 SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies first (for better caching)
COPY Daily_Bread/Daily_Bread.csproj Daily_Bread/
RUN dotnet restore Daily_Bread/Daily_Bread.csproj

# Copy everything else and build.
#
# NO --no-restore here, and that is load-bearing (prod web outage,
# 2026-08-06): the .NET 10 SDK decides at RESTORE time whether a project
# ships the ASP.NET framework static assets (blazor.web.js moved out of
# the embedded endpoint and into the static-assets pipeline in 10). The
# csproj-only restore above runs in a tree with no source, classifies the
# project wrong, and a --no-restore publish freezes that verdict: the
# image comes out with no wwwroot/_framework, @Assets falls back to the
# bare path, the request falls through to auth, and the web app is a
# styled black page. The early restore stays purely as a package-cache
# layer; this restore-during-publish recomputes with the source present.
COPY . .
WORKDIR /src/Daily_Bread
RUN dotnet publish -c Release -o /app/publish

# Runtime stage - Use .NET 10 runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy published app   
COPY --from=build /app/publish .

# Configure environment
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8080
EXPOSE 8080

# Use PORT environment variable for flexibility with different hosting providers
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT} dotnet Daily_Bread.dll"]
