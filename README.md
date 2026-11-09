# AulaVirtual Backend

API modular de AulaVirtual en .NET 10. La solución contiene los módulos de autenticación, autorización, usuarios, cursos y ediciones, almacenamiento y las demás bases del dominio.

## Requisitos

- .NET SDK 10.0.400 (`global.json`)
- PostgreSQL para la persistencia local y las pruebas de integración que lo requieren
- Configuración de R2 únicamente en los entornos que utilicen almacenamiento de archivos

## Desarrollo local

Desde la raíz de este repositorio:

```bash
dotnet restore AulaVirtual.sln
dotnet build AulaVirtual.sln
dotnet test AulaVirtual.sln
dotnet run --project src/AulaVirtual.Api/AulaVirtual.Api.csproj
```

Configura conexiones, JWT y Storage mediante variables de entorno, User Secrets o un mecanismo seguro del entorno. No coloques credenciales en archivos versionados. Los `appsettings*.json` incluidos son configuración de base, no plantillas para guardar secretos reales.

Sin .NET instalado, o para no configurar nada a mano, usa Docker: copia `.env.example` a `.env` y sigue [docker/README.md](docker/README.md) (PostgreSQL + migraciones + API en `http://localhost:5087`, y cuentas de prueba con el perfil `tools`).

## Configuración

Las dos primeras son imprescindibles: sin ellas la API falla al arrancar o en la primera petición. docker-compose las arma a partir de `.env`.

| Variable | Obligatoria | Qué es |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | Sí | Cadena de conexión a PostgreSQL |
| `Authentication__Jwt__SigningKey` | Sí | Clave de firma de los JWT (32+ caracteres) |
| `Cors__AllowedOrigins__0` | No | Origen permitido (en Development ya incluye `http://localhost:5173`) |
| `Authentication__*` | No | Duración de tokens, bloqueo y rate limit; valores por defecto en `appsettings.json` |
| `Storage__R2__Enabled` | No | `true` para activar el almacenamiento R2 (por defecto `false`) |
| `Storage__R2__Endpoint` | Si R2 | URL HTTPS del endpoint S3 de R2 |
| `Storage__R2__AccessKeyId` · `Storage__R2__SecretAccessKey` | Si R2 | Credenciales de R2 |
| `Storage__R2__PublicBucket` · `Storage__R2__PrivateBucket` | Si R2 | Buckets distintos para archivos públicos y privados |
| `Storage__R2__PublicBaseUrl` | No | URL HTTPS pública del bucket público |
| `Storage__R2__AllowedContentTypes__0`, `__1`… | Si R2 | Tipos MIME permitidos (al menos uno) |
| `Storage__R2__MaxBackendUploadBytes` · `Storage__R2__MaxDirectUploadBytes` | Si R2 | Tamaño máximo de subida, mayor que 0 |
| `Storage__R2__MaxPresignedUrlSeconds` | Si R2 | Vigencia de las URL firmadas (1 a 604800). Debe ser al menos 900: los enlaces de las lecciones duran 15 minutos |
| `Storage__R2__AllowHttpEndpoint` | No | Solo desarrollo: permite un servidor S3 local por HTTP. Fuera de `Development` la API no arranca con esta opción |
| `Courses__Cover__MaxBytes` · `Courses__Cover__AllowedContentTypes__0`… | No | Límites de portadas (por defecto 5 MB; jpeg, png, webp) |

Con `Storage__R2__Enabled=true` y algún dato inválido, la API falla al arrancar.

Los archivos de las lecciones (SCRUM-58) aceptan PDF, PNG, JPG, WebP, Word, PowerPoint y Excel, además de videos MP4 y WebM. Esos tipos también deben estar en `Storage__R2__AllowedContentTypes`, y `Storage__R2__MaxDirectUploadBytes` debe permitir videos de hasta 2 GB. Como el navegador sube directo a R2, el bucket privado necesita una regla CORS que permita `PUT` desde el origen del aula.

### Almacenamiento local

Para probar los archivos sin R2 hay un servidor S3 local (RustFS):

```bash
docker compose -f docker-compose.yml -f docker-compose.storage.yml up -d --build
```

Sin ese archivo, la API funciona igual, pero las subidas de archivos responden 503.

## Estructura

- `src/AulaVirtual.Api/`: host HTTP.
- `src/Modules/`: módulos de dominio, aplicación, infraestructura y API.
- `tests/`: pruebas por módulo.
- `tools/`: utilidades de provisioning explícito.
- `Directory.Packages.props`: versiones centralizadas de paquetes.

Las migraciones EF existentes están dentro de cada módulo de infraestructura. No se aplican automáticamente al preparar este repositorio; cualquier upgrade, rollback o provisioning debe validarse por entorno.

## CI/CD

Los flujos viven en `.github/workflows/`.

### CI (`ci.yml`)

Se ejecuta en cada pull request hacia `main`, y también de forma manual o desde `cd.yml`.

| Job | Objetivo | Bloquea |
|---|---|---|
| `build-and-test` | Restore, build Release y pruebas de toda la solución; publica los `.trx`. | Sí |
| `dependency-audit` | Falla ante paquetes NuGet vulnerables (directos y transitivos); informa los obsoletos. | Sí |
| `database` | PostgreSQL efímero: detecta cambios de modelo sin migración, aplica las migraciones sobre una base vacía, provisiona el catálogo de autorización y ejecuta la prueba de concurrencia contra Postgres. | Sí |
| `container` | Construye la imagen, levanta el stack con Docker Compose y valida `/health` y un endpoint público respaldado por la base. | Sí |
| `code-style` | `dotnet format --verify-no-changes`. Informativo hasta que el código base cumpla `.editorconfig`. | No |
| `ci-passed` | Agrega los resultados anteriores. | Es el check a exigir |

Configuración recomendada en GitHub (Settings, Branches, regla para `main`): exigir pull request, exigir el check `CI passed` y que la rama esté actualizada antes de integrar.

### CD (`cd.yml`)

En cada push a `main` o etiqueta `vX.Y.Z` ejecuta el CI completo y, solo si pasa, publica la imagen en `ghcr.io/<propietario>/<repositorio>` con etiquetas por SHA, rama y versión semántica, junto con SBOM y atestación de procedencia. El despliegue a un entorno (staging o producción) debe añadirse como un job posterior que consuma esa imagen y use un `environment` de GitHub con aprobación y secretos propios.

### Mantenimiento

`dependabot.yml` propone semanalmente actualizaciones de NuGet, GitHub Actions y la imagen base de Docker. Las acciones están fijadas por versión mayor; para mayor rigor de cadena de suministro conviene fijarlas por SHA de commit.
