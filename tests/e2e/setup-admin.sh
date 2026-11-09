#!/bin/sh
# Prepara una base nueva: catálogo de permisos, cuentas de desarrollo y primer administrador.
set -eu

# up --build no reconstruye los servicios del perfil tools: sin esto se usaría un catálogo de permisos viejo.
docker compose --profile tools build provision
docker compose --profile tools run --rm provision

sql="select a.user_id from auth.auth_accounts a join auth.auth_identifiers i on i.auth_account_id = a.id where i.login_identifier = 'corporate.e2e@example.com'"
user_id=$(docker compose exec -T -e SQL="$sql" postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -c "$SQL"')

docker compose --profile tools run --rm provision \
  sh -c "dotnet run --project tools/AulaVirtual.AuthorizationProvisioning -- assign-first-administrator $user_id --confirm-authorization-change"
