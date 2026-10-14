# Receitas-Mercado

Aplicativo web (PWA) de estoque doméstico de mantimentos integrado a receitas culinárias.
Custo operacional zero: software livre, auto-hospedagem, APIs gratuitas e IA opcional com chave própria (BYOK).

- Estoque por lotes com validade (saída FEFO: primeiro a vencer, primeiro a sair), mínimos e histórico de movimentações.
- Receitas manuais, importadas por URL (JSON-LD, com proteção contra SSRF) ou do TheMealDB, sempre revisadas antes de salvar.
- Compatibilidade receita × estoque ("pode preparar" / "faltam ingredientes" / "verificar") e baixa atômica no preparo.
- Listas de compras geradas a partir de receitas, com lançamento das compras no estoque.
- Despensas compartilhadas com papéis (administrador, editor, leitor) e convites por link.
- IA opcional (OpenAI-compatível, inclusive modelos locais): sugestões, adaptação de porções e substituições.

Documentação funcional e técnica: [docs/PLANEJAMENTO.md](docs/PLANEJAMENTO.md) (desvios do plano no Apêndice D).

## Stack

ASP.NET Core 8 (minimal APIs) · EF Core + PostgreSQL 16 · React 19 + TypeScript + Vite + Tailwind · PWA (vite-plugin-pwa).

## Executando com Docker

```bash
cp .env.example .env        # defina POSTGRES_PASSWORD
docker compose up -d --build
```

O app fica em `http://localhost:8080` (porta em `PUBLIC_PORT`). As migrações rodam na inicialização.
Atrás de um proxy HTTPS (ex.: Caddy, Cloudflare Tunnel), o app já confia em `X-Forwarded-*`.
Um contêiner de backup gera um dump diário em um volume (retenção em `BACKUP_KEEP_DAYS`):

```bash
scripts/restore.sh backups/receitas_mercado_AAAAMMDDTHHMMSSZ.sql.gz   # usa as variáveis PG*
```

## Desenvolvimento local

Requisitos: .NET 8 SDK, Node 22+, PostgreSQL 16.

```bash
psql -U postgres -c "create role rm login password 'rm_dev_pw' createdb" -c "create database receitas_mercado owner rm"

# API (http://localhost:5083); aplica migrações ao iniciar
cd src/ReceitasMercado.Api
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5083 dotnet run --no-launch-profile

# Frontend com proxy para a API (http://localhost:5173)
cd web && npm ci && npm run dev
```

`npm run build` gera o PWA em `src/ReceitasMercado.Api/wwwroot`, servido pela própria API.

## Testes

```bash
dotnet test            # unidade (domínio) + integração (PostgreSQL real, um banco temporário por classe)
cd web && npm test && npm run typecheck
```

Os testes de integração usam `RM_TEST_ADMIN_CONNECTION` (padrão: `Host=localhost;Username=rm;Password=rm_dev_pw;Database=postgres`).
