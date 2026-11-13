#!/bin/sh
# Crea los buckets del almacenamiento local (docker-compose.storage.yml) y les pone la regla CORS. Ya existentes
# (409) no es un error.
#
# CORS: el aula sube los archivos de las lecciones (SCRUM-71) DIRECTO del navegador al almacenamiento, con un
# PUT a una URL firmada. Sin una regla CORS el navegador bloquea esa subida, igual que pasaría en R2 (por eso el
# README pide configurarla allí). S3_CORS_ORIGINS: los orígenes del aula, separados por espacios.
set -eu

cors_rule() {
  echo '<CORSConfiguration><CORSRule>'
  for origin in ${S3_CORS_ORIGINS}; do echo "<AllowedOrigin>${origin}</AllowedOrigin>"; done
  echo '<AllowedMethod>PUT</AllowedMethod><AllowedMethod>GET</AllowedMethod><AllowedHeader>*</AllowedHeader>'
  echo '<ExposeHeader>ETag</ExposeHeader><MaxAgeSeconds>3000</MaxAgeSeconds></CORSRule></CORSConfiguration>'
}

for bucket in aula-public aula-private; do
  code=$(curl -s -o /dev/null -w '%{http_code}' -X PUT \
    --aws-sigv4 "aws:amz:us-east-1:s3" --user "${S3_KEY}:${S3_SECRET}" \
    "http://s3:9000/${bucket}")
  echo "${bucket}: ${code}"
  case "${code}" in
    200|409) ;;
    *) exit 1 ;;
  esac

  cors=$(cors_rule | curl -s -o /dev/null -w '%{http_code}' -X PUT \
    --aws-sigv4 "aws:amz:us-east-1:s3" --user "${S3_KEY}:${S3_SECRET}" \
    -H 'Content-Type: application/xml' --data-binary @- \
    "http://s3:9000/${bucket}?cors")
  echo "${bucket} cors: ${cors}"
  [ "${cors}" = "200" ] || exit 1
done
