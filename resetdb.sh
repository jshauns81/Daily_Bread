#!/bin/bash

# Daily Bread Database Reset Script
# 
# This script is intentionally separate from deploy.sh to prevent
# accidental database destruction during routine deployments.
#
# Location: /mnt/user/appdata/Daily_Bread/resetdb.sh
# Make executable: chmod +x resetdb.sh
#
# Usage: ./resetdb.sh
#
# WARNING: This script DESTROYS ALL DATA including:
#   - All users
#   - All chores
#   - All transactions
#   - All history

# Exit on error (-e), undefined variable (-u), or pipe failure (-o pipefail)
# This catches more failure modes than plain `set -e` and prevents silent failures
# from commands in pipelines. Essential for destructive scripts.
set -euo pipefail

# Configuration
APP_DIR="/mnt/user/appdata/Daily_Bread"
COMPOSE_CMD=(docker-compose)  # Array to safely handle 'docker compose' with space
VOLUME_NAME="dailybread_postgres_data"
COMPOSE_FILE="docker-compose.yml"
EXPECTED_REPO="jshauns81/Daily_Bread"
DB_CONTAINER_NAME="dailybread-postgres"
DB_READY_TIMEOUT=60

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m'

# ===== Helper Functions =====

# Print error message and exit - centralizes error handling
# Uses $* to capture all arguments in case caller passes multiple
die() {
    echo -e "${RED}Error: $*${NC}" >&2
    exit 1
}

# Print warning message (non-fatal) - for recoverable issues
# Uses $* for consistency with die()
warn() {
    echo -e "${YELLOW}Warning: $*${NC}" >&2
}

# Get the best available LAN IP address with multiple fallback methods
# This handles edge cases where hostname -I returns nothing, multiple IPs,
# or the command doesn't exist on the system
get_lan_ip() {
    local ip=""
    
    # Method 1: Try hostname -I (Linux, returns space-separated list)
    # Take first IP only to avoid showing multiple addresses
    if command -v hostname &> /dev/null; then
        ip=$(hostname -I 2>/dev/null | awk '{print $1}') || true
    fi
    
    # Method 2: If hostname -I failed or empty, try ip route
    # This gets the IP used for default route - usually the right one
    # Uses awk instead of grep -P for BusyBox compatibility (Unraid)
    if [[ -z "${ip:-}" ]] && command -v ip &> /dev/null; then
        ip=$(ip route get 1.1.1.1 2>/dev/null | awk '/src/ {for(i=1;i<=NF;i++) if($i=="src"){print $(i+1); exit}}') || true
    fi
    
    # Method 3: Fall back to parsing ifconfig for first non-localhost IPv4
    # Uses awk instead of grep -oP for BusyBox compatibility
    if [[ -z "${ip:-}" ]] && command -v ifconfig &> /dev/null; then
        ip=$(ifconfig 2>/dev/null | awk '/inet / && !/127\./ {gsub(/.*inet:?/, ""); gsub(/[^0-9.].*/, ""); print; exit}') || true
    fi
    
    # Method 4: Last resort - use localhost (at least it's valid)
    if [[ -z "${ip:-}" ]]; then
        ip="localhost"
    fi
    
    echo "$ip"
}

# Wait for postgres database to be ready using health checks or fallback detection
# This replaces naive sleep with proper readiness verification.
# A tired admin at 2 AM shouldn't have to guess if the DB is ready.
wait_for_db_ready() {
    local elapsed=0
    
    echo -e "${CYAN}      Checking container health status...${NC}"
    
    # Give containers a moment to start before checking
    sleep 2
    elapsed=2
    
    # Check if the container has a healthcheck defined by inspecting health status
    local health_status=""
    health_status=$(docker inspect --format='{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$DB_CONTAINER_NAME" 2>/dev/null) || health_status="none"
    
    if [[ "$health_status" != "none" ]]; then
        # Health check is available - this is the preferred method
        echo -e "${CYAN}      Health check available, waiting for 'healthy' status...${NC}"
        
        # Poll health status until healthy or timeout
        while [[ $elapsed -lt $DB_READY_TIMEOUT ]]; do
            health_status=$(docker inspect --format='{{.State.Health.Status}}' "$DB_CONTAINER_NAME" 2>/dev/null) || health_status="unknown"
            
            if [[ "$health_status" == "healthy" ]]; then
                echo -e "${GREEN}      Database is healthy after ${elapsed}s.${NC}"
                return 0
            fi
            # Note: "unhealthy" status means keep waiting - container might still be booting
            # Only give up at timeout, not on transient unhealthy state
            
            sleep 1
            ((elapsed++)) || true
            
            # Progress indicator every 10 seconds to show we're not frozen
            if ((elapsed % 10 == 0)); then
                echo -e "${CYAN}      Still waiting... (${elapsed}s/${DB_READY_TIMEOUT}s, status: ${health_status})${NC}"
            fi
        done
        
        warn "Health check did not reach 'healthy' within ${DB_READY_TIMEOUT}s (last status: ${health_status})"
        return 0
    fi
    
    # Fallback: No health check defined, try multiple methods to verify postgres is ready
    echo -e "${CYAN}      No health check defined, using fallback detection...${NC}"
    
    while [[ $elapsed -lt $DB_READY_TIMEOUT ]]; do
        # First verify container is running
        local container_status=""
        container_status=$(docker inspect --format='{{.State.Status}}' "$DB_CONTAINER_NAME" 2>/dev/null) || container_status="not found"
        
        if [[ "$container_status" != "running" ]]; then
            sleep 1
            ((elapsed++)) || true
            continue
        fi
        
        # Method A: Try pg_isready if available in container (not all images have it)
        if docker exec "$DB_CONTAINER_NAME" pg_isready -U postgres &>/dev/null; then
            echo -e "${GREEN}      Database accepting connections after ${elapsed}s (pg_isready).${NC}"
            return 0
        fi
        
        # Method B: Try TCP probe with nc if pg_isready failed/unavailable
        # This checks if postgres port is listening, which is a good proxy for readiness
        if docker exec "$DB_CONTAINER_NAME" nc -z localhost 5432 &>/dev/null; then
            echo -e "${GREEN}      Database port responding after ${elapsed}s (nc probe).${NC}"
            return 0
        fi
        
        # Method C: If container has been running for 12+ seconds, consider it ready enough
        # This is last resort when neither pg_isready nor nc are available
        if [[ $elapsed -ge 12 ]]; then
            local started_at=""
            started_at=$(docker inspect --format='{{.State.StartedAt}}' "$DB_CONTAINER_NAME" 2>/dev/null) || true
            if [[ -n "$started_at" ]]; then
                echo -e "${YELLOW}      Container running, assuming ready (no probe tools available).${NC}"
                return 0
            fi
        fi
        
        sleep 1
        ((elapsed++)) || true
        
        # Progress indicator every 10 seconds
        if ((elapsed % 10 == 0)); then
            echo -e "${CYAN}      Still waiting... (${elapsed}s/${DB_READY_TIMEOUT}s)${NC}"
        fi
    done
    
    warn "Database readiness could not be confirmed within ${DB_READY_TIMEOUT}s"
    return 0
}

# ===== Pre-flight Safety Checks =====
# These run BEFORE any destructive action and BEFORE user confirmation.
# Goal: fail fast if something is wrong with the environment.

# Check if docker-compose exists, try docker compose (v2) if not
if ! command -v docker-compose &> /dev/null; then
    if docker compose version &> /dev/null; then
        COMPOSE_CMD=(docker compose)  # Array handles the space safely
    else
        die "docker-compose not found. Install Docker Compose to use this script."
    fi
fi

# Safety Check 1: Verify APP_DIR exists
# WHY: Prevents running in wrong directory or on wrong machine entirely
if [[ ! -d "$APP_DIR" ]]; then
    die "Application directory does not exist: $APP_DIR\nIs this the correct machine?"
fi

cd "$APP_DIR" || die "Cannot change to directory: $APP_DIR"

# Safety Check 2: Verify docker-compose.yml exists in APP_DIR
# WHY: Ensures we're in a valid Daily Bread installation, not a random directory
if [[ ! -f "$COMPOSE_FILE" ]]; then
    die "Missing $COMPOSE_FILE in $APP_DIR\nIs this a valid Daily Bread installation?"
fi

# Safety Check 3: Verify compose file references expected volume name
# WHY: Prevents destroying wrong database if someone modified the compose file
# or if we're somehow in a different project with similar structure
if ! grep -q "$VOLUME_NAME" "$COMPOSE_FILE"; then
    die "$COMPOSE_FILE does not reference expected volume '$VOLUME_NAME'.\nWrong project or modified config? Safety stop."
fi

# Safety Check 4: If in a git repo, verify it's the correct repository
# WHY: Prevents running against a fork or different project that happens to have same structure
# A tired admin might SSH to wrong server - this catches that
if [[ -d ".git" ]]; then
    git_origin=""
    git_origin=$(git remote get-url origin 2>/dev/null) || true
    
    if [[ -n "$git_origin" ]]; then
        if [[ "$git_origin" != *"$EXPECTED_REPO"* ]]; then
            die "Git origin '$git_origin' does not match expected repository '$EXPECTED_REPO'.\nAre you on the right server? Safety stop."
        fi
        echo -e "${GREEN}✓ Repository verified: ${EXPECTED_REPO}${NC}"
    fi
fi

echo -e "${GREEN}✓ Working directory verified: ${APP_DIR}${NC}"
echo -e "${GREEN}✓ Compose file verified: contains ${VOLUME_NAME}${NC}"

# Safety Check 5: Prevent running from webhook or non-interactive shell
# WHY: This is the MOST CRITICAL safety check. Database destruction must require
# a human at the keyboard. Webhooks, cron jobs, and scripts cannot trigger this.
if [[ ! -t 0 ]]; then
    die "This script must be run interactively.\nIt cannot be triggered from webhooks, cron, or automated scripts."
fi

# Clear screen to make warning unmissable
clear

echo ""
echo -e "${RED}${BOLD}╔══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${RED}${BOLD}║                                                              ║${NC}"
echo -e "${RED}${BOLD}║              ⚠️  DATABASE RESET WARNING ⚠️                    ║${NC}"
echo -e "${RED}${BOLD}║                                                              ║${NC}"
echo -e "${RED}${BOLD}╚══════════════════════════════════════════════════════════════╝${NC}"
echo ""
echo -e "${YELLOW}This will ${RED}PERMANENTLY DELETE${YELLOW} all data in Daily Bread:${NC}"
echo ""
echo "  • All user accounts"
echo "  • All chores and assignments"
echo "  • All transaction history"
echo "  • All settings and preferences"
echo ""
echo -e "${CYAN}Volume to be deleted:  ${BOLD}${VOLUME_NAME}${NC}"
echo -e "${CYAN}Working directory:     ${BOLD}${APP_DIR}${NC}"
echo ""

# First confirmation - requires typing exact phrase
# WHY: Prevents accidental Enter key or muscle memory from confirming
echo -e "${YELLOW}Are you absolutely sure you want to do this?${NC}"
read -r -p "Type 'delete my database' to continue: " confirm1

if [[ "${confirm1:-}" != "delete my database" ]]; then
    echo ""
    echo -e "${GREEN}Cancelled. No changes were made.${NC}"
    exit 0
fi

echo ""

# Second confirmation with random code
# WHY: Even if someone types the phrase from muscle memory, they can't predict the code.
# This forces the user to actually read the screen.
random_code=$((RANDOM % 9000 + 1000))
echo -e "${YELLOW}Final confirmation required.${NC}"
echo -e "Enter this code to proceed: ${BOLD}${random_code}${NC}"
read -r -p "Code: " confirm2

if [[ "${confirm2:-}" != "$random_code" ]]; then
    echo ""
    echo -e "${GREEN}Cancelled. No changes were made.${NC}"
    exit 0
fi

echo ""
echo -e "${RED}${BOLD}Proceeding with database reset...${NC}"
echo ""

# ===== Perform reset =====

echo -e "${YELLOW}[1/4] Stopping containers...${NC}"
"${COMPOSE_CMD[@]}" down
echo -e "${GREEN}      ✓ Containers stopped.${NC}"

echo ""
echo -e "${YELLOW}[2/4] Removing database volume...${NC}"
if docker volume rm "$VOLUME_NAME" >/dev/null 2>&1; then
    echo -e "${GREEN}      ✓ Volume removed.${NC}"
else
    echo -e "${YELLOW}      Volume did not exist or already removed.${NC}"
fi

echo ""
echo -e "${YELLOW}[3/4] Rebuilding containers...${NC}"
"${COMPOSE_CMD[@]}" up -d --build
echo -e "${GREEN}      ✓ Containers started.${NC}"

echo ""
echo -e "${YELLOW}[4/4] Waiting for database initialization...${NC}"
wait_for_db_ready

echo ""
echo -e "${GREEN}${BOLD}╔══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${GREEN}${BOLD}║              ✓ Database Reset Complete                       ║${NC}"
echo -e "${GREEN}${BOLD}╚══════════════════════════════════════════════════════════════╝${NC}"
echo ""
echo -e "${CYAN}A fresh admin user will be created from your .env settings:${NC}"
echo ""
if [[ -f .env ]]; then
    # Use parameter expansion default to handle missing value safely with set -u
    admin_user=""
    admin_user=$(grep -E '^ADMIN_USERNAME=' .env 2>/dev/null | cut -d'=' -f2) || true
    admin_user="${admin_user:-(not set)}"
    echo -e "  Username: ${BOLD}${admin_user}${NC}"
    echo -e "  Password: ${BOLD}(from ADMIN_PASSWORD in .env)${NC}"
else
    echo -e "  ${YELLOW}Warning: .env file not found - using built-in defaults${NC}"
fi
echo ""

# Get LAN IP with robust fallback logic
lan_ip=$(get_lan_ip)
echo -e "Access at: ${BOLD}http://${lan_ip}:5000${NC}"
echo ""