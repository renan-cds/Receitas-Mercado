#!/bin/sh
# Restaura um dump gerado por backup.sh: scripts/restore.sh arquivo.sql.gz
# Atenção: substitui o conteúdo atual do banco apontado pelas variáveis PG*.
set -eu
[ $# -eq 1 ] || { echo "uso: $0 arquivo.sql.gz" >&2; exit 2; }
gunzip -c "$1" | psql --set ON_ERROR_STOP=1 --single-transaction
echo "Restauração concluída."
