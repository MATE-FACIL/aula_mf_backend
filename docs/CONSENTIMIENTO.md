# Consentimiento del padre para los datos del menor

API de SCRUM-38. Todas las rutas empiezan por `/api/v1` y necesitan sesión.

## Cómo funciona

1. El padre, la madre o el tutor entra a su panel de la familia y ve un aviso si falta aceptar el texto de **términos de uso y política de privacidad** por alguno de sus hijos.
2. Lee el texto, marca que es el padre, madre o tutor legal y **acepta por todos sus hijos pendientes a la vez**.
3. Queda guardado, por cada hijo: **quién** aceptó, **cuándo** y **qué versión** del texto.
4. El administrador ve en la **ficha del alumno** cómo va: *Aceptado* (cuándo y por quién), *Pendiente* o *Sin padre vinculado*.

El consentimiento **se registra pero todavía no bloquea nada**: un alumno pendiente puede matricularse y estudiar. Si el dueño decide que la activación de la matrícula lo exija, es un cambio pequeño y está pendiente de esa decisión.

## Qué cuenta como aceptado

- **Basta con un padre.** Si el alumno tiene dos padres vinculados, la aceptación de uno vale para el hijo.
- **Solo cuenta mientras ese padre siga vinculado.** Si el administrador desvincula a quien aceptó, el alumno deja de figurar como aceptado (queda *Sin padre vinculado* o *Pendiente* si hay otro padre). La aceptación no se borra: si se vuelve a vincular, vuelve a contar.
- **Solo vale para la versión vigente del texto.** Cuando el texto cambia, todos los hijos vuelven a *Pendiente* y se pide aceptar de nuevo. Las aceptaciones anteriores quedan guardadas como constancia de lo que se aceptó en su momento.

## El texto y sus versiones

El texto vive en el código (`ConsentTexts.Current`, módulo Users), para que cada cambio quede en el historial de git junto con su fecha. Cada texto tiene una `version` y una marca `isProvisional`.

> **El texto actual es provisional.** Se escribió para poder construir y probar el flujo. **No lo revisó el dueño ni un abogado**, y falta añadir el medio de contacto para ejercer los derechos. Hay que validarlo antes de abrir el aula a las familias. Mientras sea provisional, el panel de la familia lo dice («en revisión») y la ficha del administrador también.

**Cómo cambiarlo:**

1. Edita el texto en `src/Modules/Users/AulaVirtual.Modules.Users.Application/Consent/ConsentText.cs`.
2. Cambia `Version` (cualquier valor nuevo, de hasta 60 caracteres; por ejemplo `2026-12`).
3. Pon `IsProvisional` en `false` cuando el dueño lo haya validado.
4. Despliega. No hace falta migración: los padres verán el aviso de nuevo.

## Padre: sus hijos y el texto

La ruta no lleva ningún id de padre ni de alumno: todo parte de los vínculos de quien tiene la sesión. Otra cuenta recibe una lista vacía o 404.

| Método y ruta | Qué hace |
|---|---|
| `GET /families/me/consent` | El texto vigente (`version`, `title`, `isProvisional`, `sections`) y, por cada hijo, `status` (`accepted` o `pending`), `acceptedAt` y `acceptedByName`; más cuántos faltan (`pending`) |
| `POST /families/me/consent` | Cuerpo: `{ version, studentUserIds }`. Acepta el texto por esos hijos y devuelve lo mismo que el `GET` |

Reglas del `POST`:

- **`version` debe ser la vigente.** Si el texto cambió mientras el padre lo leía: **409** («Recarga la página…»). Así nadie acepta un texto que no leyó.
- **Todo o nada.** Si uno de los hijos no es del padre, **404** y no se guarda ninguno.
- Sin hijos: **400**. Más de 20 a la vez: **400**.
- **Repetir no cambia nada**: la fecha que queda es la primera. Varias aceptaciones a la vez tampoco duplican ni dan error.
- Un alumno, o un padre sin hijos, no puede aceptar por nadie (404).

## Administrador: la ficha del alumno

`GET /admin/users/{id}` (permiso `users.manage`) devuelve, solo para los alumnos, un campo `consent`:

| Campo | Qué es |
|---|---|
| `status` | `accepted`, `pending` o `no_guardian` (el alumno no tiene padre vinculado: nadie puede dar el consentimiento) |
| `currentVersion` | La versión del texto vigente |
| `isProvisional` | `true` mientras el texto no esté validado |
| `acceptedAt`, `acceptedByName` | Cuándo y quién, si está aceptado |

Para una persona que no es alumno, `consent` es `null`.

## Datos guardados

Tabla `users.guardian_consents`: una fila por padre, alumno y versión.

| Columna | Qué guarda |
|---|---|
| `guardian_user_id`, `student_user_id` | Quién aceptó y por quién |
| `terms_version` | La versión del texto que se aceptó |
| `accepted_at` | Cuándo |

Solo se guarda eso: **no se guardan IP ni datos del navegador**. Si se borra a una persona, sus filas se borran con ella.

## Rendimiento

| Operación | Consultas |
|---|---|
| Ver el texto y los hijos (padre) | 3, sin importar cuántos hijos: vínculos, aceptaciones de todos los hijos y nombres. Una cuenta sin hijos hace solo 1 |
| Aceptar | Los vínculos (1), **una sola sentencia** que guarda a todos los hijos, y la lectura final (3) |
| Ficha del alumno (administrador) | Una consulta más, solo si la persona es alumno, en paralelo con la de las cuentas |

Aceptar es **una sentencia atómica**: `INSERT … SELECT` desde los vínculos del padre con `ON CONFLICT DO NOTHING`. Por eso nadie puede aceptar por un alumno que no es suyo (aunque lo desvinculen un instante antes) ni se duplican filas con pedidos simultáneos.

| Índice | Para qué |
|---|---|
| `ux_guardian_consents_guardian_student_version` | Una aceptación por padre, alumno y versión; hace posible el `ON CONFLICT` |
| `ix_guardian_consents_student_version` | Cómo está un alumno con la versión vigente: el aviso del panel y la ficha |

## Despliegue

- **Migración:** `AddGuardianConsents` (esquema `users`). Solo crea una tabla; no toca los datos existentes. `docker/migrate.sh` ya la aplica.
- **Permisos:** no hay permisos nuevos.
- **Antes de abrir el aula a las familias:** validar el texto legal (ver arriba).
