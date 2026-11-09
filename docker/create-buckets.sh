#!/bin/sh
# Crea los buckets del almacenamiento local (docker-compose.storage.yml). Ya existentes (409) no es un error.
set -eu

for bucket in aula-public aula-private; do
  code=$(curl -s -o /dev/null -w '%{http_code}' -X PUT \
    --aws-sigv4 "aws:amz:us-east-1:s3" --user "${S3_KEY}:${S3_SECRET}" \
    "http://s3:9000/${bucket}")
  echo "${bucket}: ${code}"
  case "${code}" in
    200|409) ;;
    *) exit 1 ;;
  esac
done
