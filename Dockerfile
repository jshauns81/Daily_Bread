# Build stage - Use .NET 10 preview SDK
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0-preview AS build
WORKDIR /src

# Copy project file and restore dependencies first (for better caching)
COPY Daily_Bread/Daily_Bread.csproj Daily_Bread/
RUN dotnet restore Daily_Bread/Daily_Bread.csproj

# Copy everything else and build
COPY . .
WORKDIR /src/Daily_Bread
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage - Use .NET 10 preview runtime
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0-preview AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Create non-root user for security
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

# Railway uses PORT env var, default to 8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8080
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:${PORT}/health || exit 1

# Use shell form to expand PORT variable
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT} dotnet Daily_Bread.dll"]
