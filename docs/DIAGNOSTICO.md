# Diagnóstico de entrada

API de SCRUM-47 (banco de preguntas) y SCRUM-48 (diagnóstico con resultado por tema y nivel sugerido). La pantalla del alumno es SCRUM-49. Todas las rutas empiezan por `/api/v1` y necesitan sesión.

## Cómo funciona

1. El equipo carga un **banco de preguntas por etapa** (primaria y secundaria). Cada pregunta mide un **nivel** (Prebásico, Básico, Intermedio o Avanzado) y tiene un **tema** («Fracciones», «Geometría»…).
2. El alumno elige la etapa y rinde el diagnóstico: una pregunta a la vez. Puede responder o elegir «No lo sé», que cuenta como incorrecta. **No se le dice si acertó**: es una prueba de ubicación, no una práctica.
3. Al responder la última pregunta, el diagnóstico se califica:
   - **Resultado por tema:** aciertos sobre preguntas de cada tema, en el orden en que aparecen.
   - **Nivel sugerido:** el primer nivel que el alumno **todavía no domina**. Un nivel se domina con el **60 %** de sus preguntas bien. Si domina todos, se sugiere el más alto medido.
4. El profesor revisa cada respuesta y **confirma** el nivel sugerido o elige otro. Puede cambiarlo después.

Ejemplo: Prebásico 100 %, Básico 100 %, Intermedio 50 % → se sugiere **Intermedio**.

## Equipo: banco de preguntas

Requiere el permiso `courses.manage`. `{stage}` es `primary` o `secondary`.

| Método y ruta | Qué hace |
|---|---|
| `GET /diagnostics/questions/{stage}` | Las preguntas activas, en orden, con su respuesta y su explicación |
| `PUT /diagnostics/questions/{stage}` | Reemplaza la lista ordenada. Cuerpo: `{ questions: [{ id?, level, topic, kind, prompt, options, correctOption, correctNumber, tolerance, explanation }] }` |

- `level`: `prebasic`, `basic`, `intermediate` o `advanced`.
- `kind`, `options`, `correctOption`, `correctNumber` y `tolerance` funcionan igual que en los ejercicios de práctica.
- Máximo 60 preguntas por etapa. Si una falla, el error dice cuál («Pregunta 3: …»).
- Una pregunta que falta en la lista se **archiva**: los diagnósticos ya empezados la conservan.

## Alumno

| Método y ruta | Qué hace |
|---|---|
| `GET /students/me/diagnostics` | Las dos etapas: si hay diagnóstico disponible y cómo va el del alumno, con su resultado si terminó |
| `POST /students/me/diagnostics` | Cuerpo: `{ stage }`. Empieza el diagnóstico de esa etapa, o devuelve el que ya existe |
| `GET /students/me/diagnostics/{attemptId}` | Las preguntas **sin** respuesta ni explicación, cuáles ya respondió y, si terminó, el resultado |
| `POST /students/me/diagnostics/{attemptId}/answers` | Cuerpo: `{ questionId, answer, skip }`. Responde `{ answered, total, completed, result }` |

Reglas:

- Solo las cuentas con rol `student` pueden empezar un diagnóstico.
- **Uno por alumno y etapa.** Al terminar, empezar otra vez devuelve el resultado.
- Las preguntas se copian al empezar: editar el banco después no cambia un diagnóstico en curso.
- Cada pregunta se responde una sola vez (409 si se repite). Una respuesta que no se puede leer da 400 y no se guarda.
- `skip: true` es «No lo sé»: cuenta como incorrecta.

## Profesor: revisión y confirmación

Requiere el permiso `diagnostics.review`. Lo tienen los **administradores y los profesores**. Un administrador puede quitárselo a los profesores; volver a provisionar el catálogo no se lo devuelve.

| Método y ruta | Qué hace |
|---|---|
| `GET /diagnostics?status=&stage=&page=&pageSize=` | Lista paginada, del más reciente al más antiguo. `status`: `pending` (por defecto: terminados sin confirmar), `confirmed`, `in_progress` o `all` |
| `GET /diagnostics/{attemptId}` | Resultado, y cada pregunta con lo que respondió el alumno, la respuesta correcta y la explicación |
| `POST /diagnostics/{attemptId}/confirm` | Cuerpo: `{ level }`. Confirma el nivel. Solo con el diagnóstico terminado (409 si no) |

## Rendimiento

| Operación | Consultas |
|---|---|
| Resumen del alumno | Sus diagnósticos (1), preguntas activas por etapa agrupadas (1) y sus respuestas (1) |
| Ver un diagnóstico | Diagnóstico (1), sus preguntas (1) y sus respuestas (1) |
| Responder | Diagnóstico (1), pregunta (1), guardar (1) y respuestas (1); al terminar, una actualización más |
| Lista del profesor | Página con su total (2), respuestas por diagnóstico agrupadas (1) y nombres (1) |
| Detalle del profesor | Diagnóstico (1), preguntas (1), respuestas (1) y nombres (1) |

Índices:

| Índice | Para qué |
|---|---|
| `ix_diagnostic_questions_stage_active_position` | Preguntas activas de una etapa, en orden, y su conteo |
| `ux_diagnostic_attempts_student_stage` | Un diagnóstico por alumno y etapa; también evita empezar dos veces desde dos pestañas |
| `ix_diagnostic_attempts_status_started_at` | Lista del profesor por estado, del más reciente al más antiguo |
| `ux_diagnostic_answers_attempt_question` | Una respuesta por pregunta; también lee y cuenta las respuestas de un diagnóstico |

## Despliegue

- **Migración:** `AddDiagnostics` (esquema `exams`). `docker/migrate.sh` ya la aplica.
- **Permisos:** hay que volver a provisionar el catálogo (v5) para crear `diagnostics.review`. En local: `docker compose --profile tools build provision` y luego `docker compose --profile tools run --rm provision`.
- **Contenido:** el banco de preguntas (SCRUM-47) lo carga el equipo académico. Sin preguntas, el alumno ve que el diagnóstico aún no está disponible.
