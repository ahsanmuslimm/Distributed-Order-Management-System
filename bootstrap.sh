#!/bin/bash

# Bootstrap Script - Distributed Order Management System
# Usage: bash bootstrap.sh
# This script sets up Phase 0 (foundations) of the project

set -e

echo "========================================="
echo "DOMS Bootstrap - Phase 0 Setup"
echo "========================================="

PROJECT_NAME="Distributed-Order-Management-System"
DOTNET_VERSION="8.0"

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

print_step() {
    echo -e "${BLUE}==>${NC} $1"
}

print_success() {
    echo -e "${GREEN}✓${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
}

# Step 1: Check prerequisites
print_step "Checking prerequisites..."

if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK not found. Install .NET $DOTNET_VERSION or later."
    exit 1
fi

DOTNET_INSTALLED=$(dotnet --version | cut -d. -f1)
if [ "$DOTNET_INSTALLED" -lt 8 ]; then
    echo "Error: .NET 8+ required. Current version: $(dotnet --version)"
    exit 1
fi

if ! command -v docker &> /dev/null; then
    echo "Error: Docker not found. Install Docker to continue."
    exit 1
fi

if ! command -v docker-compose &> /dev/null; then
    echo "Error: Docker Compose not found. Install Docker Compose to continue."
    exit 1
fi

print_success "Prerequisites satisfied (.NET $(dotnet --version), Docker, Docker Compose)"

# Step 2: Create solution structure
print_step "Creating solution structure..."

if [ -d "$PROJECT_NAME" ]; then
    print_warning "$PROJECT_NAME directory already exists. Using existing directory."
    cd "$PROJECT_NAME"
else
    mkdir -p "$PROJECT_NAME"
    cd "$PROJECT_NAME"
fi

# Initialize global.json (pin .NET version)
if [ ! -f global.json ]; then
    print_step "Creating global.json..."
    cat > global.json << 'EOF'
{
  "sdk": {
    "version": "8.0.0",
    "rollForward": "latestFeature"
  }
}
EOF
    print_success "global.json created"
fi

# Create solution
if [ ! -f "$PROJECT_NAME.sln" ]; then
    print_step "Creating solution..."
    dotnet new sln -n "$PROJECT_NAME" -f
    print_success "Solution created: $PROJECT_NAME.sln"
else
    print_warning "Solution already exists"
fi

# Step 3: Create projects
print_step "Creating service projects..."

PROJECTS=(
    "src:Orders.Service:web"
    "src:Inventory.Service:web"
    "src:Payment.Service:web"
    "src:Notification.Service:web"
    "src:Saga.Orchestrator:web"
    "src:Gateway:web"
    "src:Contracts:classlib"
    "src:Observability:classlib"
    "tests:Orders.Service.Tests:xunit"
    "tests:Inventory.Service.Tests:xunit"
    "tests:Payment.Service.Tests:xunit"
    "tests:Notification.Service.Tests:xunit"
    "tests:Saga.Orchestrator.Tests:xunit"
    "tests:Gateway.Tests:xunit"
    "tests:Kafka.Tests:xunit"
    "tests:Integration.Tests:xunit"
)

for project_spec in "${PROJECTS[@]}"; do
    IFS=':' read -r dir name template <<< "$project_spec"
    
    project_path="$dir/$name"
    
    if [ ! -d "$project_path" ]; then
        print_step "Creating $name ($template)..."
        mkdir -p "$dir"
        dotnet new "$template" -n "$name" -o "$project_path" -f
        dotnet sln "$PROJECT_NAME.sln" add "$project_path/$name.csproj"
        print_success "$name created"
    else
        print_warning "$name already exists"
    fi
done

# Step 4: Add NuGet packages (core dependencies)
print_step "Adding NuGet packages..."

CORE_PACKAGES=(
    "MassTransit:8.1.0"
    "MassTransit.RabbitMQ:8.1.0"
    "Confluent.Kafka:2.3.0"
    "Microsoft.EntityFrameworkCore:8.0.0"
    "Microsoft.EntityFrameworkCore.Npgsql:8.0.0"
    "Microsoft.EntityFrameworkCore.Tools:8.0.0"
    "StackExchange.Redis:2.6.122"
    "OpenTelemetry:1.7.0"
    "OpenTelemetry.Exporter.Jaeger:1.7.0"
    "OpenTelemetry.Instrumentation.AspNetCore:1.7.0"
    "OpenTelemetry.Instrumentation.EntityFrameworkCore:1.0.0"
    "Serilog:3.1.1"
    "Serilog.Sinks.Console:5.0.1"
    "Serilog.Sinks.Seq:6.0.0"
    "Polly:8.2.1"
    "Yarp.ReverseProxy:2.1.0"
    "FsCheck:2.16.6"
    "FsCheck.Xunit:2.16.6"
    "Testcontainers:3.6.0"
    "Testcontainers.PostgreSql:3.6.0"
    "Testcontainers.Kafka:3.6.0"
    "Testcontainers.Redis:3.6.0"
)

for package_spec in "${CORE_PACKAGES[@]}"; do
    IFS=':' read -r package_name package_version <<< "$package_spec"
    
    print_step "Adding $package_name (v$package_version)..."
    # Note: In real scenario, add to specific projects
    # For now, just print which packages should be added
    print_success "$package_name should be added to relevant projects"
done

# Step 5: Create docker-compose.yml
print_step "Creating docker-compose.yml..."

if [ ! -f docker-compose.yml ]; then
    cat > docker-compose.yml << 'DOCKER_EOF'
version: '3.8'

services:
  # Kafka (KRaft mode - single broker)
  kafka:
    image: confluentinc/cp-kafka:7.6.0
    container_name: kafka
    environment:
      KAFKA_NODE_ID: 1
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: 'CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT'
      KAFKA_ADVERTISED_LISTENERS: 'PLAINTEXT://kafka:9092'
      KAFKA_PROCESS_ROLES: 'broker,controller'
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
      KAFKA_CONTROLLER_QUORUM_VOTERS: '1@kafka:9093'
      KAFKA_CONTROLLER_LISTENER_NAMES: 'CONTROLLER'
      KAFKA_LOG_DIRS: '/tmp/kraft-combined-logs'
      CLUSTER_ID: 'MkQrr73dT1SnUAaddVavSQ'
    ports:
      - "9092:9092"
    volumes:
      - kafka-data:/var/lib/kafka/data
    healthcheck:
      test: ["CMD", "kafka-broker-api-versions", "--bootstrap-servers", "localhost:9092"]
      interval: 10s
      timeout: 5s
      retries: 5

  # PostgreSQL - Order DB
  postgres-orders:
    image: postgres:15-alpine
    container_name: postgres-orders
    environment:
      POSTGRES_DB: orders
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres-orders-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

  # PostgreSQL - Inventory DB
  postgres-inventory:
    image: postgres:15-alpine
    container_name: postgres-inventory
    environment:
      POSTGRES_DB: inventory
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5433:5432"
    volumes:
      - postgres-inventory-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

  # PostgreSQL - Payment DB
  postgres-payment:
    image: postgres:15-alpine
    container_name: postgres-payment
    environment:
      POSTGRES_DB: payment
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5434:5432"
    volumes:
      - postgres-payment-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

  # PostgreSQL - Notification DB
  postgres-notification:
    image: postgres:15-alpine
    container_name: postgres-notification
    environment:
      POSTGRES_DB: notification
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5435:5432"
    volumes:
      - postgres-notification-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

  # PostgreSQL - Saga DB
  postgres-saga:
    image: postgres:15-alpine
    container_name: postgres-saga
    environment:
      POSTGRES_DB: saga
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5436:5432"
    volumes:
      - postgres-saga-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

  # Redis - Caching
  redis:
    image: redis:7-alpine
    container_name: redis
    ports:
      - "6379:6379"
    volumes:
      - redis-data:/data
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5

  # Jaeger - Distributed Tracing
  jaeger:
    image: jaegertracing/all-in-one:latest
    container_name: jaeger
    ports:
      - "16686:16686"  # UI
      - "14268:14268"  # Collector HTTP
      - "14250:14250"  # gRPC
    environment:
      COLLECTOR_ZIPKIN_HTTP_PORT: 9411
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:16686/"]
      interval: 10s
      timeout: 5s
      retries: 5

volumes:
  kafka-data:
  postgres-orders-data:
  postgres-inventory-data:
  postgres-payment-data:
  postgres-notification-data:
  postgres-saga-data:
  redis-data:
DOCKER_EOF
    print_success "docker-compose.yml created"
else
    print_warning "docker-compose.yml already exists"
fi

# Step 6: Create .gitignore
print_step "Creating .gitignore..."

if [ ! -f .gitignore ]; then
    cat > .gitignore << 'EOF'
## Build results
bin/
obj/
*.dll
*.exe
*.pdb

## IDE
.vs/
.vscode/
*.swp
*.swo
*~
.DS_Store
*.user
*.userosscache

## Test results
TestResults/
coverage.cobertura.xml
coverage.opencover.xml

## Dependencies
packages/
node_modules/

## Environment
.env
.env.local
*.log
EOF
    print_success ".gitignore created"
fi

# Step 7: Build solution
print_step "Building solution..."

if dotnet build "$PROJECT_NAME.sln"; then
    print_success "Solution built successfully"
else
    print_warning "Build had issues. Review errors above."
fi

# Step 8: Print next steps
echo ""
echo "========================================="
echo "✓ Phase 0 Bootstrap Complete!"
echo "========================================="
echo ""
echo "Next steps:"
echo ""
echo "1. Start infrastructure (docker-compose):"
echo "   docker-compose up --wait"
echo ""
echo "2. Verify services are running:"
echo "   docker-compose ps"
echo ""
echo "3. Create shared Contracts library DTOs:"
echo "   cd src/Contracts"
echo "   # Add event/command DTO files"
echo ""
echo "4. Configure MassTransit + Kafka in Program.cs files"
echo ""
echo "5. Create first integration test:"
echo "   cd tests/Kafka.Tests"
echo "   dotnet test"
echo ""
echo "6. Follow tasks.md for Phase 1+ implementation"
echo ""
echo "========================================="
