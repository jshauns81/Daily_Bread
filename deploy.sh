#!/bin/bash

# Daily Bread Deployment Script for Unraid
# Usage: ./deploy.sh [command] [--verbose]
#
# Commands:
#   start    - Start the application
#   stop     - Stop the application
#   restart  - Restart the application
#   rebuild  - Pull latest code and rebuild (default)
#   logs     - Show live logs
#   status   - Show container status
#
# Options:
#   --verbose, -v  - Show detailed output (for manual runs)
#
# Examples:
#   ./deploy.sh rebuild           # Quiet mode (for cron/webhooks)
#   ./deploy.sh rebuild --verbose # Detailed output (for manual runs)
#   ./deploy.sh -v rebuild        # Same as above

set -e

# Configuration
APP_DIR="/mnt/user/appdata/Daily_Bread"
COMPOSE_CMD="docker-compose"

# Parse arguments
VERBOSE=false
COMMAND="rebuild"

for arg in "$@"; do
  case "$arg" in
    -v|--verbose) VERBOSE=true ;;
    start|stop|restart|rebuild|logs|status) COMMAND="$arg" ;;
  esac
done

# Colors (only used in verbose mode)
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

# Output helpers
log() {
  if $VERBOSE; then
    echo -e "$1"
  fi
}

log_always() {
  echo -e "$1"
}

run() {
  if $VERBOSE; then
    "$@"
  else
    "$@" >/dev/null 2>&1
  fi
}

run_visible() {
  # Always show output (for logs/status commands)
  "$@"
}

# Check for docker-compose
if ! command -v docker-compose &>/dev/null; then
  if docker compose version &>/dev/null 2>&1; then
    COMPOSE_CMD="docker compose"
  else
    log_always "${RED}Error: docker-compose not found${NC}"
    exit 1
  fi
fi

cd "$APP_DIR" || { log_always "${RED}Error: Cannot find $APP_DIR${NC}"; exit 1; }

case "$COMMAND" in
  start)
    log "${GREEN}=== Starting Daily Bread ===${NC}"
    if [ ! -f .env ]; then
      log_always "${RED}Error: .env file not found${NC}"
      exit 1
    fi
    run $COMPOSE_CMD up -d
    log "${GREEN}Daily Bread started!${NC}"
    log "Access at: http://$(hostname -I | awk '{print $1}'):5000"
    ;;

  stop)
    log "${YELLOW}=== Stopping Daily Bread ===${NC}"
    run $COMPOSE_CMD down
    log "${GREEN}Daily Bread stopped.${NC}"
    ;;

  restart)
    log "${YELLOW}=== Restarting Daily Bread ===${NC}"
    run $COMPOSE_CMD restart
    log "${GREEN}Daily Bread restarted.${NC}"
    ;;

  rebuild)
    log "${YELLOW}=== Rebuilding Daily Bread ===${NC}"

    log "Pulling latest code..."
    run git pull origin master

    log "Stopping containers..."
    run $COMPOSE_CMD down

    log "Rebuilding and starting..."
    run $COMPOSE_CMD up -d --build

    # These lines are parsed by the webhook script - always output them
    log_always "Daily Bread rebuilt and started!"
    log_always "Access at: http://$(hostname -I | awk '{print $1}'):5000"
    ;;

  logs)
    log "${GREEN}=== Daily Bread Logs (Ctrl+C to exit) ===${NC}"
    run_visible $COMPOSE_CMD logs -f dailybread
    ;;

  status)
    log "${GREEN}=== Container Status ===${NC}"
    run_visible docker ps --filter "name=dailybread" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
    ;;

  *)
    log_always "Usage: $0 {start|stop|restart|rebuild|logs|status} [--verbose]"
    exit 1
    ;;
esac
