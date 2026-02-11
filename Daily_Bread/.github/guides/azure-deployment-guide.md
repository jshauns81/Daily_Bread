# Azure Deployment Guide — Daily Bread

> **Goal**: Deploy Daily Bread (Blazor Server + PostgreSQL) to Azure App Service with Azure Database for PostgreSQL Flexible Server.
>
> **Estimated Time**: 30–45 minutes  
> **Monthly Cost**: ~$13/mo (B1ms PostgreSQL) + Free/~$13 App Service = **$13–26/mo**  
> **Free Alternative**: Azure Free Tier gives you 12 months of both services at $0.

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Create Azure Resources](#2-create-azure-resources)
   - [2.1 Resource Group](#21-resource-group)
   - [2.2 PostgreSQL Flexible Server](#22-postgresql-flexible-server)
   - [2.3 App Service](#23-app-service)
3. [Configure the Database](#3-configure-the-database)
4. [Configure App Service Settings](#4-configure-app-service-settings)
5. [Set Up GitHub Actions CI/CD](#5-set-up-github-actions-cicd)
6. [Deploy](#6-deploy)
7. [Post-Deployment Verification](#7-post-deployment-verification)
8. [Custom Domain & HTTPS (Optional)](#8-custom-domain--https-optional)
9. [Monitoring & Backups](#9-monitoring--backups)
10. [Cost Optimization Tips](#10-cost-optimization-tips)
11. [Troubleshooting](#11-troubleshooting)

---

## 1. Prerequisites

- [ ] An [Azure account](https://azure.microsoft.com/free/) (free tier available)
- [ ] [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) installed (`az --version`)
- [ ] [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) installed
- [ ] Your Daily Bread repo pushed to GitHub (`jshauns81/Daily_Bread`)

### Login to Azure CLI

```bash
az login
az account show  # Verify you're on the right subscription
```

---

## 2. Create Azure Resources

### 2.1 Resource Group

A resource group is a logical container for all your Azure resources.

```bash
# Choose a region close to you
az group create \
  --name dailybread-rg \
  --location eastus
```

### 2.2 PostgreSQL Flexible Server

This creates the managed PostgreSQL database.

```bash
# Create the PostgreSQL Flexible Server (Burstable B1ms = ~$13/mo)
az postgres flexible-server create \
  --resource-group dailybread-rg \
  --name dailybread-db \
  --location eastus \
  --admin-user dbadmin \
  --admin-password '<CHOOSE_A_STRONG_PASSWORD>' \
  --sku-name Standard_B1ms \
  --tier Burstable \
  --storage-size 32 \
  --version 16 \
  --yes

# Create the application database
az postgres flexible-server db create \
  --resource-group dailybread-rg \
  --server-name dailybread-db \
  --database-name dailybread
```

> **⚠️ Important**: Replace `<CHOOSE_A_STRONG_PASSWORD>` with a real password.
> Store it securely — you'll need it for the connection string.

#### Allow Azure Services to Connect

```bash
# Allow Azure services (like App Service) to reach the database
az postgres flexible-server firewall-rule create \
  --resource-group dailybread-rg \
  --name dailybread-db \
  --rule-name AllowAzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0
```

> **Note**: For production, consider using Virtual Network integration instead of firewall rules.

### 2.3 App Service

```bash
# Create an App Service Plan (B1 = ~$13/mo, F1 = Free)
az appservice plan create \
  --resource-group dailybread-rg \
  --name dailybread-plan \
  --sku B1 \
  --is-linux

# Create the Web App
az webapp create \
  --resource-group dailybread-rg \
  --plan dailybread-plan \
  --name dailybread-app \
  --runtime 'DOTNETCORE:9.0'
```

> **Tip**: The `--name` must be globally unique. If `dailybread-app` is taken, try `dailybread-yourname`.

---

## 3. Configure the Database

Your app's `Program.cs` already supports the `DATABASE_URL` format (lines 653–661), so you just need to provide the connection string.

### Build the Connection String

```
Host=dailybread-db.postgres.database.azure.com;Port=5432;Database=dailybread;Username=dbadmin;Password=<YOUR_PASSWORD>;SSL Mode=Require;Trust Server Certificate=true
```

### Apply Migrations Locally (Against Azure DB)

Before deploying, apply your EF Core migrations to the Azure database:

```bash
# Temporarily set the connection string for migration
# (from your Daily_Bread project directory)
export DATABASE_URL="postgresql://dbadmin:<YOUR_PASSWORD>@dailybread-db.postgres.database.azure.com:5432/dailybread?sslmode=require"

dotnet ef database update --project Daily_Bread/Daily_Bread.csproj
```

> **Alternative**: Your app already runs `db.Database.MigrateAsync()` on startup (Program.cs line 274), so migrations will apply automatically on first deploy. However, running them manually first lets you catch errors before deployment.

---

## 4. Configure App Service Settings

These map to the environment variables your `Program.cs` already reads.

```bash
# Set the database connection (your app reads POSTGRES_* env vars at lines 664-668)
az webapp config appsettings set \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --settings \
    POSTGRES_HOST="dailybread-db.postgres.database.azure.com" \
    POSTGRES_PORT="5432" \
    POSTGRES_DB="dailybread" \
    POSTGRES_USER="dbadmin" \
    POSTGRES_PASSWORD="<YOUR_PASSWORD>" \
    ASPNETCORE_ENVIRONMENT="Production" \
    Application__MigrateDatabaseOnStartup="true" \
    Application__SeedDataOnStartup="true"
```

> **Security Best Practice**: For production, use Azure Key Vault instead of plain app settings for passwords. See [Key Vault References](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references).

### Enable HTTPS Only

```bash
az webapp update \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --https-only true
```

This is important because:
- Your PWA (`service-worker.js`) requires HTTPS
- Push notifications (`WebPush`) require a secure context
- Your cookie config (`.DailyBread.Auth`) uses `SameAsRequest` which will automatically be Secure over HTTPS

---

## 5. Set Up GitHub Actions CI/CD

### 5.1 Get the Publish Profile

```bash
az webapp deployment list-publishing-profiles \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --xml
```

Copy the entire XML output.

### 5.2 Add GitHub Secret

1. Go to `https://github.com/jshauns81/Daily_Bread/settings/secrets/actions`
2. Click **New repository secret**
3. Name: `AZURE_WEBAPP_PUBLISH_PROFILE`
4. Value: Paste the XML from above

### 5.3 Create the Workflow File

Create `.github/workflows/azure-deploy.yml` in your repo:

```yaml
name: Deploy to Azure

on:
  push:
    branches: [main]
  workflow_dispatch:  # Allow manual trigger

env:
  AZURE_WEBAPP_NAME: dailybread-app
  DOTNET_VERSION: '9.0.x'

jobs:
  build-and-deploy:
    runs-on: ubuntu-latest
    
    steps:
    - name: Checkout code
      uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ env.DOTNET_VERSION }}

    - name: Restore dependencies
      run: dotnet restore Daily_Bread/Daily_Bread.csproj

    - name: Build
      run: dotnet build Daily_Bread/Daily_Bread.csproj --configuration Release --no-restore

    - name: Publish
      run: dotnet publish Daily_Bread/Daily_Bread.csproj --configuration Release --output ./publish --no-build

    - name: Deploy to Azure Web App
      uses: azure/webapps-deploy@v3
      with:
        app-name: ${{ env.AZURE_WEBAPP_NAME }}
        publish-profile: ${{ secrets.AZURE_WEBAPP_PUBLISH_PROFILE }}
        package: ./publish
```

---

## 6. Deploy

### Option A: GitHub Actions (Recommended)

Push to `main` and the workflow runs automatically:

```bash
git push origin main
```

Monitor at: `https://github.com/jshauns81/Daily_Bread/actions`

### Option B: Direct CLI Deploy

For quick one-off deployments:

```bash
# From repo root
dotnet publish Daily_Bread/Daily_Bread.csproj -c Release -o ./publish

cd publish
zip -r ../deploy.zip .
cd ..

az webapp deploy \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --src-path deploy.zip \
  --type zip
```

---

## 7. Post-Deployment Verification

### Check the App is Running

```bash
# View app URL
az webapp show \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --query defaultHostName \
  --output tsv
```

Visit: `https://dailybread-app.azurewebsites.net`

### Verify Health Check

Your app has a health endpoint (Program.cs line 243):

```
https://dailybread-app.azurewebsites.net/health
```

### Check Logs if Something Goes Wrong

```bash
# Stream live logs
az webapp log tail \
  --resource-group dailybread-rg \
  --name dailybread-app

# Or view recent logs
az webapp log download \
  --resource-group dailybread-rg \
  --name dailybread-app
```

### Verify Checklist

- [ ] App loads at `https://dailybread-app.azurewebsites.net`
- [ ] Health endpoint returns healthy
- [ ] Can log in (Identity + cookies working)
- [ ] Chore data loads (PostgreSQL connected)
- [ ] SignalR connection established (`/chorehub`)
- [ ] PWA install prompt appears on mobile
- [ ] Push notifications work (WebPush over HTTPS)

---

## 8. Custom Domain & HTTPS (Optional)

If you want `dailybread.yourdomain.com` instead of `*.azurewebsites.net`:

```bash
# Add custom domain
az webapp config hostname add \
  --resource-group dailybread-rg \
  --webapp-name dailybread-app \
  --hostname dailybread.yourdomain.com

# Add free managed SSL certificate
az webapp config ssl create \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --hostname dailybread.yourdomain.com
```

You'll need to add a CNAME record in your DNS pointing to `dailybread-app.azurewebsites.net`.

---

## 9. Monitoring & Backups

### Database Backups (Automatic)

Azure PostgreSQL Flexible Server includes:
- **Automated daily backups** retained for 7 days (default)
- **Point-in-time restore** to any second within retention period

```bash
# Check backup status
az postgres flexible-server show \
  --resource-group dailybread-rg \
  --name dailybread-db \
  --query backup
```

### App Service Monitoring

```bash
# Enable Application Insights (optional, free tier available)
az monitor app-insights component create \
  --app dailybread-insights \
  --location eastus \
  --resource-group dailybread-rg

# Link to App Service
az webapp config appsettings set \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --settings APPLICATIONINSIGHTS_CONNECTION_STRING="<connection-string-from-above>"
```

### Set Up Alerts

```bash
# Alert when app is down for 5+ minutes
az monitor metrics alert create \
  --resource-group dailybread-rg \
  --name "DailyBread-HealthAlert" \
  --scopes "/subscriptions/<sub-id>/resourceGroups/dailybread-rg/providers/Microsoft.Web/sites/dailybread-app" \
  --condition "avg availabilityResults/availabilityPercentage < 95" \
  --description "Daily Bread availability dropped below 95%"
```

---

## 10. Cost Optimization Tips

| Optimization | Savings | How |
|---|---|---|
| **Use F1 (Free) App Service** | -$13/mo | `--sku F1` (limited: no custom domain, 60min/day compute) |
| **Stop when not needed** | Variable | `az webapp stop` / `az webapp start` during sleeping hours |
| **Use Burstable B1ms** | Already cheapest | $12.98/mo is the entry paid tier for PostgreSQL |
| **Azure Free Tier** | -$26/mo for 12 months | New accounts get both services free |
| **Dev/Test pricing** | ~30% off | Available with Visual Studio subscriptions |

### Your Break-Even

Running on your local server costs electricity (~$5–10/mo for always-on) plus the risk of data loss. Azure at $13–26/mo gives you backups, HTTPS, global availability, and zero maintenance.

---

## 11. Troubleshooting

### App Won't Start

```bash
# Check startup logs
az webapp log tail --resource-group dailybread-rg --name dailybread-app

# Common issue: missing connection string
az webapp config appsettings list \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --output table
```

### Database Connection Refused

```bash
# Verify firewall rules
az postgres flexible-server firewall-rule list \
  --resource-group dailybread-rg \
  --name dailybread-db \
  --output table

# Test connectivity from your machine
psql "host=dailybread-db.postgres.database.azure.com port=5432 dbname=dailybread user=dbadmin sslmode=require"
```

### SignalR WebSocket Issues

Azure App Service supports WebSockets but it must be enabled:

```bash
az webapp config set \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --web-sockets-enabled true
```

### Slow Cold Start

The B1 tier may have cold starts after inactivity. Mitigate with:

```bash
az webapp config set \
  --resource-group dailybread-rg \
  --name dailybread-app \
  --always-on true  # Requires B1 or higher (not available on F1)
```

---

## Quick Reference

| Resource | Value |
|---|---|
| **App URL** | `https://dailybread-app.azurewebsites.net` |
| **Resource Group** | `dailybread-rg` |
| **App Service** | `dailybread-app` |
| **DB Server** | `dailybread-db.postgres.database.azure.com` |
| **DB Name** | `dailybread` |
| **Health Check** | `https://dailybread-app.azurewebsites.net/health` |
| **GitHub Actions** | `https://github.com/jshauns81/Daily_Bread/actions` |

---

*Guide created for Daily Bread v1.0.0-rc.3 targeting .NET 9.0 with PostgreSQL (Npgsql).*
