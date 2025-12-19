#!/bin/bash

# Daily Bread Deployment Script for Unraid
# Usage: ./deploy.sh [command]
# Commands:
#   start    - Start the application (default)
#   stop     - Stop the application
#   restart  - Restart the application
#   rebuild  - Pull latest code and rebuild
#   resetdb  - Reset database (DESTROYS ALL DATA)
#   logs     - Show live logs
#   status   - Show container status
#   setup    - First-time setup (creates .env file)

set -e

# Configuration
APP_DIR="/mnt/user/appdata/Daily_Bread"
COMPOSE_CMD="docker-compose"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Check if docker-compose exists, try docker compose if not
if ! command -v docker-compose &> /dev/null; then
    if docker compose version &> /dev/null; then
        COMPOSE_CMD="docker compose"
    else
        echo -e "${RED}Error: docker-compose not found. Installing...${NC}"
        curl -L "https://github.com/docker/compose/releases/download/v2.29.1/docker-compose-linux-x86_64" -o /usr/local/bin/docker-compose
        chmod +x /usr/local/bin/docker-compose
        echo -e "${GREEN}docker-compose installed successfully${NC}"
    fi
fi

cd "$APP_DIR" || { echo -e "${RED}Error: Cannot find $APP_DIR${NC}"; exit 1; }

case "${1:-start}" in
    setup)
        echo -e "${YELLOW}=== First-Time Setup ===${NC}"
        
        if [ -f .env ]; then
            echo -e "${YELLOW}.env file already exists. Skipping creation.${NC}"
        else
            echo -e "${GREEN}Creating .env file from template...${NC}"
            cp .env.example .env
            
            echo ""
            echo -e "${YELLOW}Please edit .env with your passwords:${NC}"
            echo "  nano $APP_DIR/.env"
            echo ""
            echo "Required settings:"
            echo "  POSTGRES_PASSWORD=YourSecureDbPassword"
            echo "  ADMIN_PASSWORD=YourSecureAdminPass (must have uppercase, lowercase, digit, special char)"
            echo ""
            exit 0
        fi
        ;;
        
    start)
        echo -e "${GREEN}=== Starting Daily Bread ===${NC}"
        
        if [ ! -f .env ]; then
            echo -e "${RED}Error: .env file not found. Run './deploy.sh setup' first.${NC}"
            exit 1
        fi
        
        $COMPOSE_CMD up -d
        echo -e "${GREEN}Daily Bread started!${NC}"
        echo -e "Access at: http://$(hostname -I | awk '{print $1}'):5000"
        ;;
        
    stop)
        echo -e "${YELLOW}=== Stopping Daily Bread ===${NC}"
        $COMPOSE_CMD down
        echo -e "${GREEN}Daily Bread stopped.${NC}"
        ;;
        
    restart)
        echo -e "${YELLOW}=== Restarting Daily Bread ===${NC}"
        $COMPOSE_CMD restart
        echo -e "${GREEN}Daily Bread restarted.${NC}"
        ;;
        
    rebuild)
        echo -e "${YELLOW}=== Rebuilding Daily Bread ===${NC}"
        
        echo "Pulling latest code..."
        git pull origin master
        
        echo "Stopping containers..."
        $COMPOSE_CMD down
        
        echo "Rebuilding and starting..."
        $COMPOSE_CMD up -d --build
        
        echo -e "${GREEN}Daily Bread rebuilt and started!${NC}"
        echo -e "Access at: http://$(hostname -I | awk '{print $1}'):5000"
        ;;
    
    resetdb)
        echo -e "${RED}=== DATABASE RESET ===${NC}"
        echo -e "${RED}WARNING: This will DELETE ALL DATA including users, chores, and transactions!${NC}"
        echo ""
        
        # Confirmation prompt
        read -p "Are you sure you want to reset the database? Type 'yes' to confirm: " confirm
        if [ "$confirm" != "yes" ]; then
            echo -e "${YELLOW}Cancelled.${NC}"
            exit 0
        fi
        
        echo ""
        echo -e "${YELLOW}Stopping containers...${NC}"
        $COMPOSE_CMD down
        
        echo -e "${YELLOW}Removing database volume...${NC}"
        docker volume rm dailybread_postgres_data 2>/dev/null || true
        
        echo -e "${YELLOW}Starting fresh...${NC}"
        $COMPOSE_CMD up -d --build
        
        echo ""
        echo -e "${GREEN}Database reset complete!${NC}"
        echo -e "${CYAN}The admin user will be created from your .env settings:${NC}"
        echo -e "  Username: \$ADMIN_USERNAME"
        echo -e "  Password: \$ADMIN_PASSWORD"
        echo ""
        echo -e "Access at: http://$(hostname -I | awk '{print $1}'):5000"
        ;;
        
    logs)
        echo -e "${GREEN}=== Daily Bread Logs (Ctrl+C to exit) ===${NC}"
        $COMPOSE_CMD logs -f dailybread
        ;;
        
    status)
        echo -e "${GREEN}=== Container Status ===${NC}"
        docker ps --filter "name=dailybread" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
        ;;
        
    *)
        echo "Usage: $0 {start|stop|restart|rebuild|resetdb|logs|status|setup}"
        echo ""
        echo "Commands:"
        echo "  setup    - First-time setup (creates .env file)"
        echo "  start    - Start the application"
        echo "  stop     - Stop the application"
        echo "  restart  - Restart the application"
        echo "  rebuild  - Pull latest code and rebuild containers"
        echo -e "  resetdb  - ${RED}Reset database (DESTROYS ALL DATA)${NC}"
        echo "  logs     - Show live application logs"
        echo "  status   - Show container status"
        exit 1
        ;;
esac
