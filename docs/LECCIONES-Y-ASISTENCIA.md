# Lecciones, archivos, niveles y asistencia

API de SCRUM-55 (lecciones), SCRUM-58 (archivos), SCRUM-39 (niveles de Matemáticas) y SCRUM-53 (asistencia). Todas las rutas empiezan por `/api/v1` y necesitan sesión.

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
- **Materiales y video:** pueden ser enlaces o archivos subidos (ver «Archivos de las lecciones»).

### Alumno

| Método y ruta | Qué hace |
|---|---|
| `GET /students/me/courses` | Cursos de sus matrículas activas en grupos publicados |
| `GET /students/me/courses/{courseId}/lessons` | Lecciones publicadas del curso; unidades sin lecciones publicadas no aparecen |

Si el alumno no tiene una matrícula activa en el curso, la respuesta es 404. No se distingue de un curso que no existe.

## Archivos de las lecciones (SCRUM-58)

El video de una lección y cada material pueden ser un enlace o un **archivo subido** al almacenamiento privado. Los archivos subidos solo los descargan el equipo y los alumnos con matrícula activa.

### Subir un archivo

Son tres pasos, y el archivo nunca pasa por la API:

1. Pedir el permiso de subida: `POST /courses/{courseId}/content/lessons/{lessonId}/uploads` con el cuerpo `{ kind, contentType, size }`, donde `kind` es `video` o `material`. La respuesta trae `fileId`, `url`, `method`, `headers` y `expiresAt`.
2. Subir el archivo directo al almacenamiento: `PUT` a esa `url` con esos `headers`, antes de que pasen 15 minutos.
3. Adjuntarlo con `PUT /courses/{courseId}/content/lessons/{lessonId}`: el material va como `{ title, fileId }` y el video como `videoFileId`.

La lección tiene que existir antes de subir sus archivos. Si al crearla se envía un `fileId`, la respuesta es 400.

### Reglas

| Tipo | Formatos | Máximo |
|---|---|---|
| Material | PDF, PNG, JPG, WebP, Word, PowerPoint, Excel | 50 MB |
| Video | MP4, WebM | 2 GB |

- **Verificación al adjuntar:** como el archivo se sube por fuera de la API, al adjuntarlo la API consulta el almacenamiento y revisa de nuevo el tipo y el tamaño reales. Si nunca se subió, o no cumple las reglas, responde 400.
- **Enlace o archivo:** un material es un enlace o un archivo, nunca ambos, y lo mismo pasa con el video. La base de datos también lo exige.
- **Limpieza:** al reemplazar un archivo o borrar la lección, los archivos que quedan sin uso se borran del almacenamiento. Si ese borrado falla, se registra en el log y la lección se guarda igual.

### Descarga

- **Quién recibe enlaces:** las respuestas de `GET /courses/{id}/content` (equipo) y `GET /students/me/courses/{id}/lessons` (alumno) traen enlaces firmados que vencen en **15 minutos**, en `url` para los materiales y en `videoUrl` para el video. Sin matrícula activa la respuesta es 404, así que no hay enlace.
- **Firma local:** firmar un enlace es un cálculo local, sin llamada de red, así que firmar todos los archivos de un curso es barato.
- **Sin almacenamiento:** si el ambiente no tiene almacenamiento, las subidas responden 503 y los archivos aparecen con `url: null`.

## Niveles de Matemáticas (SCRUM-39)

Hay cuatro niveles por etapa (Prebásico, Básico, Intermedio y Avanzado), para primaria y secundaria. Cada nivel es un **curso normal**, así que grupos, lecciones, matrícula y asistencia funcionan igual que en cualquier otro. Una tabla aparte solo registra qué curso corresponde a cada nivel.

Requiere el permiso `courses.manage`.

| Método y ruta | Qué hace |
|---|---|
| `GET /math-levels` | Los 8 niveles, en orden, con su curso (o `null` si falta), sus sesiones y cuántas están publicadas |
| `POST /math-levels/provision` | Crea los niveles que faltan y devuelve `{ created, levels }` |

Cada nivel creado es:

- un curso en borrador, por ejemplo «Matemáticas · Primaria Prebásico»;
- en la categoría «Matemáticas»;
- con clases de 60 minutos en primaria y de 90 en secundaria;
- con una unidad «Sesiones» y 14 lecciones en borrador («Sesión 1» a «Sesión 14»), que el equipo académico llena después (SCRUM-74).

Crear niveles se puede repetir sin riesgo: los niveles que ya existen y su contenido no se tocan. Si dos personas lo ejecutan a la vez, la base impide duplicados y una de las dos recibe 409 sin que se guarde nada a medias.

`GET /students/me/courses` también devuelve el `level` de cada curso que sea un nivel, para la pantalla «Mi ruta» y el diagnóstico.

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
| Curso completo | 4: curso, unidades, lecciones y materiales; después, la firma local de los archivos |
| Lista de niveles | 2: niveles con su curso y conteo de lecciones agrupado |
| Adjuntar archivos | Una consulta al almacenamiento por archivo nuevo, todas en paralelo |
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

- **Migraciones:** `AddCourseContent` y `AddLessonFilesAndMathLevels` (cursos) e `InitialAttendance` (esquema `attendance`). `docker/migrate.sh` y el CI ya las incluyen.
- **Almacenamiento:** en R2, agrega los tipos de archivo de las lecciones a `Storage__R2__AllowedContentTypes`, permite 2 GB en `Storage__R2__MaxDirectUploadBytes` y configura CORS en el bucket privado (ver el README).
- **Permisos:** hay que volver a provisionar el catálogo para crear `attendance.manage`. En local: `docker compose --profile tools run --rm provision`.
