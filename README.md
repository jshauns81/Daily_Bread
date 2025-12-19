# 🍞 Daily Bread

[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](https://github.com/jshauns81/Daily_Bread/releases)
[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-blueviolet.svg)](https://blazor.net/)

Daily Bread is a comprehensive family management and chore tracking application built with **.NET 9** and **Blazor Server**. It is designed to help families manage daily responsibilities, track chores, maintain a financial ledger for kids, and gamify tasks with achievements and savings goals.

## 🚀 Key Features

-   **Chore Management**: Schedule, track, and manage daily and weekly chores.
-   **Financial Ledger**: Track earnings, payouts, and balances for each child.
-   **Savings Goals**: Help children set and track progress toward specific purchase goals.
-   **Kid Mode**: A simplified, interactive interface for children to check off their tasks.
-   **Achievements**: Gamify chores with unlockable milestones and rewards.
-   **Family Dashboard**: A centralized view for parents to monitor progress and approve tasks.
-   **Push Notifications**: Integrated WebPush support for reminders and updates.
-   **Chore Planner**: Visual weekly wage board for scheduling and tracking earnings.
-   **Printable Chore Charts**: Generate fridge-ready weekly charts for kids.

## 🛠️ Tech Stack

-   **Framework**: .NET 9.0 (Blazor Server)
-   **Database**: PostgreSQL
-   **ORM**: Entity Framework Core
-   **Styling**: Custom CSS Design System (Nord-inspired dark theme)
-   **Deployment**: Docker & Docker Compose

## 📋 Prerequisites

Before setting up Daily Bread on your local server, ensure you have the following installed:

-   [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
-   [PostgreSQL](https://www.postgresql.org/download/) (if running bare-metal)
-   [Docker & Docker Compose](https://www.docker.com/products/docker-desktop/) (recommended for hosting)

## ⚙️ Local Setup & Hosting

### 1. Clone the Repository
```bash
git clone https://github.com/jshauns81/Daily_Bread.git
cd Daily_Bread
```

### 2. Security Configuration

#### Initial Setup
1. Copy the example configuration:
   ```bash
   cp Daily_Bread/appsettings.json.example Daily_Bread/appsettings.json
   ```

2. Edit `appsettings.json` with your actual credentials:
   - **Never commit this file to version control**
   - Use strong, unique passwords
   - The file is already in `.gitignore` to prevent accidental commits

3. For production deployments, use environment variables instead of `appsettings.json`:
   - `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`
   - `ADMIN_USERNAME`, `ADMIN_PASSWORD`

#### Important Security Notes
⚠️ **Never commit files containing:**
- Database passwords or connection strings
- API keys or tokens
- Private keys or certificates
- User credentials

✅ **Always use:**
- Environment variables for sensitive configuration
- `.env` files (already in `.gitignore`) for local development
- Secure secret management for production (Azure Key Vault, AWS Secrets Manager, etc.)

### 3. Environment Configuration
Create a `.env` file in the root directory (or set these as system environment variables) to configure your local environment:

```env
# Database Configuration
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
POSTGRES_USER=your_user
POSTGRES_PASSWORD=your_password
POSTGRES_DB=dailybread

# Application Settings
RECREATE_DATABASE=false
SHOW_ERRORS=false

# Identity / Admin Setup
ADMIN_EMAIL=admin@example.com
ADMIN_PASSWORD=YourSecurePassword123!
```

### 4. Database Setup
If you are running the application for the first time, ensure your PostgreSQL server is active. The application will automatically attempt to apply migrations on startup.

To manually update the database via CLI:
```bash
dotnet ef database update --project Daily_Bread
```

### 5. Running the Application

#### Using the .NET CLI:
```bash
dotnet run --project Daily_Bread
```
The app will typically be available at `http://localhost:5000`.

#### Using Docker Compose (Recommended for Local Hosting):
The repository includes a `docker-compose.yml` for easy local deployment:

```bash
docker-compose up -d
```
This will spin up both the .NET application and a PostgreSQL container, pre-configured to communicate with each other.

## 🐳 Docker Configuration

The included `Dockerfile` uses a multi-stage build for efficiency:
1.  **Build Stage**: Compiles the app using the .NET 9 SDK.
2.  **Runtime Stage**: Runs the app on the lightweight ASP.NET 9.0 runtime.

By default, the container listens on port `8080`. You can map this to any port on your local server using Docker.

## 🔒 Security & Identity

-   **Authentication**: Uses ASP.NET Core Identity with hardened cookie settings (HttpOnly, SameSite=Lax).
-   **Authorization**: Implements a "Default Deny" policy; all pages require login unless explicitly marked for anonymous access (like the Kid Mode or Login pages).
-   **Reverse Proxy**: The app is configured to handle `X-Forwarded-For` and `X-Forwarded-Proto` headers, making it safe to host behind Nginx, Caddy, or Traefik.

## 📄 License

This project is licensed under the terms specified in the repository.

---

## 📋 Version History

### 1.0.0 (Current)
- 🎉 **First stable release**
- ✅ Complete design system with `--ds-*` tokens
- ✅ Core chore management with daily expectations and earning chores
- ✅ Kid Mode with secure PIN authentication
- ✅ Financial ledger with savings goals
- ✅ Achievement system with bonuses and progress tracking
- ✅ Weekly progress tracking and reconciliation
- ✅ Push notification support
- ✅ PWA support for mobile installation

### 1.0.0-rc.3
- Complete design system migration
- Removed legacy token compatibility layer
- All components now use `--ds-*` tokens exclusively
- Improved consistency across all UI components

### 1.0.0-rc.2
- Achievement system with bonuses
- Weekly progress tracking and reconciliation
- Push notification support
- Chore Planner (Wage Board) improvements

### 1.0.0-rc.1
- Initial release candidate
- Core chore management features
- Kid Mode with PIN authentication
- Financial ledger and savings goals
