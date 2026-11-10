# Asignación a grupo según el diagnóstico

API de SCRUM-45. Todas las rutas empiezan por `/api/v1`, necesitan sesión y el permiso `enrollments.manage` (el administrador).

## Cómo funciona

1. El alumno rinde el diagnóstico y el profesor **confirma su nivel** (ver [DIAGNOSTICO.md](DIAGNOSTICO.md)).
2. El administrador abre la **ubicación** del alumno: los grupos de Matemáticas publicados con su cupo libre. Los del nivel confirmado salen primero, marcados como recomendados.
3. El administrador **matricula** al alumno en uno de ellos (`POST /enrollments`, como siempre) o, si ya está matriculado en otro, lo **cambia de grupo**.
4. Cada cambio de grupo queda **registrado**: quién, cuándo, de qué grupo a cuál y por qué.

El sistema recomienda, el administrador decide. Elegir un grupo que no es el recomendado está permitido; el cupo se valida igual.

## Ubicación

`GET /enrollments/placement?studentUserId=`

Devuelve:

- `diagnostics`: el diagnóstico de cada etapa, con `status` (`in_progress` o `completed`), el nivel `suggested` y el `confirmed` (null hasta que el profesor lo confirma), y su `attemptId` para abrirlo.
- `enrollments`: las matrículas vivas del alumno (pendientes y activas).
- `groups`: los grupos publicados de cursos que son un nivel de Matemáticas, con `stageLabel`, `levelLabel`, `maximumCapacity`, `takenSeats`, `freeSeats`, `recommended` y `alreadyEnrolled`.

Reglas:

- **Recomendado** es el grupo cuyo nivel y etapa coinciden con el nivel **confirmado** del diagnóstico de esa etapa. Un diagnóstico terminado pero sin confirmar muestra su nivel sugerido y **no recomienda nada**.
- Los grupos recomendados van primero; después, por etapa, nivel y nombre.
- Los grupos en borrador y los cursos que no son un nivel (laboratorios) no se ofrecen aquí. Para matricular en un laboratorio se usa `POST /enrollments`, como siempre.
- Solo para personas con rol de alumno: otra persona da 400 y una que no existe, 404.

## Cambio de grupo

`POST /enrollments/{enrollmentId}/transfer` con `{ courseOfferingId, reason }`

El `reason` es opcional (máximo 300 caracteres).

En **una sola transacción**:

1. se valida que el grupo de destino exista, esté publicado y tenga un cupo libre;
2. la matrícula actual queda **retirada**;
3. se crea la nueva en el grupo de destino con el **mismo estado**: una pendiente sigue pendiente y una activa sigue activa, con la nota de pago y quién confirmó el pago y cuándo;
4. se guarda el registro del cambio.

Si algo falla, no cambia nada.

| Situación | Respuesta |
|---|---|
| La matrícula no existe | 404 |
| El grupo de destino no existe | 404 |
| El grupo de destino está en borrador | 409 |
| El grupo de destino está lleno | 409 |
| El alumno ya tiene una matrícula en el grupo de destino | 409 |
| La matrícula ya estaba retirada (por ejemplo, otro administrador la movió un momento antes) | 409 |
| El destino es el grupo actual | 400 |
| El motivo supera los 300 caracteres | 400 |

**Pedidos simultáneos.** Los cambios de un mismo alumno se hacen uno tras otro, y los que entran al mismo grupo se serializan con las matrículas nuevas. Por eso, si seis alumnos piden pasar a la vez a un grupo con un cupo libre, entra solo uno; y si dos administradores mueven la misma matrícula, se aplica uno y el otro recibe 409. Nunca queda un alumno con dos cupos ni un grupo sobre su tope.

## Registro de cambios

`GET /enrollments/transfers?studentUserId=&courseOfferingId=&page=&pageSize=`

Del más reciente al más antiguo, paginado en la base de datos. `courseOfferingId` encuentra los cambios que salieron de ese grupo o entraron a él. Cada fila trae el alumno, los grupos de origen y destino, las dos matrículas (`fromEnrollmentId` y `toEnrollmentId`), el motivo, quién lo hizo y cuándo.

La matrícula anterior se conserva como **retirada**: así el historial del alumno (asistencia, práctica) sigue ligado al grupo donde ocurrió.

## Rendimiento

| Operación | Consultas |
|---|---|
| Ubicación | 7, sin importar cuántos grupos haya: persona, rol, diagnósticos y grupos (en paralelo, cuatro módulos), niveles de los cursos, matrículas del alumno y cupos |
| Cambio de grupo | Grupo de destino (1), la transacción del cambio y los nombres (2, en paralelo) |
| Lista de cambios | Página con su total (2) y nombres (2, en paralelo) |

Índices nuevos (tabla `enrollments.enrollment_transfers`):

| Índice | Para qué |
|---|---|
| `ix_enrollment_transfers_student_transferred_at` | Cambios de un alumno, del más reciente al más antiguo |
| `ix_enrollment_transfers_transferred_at` | La lista general, del más reciente al más antiguo |
| `ix_enrollment_transfers_from_offering` y `ix_enrollment_transfers_to_offering` | Cambios que salieron de un grupo o entraron a él |
| `ux_enrollment_transfers_from_enrollment` y `ux_enrollment_transfers_to_enrollment` | Una matrícula se mueve una sola vez y nace de un solo cambio |

## Despliegue

- **Migración:** `AddEnrollmentTransfers` (esquema `enrollments`). Solo crea una tabla; no toca las matrículas existentes. `docker/migrate.sh` ya la aplica.
- **Permisos:** no hay permisos nuevos. Usa `enrollments.manage`.
