COMPOSE := docker compose -f deploy/docker-compose.dev.yml --env-file .env

.PHONY: up down restart logs ps psql redis-cli seq build test format clean

up: ## Spustí dev stack (Postgres + Redis + Seq) na pozadí
	$(COMPOSE) up -d

down: ## Zastaví dev stack (volumes zachovány)
	$(COMPOSE) down

restart: ## Restart všech služeb
	$(COMPOSE) restart

logs: ## Tail logů všech služeb
	$(COMPOSE) logs -f --tail=100

ps: ## Stav kontejnerů
	$(COMPOSE) ps

psql: ## psql shell v Postgres
	$(COMPOSE) exec postgres psql -U $${POSTGRES_USER:-azkotle} -d $${POSTGRES_DB:-azkotle}

redis-cli: ## redis-cli shell v Redis
	$(COMPOSE) exec redis redis-cli

seq: ## Otevře Seq UI v prohlížeči (http://localhost:8081)
	@echo "Seq UI: http://localhost:8081"
	@echo "Ingest endpoint: http://localhost:5341"

build: ## dotnet build celé solution
	dotnet build

test: ## dotnet test celé solution
	dotnet test

format: ## dotnet format (pořadí: imports, whitespace, style)
	dotnet format

clean: ## Smaže bin/obj + volumes (destruktivní!)
	dotnet clean
	find src tests -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null || true
	$(COMPOSE) down -v

help: ## Zobrazí tuto nápovědu
	@awk 'BEGIN {FS = ":.*?## "} /^[a-zA-Z_-]+:.*?## / {printf "  \033[36m%-15s\033[0m %s\n", $$1, $$2}' $(MAKEFILE_LIST)

.DEFAULT_GOAL := help
