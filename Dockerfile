# Build stage - Use .NET 9 SDK
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies first (for better caching)
COPY Daily_Bread/Daily_Bread.csproj Daily_Bread/
RUN dotnet restore Daily_Bread/Daily_Bread.csproj

# Copy everything else and build
COPY . .
WORKDIR /src/Daily_Bread
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage - Use .NET 9 runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copy published app   
COPY --from=build /app/publish .

# Railway uses PORT env var, default to 8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=8080
EXPOSE 8080

# Use shell form to expand PORT variable
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT} dotnet Daily_Bread.dll"]
