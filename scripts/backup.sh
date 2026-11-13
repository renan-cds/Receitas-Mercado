#!/bin/sh
# Gera um dump comprimido do banco e apaga os mais antigos que BACKUP_KEEP_DAYS.
# Usa as variáveis padrão do libpq (PGHOST, PGUSER, PGPASSWORD, PGDATABASE).
set -eu
dir="${BACKUP_DIR:-./backups}"
keep="${BACKUP_KEEP_DAYS:-14}"
mkdir -p "$dir"
file="$dir/receitas_mercado_$(date -u +%Y%m%dT%H%M%SZ).sql.gz"
pg_dump --no-owner --clean --if-exists | gzip > "$file.tmp"
mv "$file.tmp" "$file"
find "$dir" -name 'receitas_mercado_*.sql.gz' -mtime +"$keep" -delete
echo "Backup criado: $file"
