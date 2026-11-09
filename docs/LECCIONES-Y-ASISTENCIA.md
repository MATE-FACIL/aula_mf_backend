# Lecciones y asistencia

API de SCRUM-55 (lecciones) y SCRUM-53 (asistencia). Todas las rutas empiezan por `/api/v1` y necesitan sesión.

## Lecciones (SCRUM-55)

Un curso tiene **unidades** y cada unidad tiene **lecciones** en orden. Cada lección tiene título, resumen, un video y hasta 20 materiales. El número de una lección es su lugar en todo el curso: 1, 2, 3… recorriendo las unidades en orden.

### Administración

Requiere el permiso `courses.manage`.

| Método y ruta | Qué hace |
|---|---|
| `GET /courses/{courseId}/content` | El curso completo, borradores incluidos |
| `POST /courses/{courseId}/content/units` | Crea una unidad al final. Cuerpo: `{ title }` |
| `PUT /courses/{courseId}/content/units/{unitId}` | Cambia el título |
| `DELETE /courses/{courseId}/content/units/{unitId}` | Borra una unidad vacía; con lecciones responde 409 |
| `PUT /courses/{courseId}/content/units/order` | Nuevo orden. Cuerpo: `{ ids }` con todas las unidades una vez |
| `POST /courses/{courseId}/content/units/{unitId}/lessons` | Crea una lección al final de la unidad |
| `PUT /courses/{courseId}/content/lessons/{lessonId}` | Reemplaza la lección; con otro `unitId` la mueve al final de esa unidad |
| `DELETE /courses/{courseId}/content/lessons/{lessonId}` | Borra la lección y sus materiales |
| `PUT /courses/{courseId}/content/units/{unitId}/lessons/order` | Nuevo orden de las lecciones de la unidad |

Este es el cuerpo de una lección:

```json
{
  "unitId": "…solo al editar…",
  "title": "Sumar fracciones",
  "summary": "Con distinto denominador",
  "videoUrl": "https://www.youtube.com/watch?v=…",
  "isPublished": true,
  "materials": [{ "title": "Guía de práctica", "url": "https://…/guia.pdf" }]
}
```

### Reglas

- **Enlaces:** el video y los materiales deben ser enlaces `https://`. Un enlace `http://` no se aceptaría, porque el navegador lo bloquea en el aula.
- **Límites:** 30 unidades por curso, 200 lecciones por curso y 20 materiales por lección.
- **Borradores:** una lección no publicada solo la ve el equipo.
- **Errores:** las respuestas 400 traen un mensaje listo para mostrar, por ejemplo «El video debe ser un enlace https:// válido.».
- **Práctica:** la práctica de una lección llega con SCRUM-59 y apunta al id de la lección.
- **Materiales:** por ahora son enlaces. La subida de archivos propios llega con SCRUM-58.

### Alumno

| Método y ruta | Qué hace |
|---|---|
| `GET /students/me/courses` | Cursos de sus matrículas activas en grupos publicados |
| `GET /students/me/courses/{courseId}/lessons` | Lecciones publicadas del curso; unidades sin lecciones publicadas no aparecen |

Si el alumno no tiene una matrícula activa en el curso, la respuesta es 404. No se distingue de un curso que no existe.

## Asistencia (SCRUM-53)

Hay un registro por clase y alumno, con estado `present`, `late` o `absent` y una nota opcional de hasta 200 caracteres. Una corrección cambia ese mismo registro y guarda quién la hizo.

### Tomar asistencia

| Método y ruta | Qué hace |
|---|---|
| `GET /attendance/sessions/{sessionId}` | Lista de la clase: alumnos con matrícula activa y su marca (`null` = sin marcar) |
| `PUT /attendance/sessions/{sessionId}` | Guarda varias marcas en un envío. Cuerpo: `{ records: [{ studentUserId, status, note }] }` |

Quién puede tomarla:

- El **equipo**, con el permiso `attendance.manage` (catálogo de permisos v4).
- El **profesor asignado** al grupo de esa clase, sin necesitar ese permiso.

Cualquier otra cuenta recibe 403.

### Reglas

- **Grupos publicados:** una clase de un grupo en borrador responde 404.
- **Cuándo se abre:** desde 15 minutos antes de la clase; antes responde 409. La respuesta trae `opensAt` y `canMark`.
- **A quién se marca:** solo a alumnos con matrícula **activa**; otro alumno responde 400. Un alumno que dejó el grupo conserva su historial y aparece con `enrolled: false`.
- **Tamaño del envío:** hasta 100 alumnos, cada uno una sola vez.
- **Guardados simultáneos:** si dos personas guardan la misma clase a la vez, un índice único impide duplicados y el segundo guardado se reintenta una vez. Si vuelve a chocar, responde 409.

### Alumno y familia

| Método y ruta | Qué hace |
|---|---|
| `GET /students/me/attendance` | Totales de todas sus clases y sus 30 clases más recientes |
| `GET /families/me/attendance` | Lo mismo para cada hijo vinculado, con 10 clases recientes por hijo |

La tasa (`rate`) es el porcentaje de clases asistidas, y llegar tarde cuenta como asistencia. Cuando no hay clases marcadas, vale `null`.

Ninguna de las dos rutas recibe un id de alumno: parten de la cuenta de la sesión, así que nadie puede pedir la asistencia de otro niño.

## Rendimiento

Cada lectura tiene un número fijo de consultas, sin importar cuántas unidades, lecciones, alumnos o hijos haya. Ninguna consulta se repite por cada elemento.

| Operación | Consultas |
|---|---|
| Curso completo | 4: curso, unidades, lecciones y materiales |
| Lecciones del alumno | 2 de matrícula + 4 del curso |
| Lista de una clase | Clase, profesores y permiso en paralelo; luego alumnos y marcas en paralelo; luego nombres |
| Asistencia de la familia | Vínculos, totales agrupados, últimas clases (como máximo 500 filas de los últimos 120 días) y nombres de los grupos |

Estos son los índices que sostienen esas consultas:

| Índice | Para qué |
|---|---|
| `ix_lessons_course_published` | Leer el curso completo |
| `ix_lessons_unit_position` | Ordenar y agregar lecciones en una unidad |
| `ux_attendance_records_session_student` | Una marca por clase y alumno; también sirve la lista de una clase |
| `ix_attendance_records_student_starts_at` | Historial del alumno, de la más reciente a la más antigua, y sus totales |

La fecha de la clase se copia en cada registro de asistencia. Así el historial se ordena sin consultar cursos: el horario de un grupo publicado no puede cambiar, por lo que la copia no se desactualiza.

## Despliegue

- **Migraciones:** `AddCourseContent` (cursos) e `InitialAttendance` (esquema `attendance`). `docker/migrate.sh` y el CI ya las incluyen.
- **Permisos:** hay que volver a provisionar el catálogo para crear `attendance.manage`. En local: `docker compose --profile tools run --rm provision`.
