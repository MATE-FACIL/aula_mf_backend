# Pruebas del backend

Resultados del 13 de noviembre de 2026 sobre la rama `feature/scrum-38-consentimiento`, con el consentimiento del padre (SCRUM-38), la asignación a grupo (SCRUM-45) y el diagnóstico de entrada (SCRUM-47, SCRUM-48).

| Prueba | Dónde corre | Resultado |
|---|---|---|
| Unitarias y de integración (`dotnet test`) | En memoria | **314 de 314** |
| Matrícula (SCRUM-40, SCRUM-44) | API en Docker con PostgreSQL 17 | **24 de 24** |
| Grupos y horarios (SCRUM-41, SCRUM-42) | API en Docker con PostgreSQL 17 | **17 de 17** |
| Clases del alumno (SCRUM-50, SCRUM-51) | API en Docker con PostgreSQL 17 | **11 de 11** |
| Familia (SCRUM-67, SCRUM-35) | API en Docker con PostgreSQL 17 | **6 de 6** |
| Lecciones (SCRUM-55) | API en Docker con PostgreSQL 17 | **19 de 19** |
| Asistencia (SCRUM-53) | API en Docker con PostgreSQL 17 | **14 de 14** |
| Niveles de Matemáticas (SCRUM-39) | API en Docker con PostgreSQL 17 | **9 de 9** |
| Archivos de las lecciones (SCRUM-58) | API en Docker con almacenamiento S3 local (RustFS) | **16 de 16** |
| Práctica (SCRUM-59, SCRUM-60) | API en Docker con PostgreSQL 17 | **16 de 16** |
| Panel del profesor (SCRUM-69) | API en Docker con PostgreSQL 17 | **8 de 8** |
| Diagnóstico de entrada (SCRUM-47, SCRUM-48) | API en Docker con PostgreSQL 17 | **22 de 22** |
| Asignación a grupo y cambio de grupo (SCRUM-45), con pedidos simultáneos | API en Docker con PostgreSQL 17 | **32 de 32** |
| Consentimiento del padre (SCRUM-38), con pedidos simultáneos | API en Docker con PostgreSQL 17 | **31 de 31** |
| Recorridos en navegador real | Aula conectada a la API en Docker | Ver las [pruebas del frontend](https://github.com/MATE-FACIL/aula_mf_frontend/blob/main/docs/pruebas/README.md) |

Las pruebas contra la API también pasaron en una base nueva, preparada igual que en el CI. Esa base no tiene almacenamiento, así que la suite de archivos se omite allí; la de archivos corre con `docker-compose.storage.yml`. Durante el recorrido en navegador, la API no registró ningún error ni excepción.

## Pruebas por módulo

| Módulo | Pruebas |
|---|---:|
| Administration | 20 |
| Attendance | 23 |
| Auth | 43 |
| Authorization | 12 |
| Courses | 96 |
| Enrollments | 42 |
| Exams | 47 |
| Users | 25 |
| SharedKernel.Storage | 6 |
| **Total** | **314** |

La lista completa está en [pruebas-unitarias.md](pruebas-unitarias.md). Las reglas de los endpoints de lecciones, archivos, niveles, práctica, asistencia y panel del profesor están en [LECCIONES-Y-ASISTENCIA.md](../LECCIONES-Y-ASISTENCIA.md); las del diagnóstico, en [DIAGNOSTICO.md](../DIAGNOSTICO.md); las de la asignación a grupo, en [ASIGNACION-DE-GRUPO.md](../ASIGNACION-DE-GRUPO.md); las del consentimiento del padre, en [CONSENTIMIENTO.md](../CONSENTIMIENTO.md).

## Pruebas contra la API real

Cada corrida crea sus propios datos ficticios en PostgreSQL. Esto es lo que se comprobó:

### Matrícula

- crear curso y grupo con cupo 3
- sin enrollments.manage → 403
- sin token → 401
- 10 alumnos creados
- grupo en borrador → 409
- publicar el grupo
- 10 altas simultáneas: exactamente 3 entran
- las otras 7 → 409 «grupo lleno»
- cupos del grupo: 3 de 3, 0 libres
- 4 altas simultáneas del mismo alumno: 1 entra, 3 «ya matriculado»
- matricular a quien no es alumno → 400
- grupo inexistente → 404
- nota de pago larga → 400
- alta queda pendiente con quién la registró
- activar registra quién, cuándo y nota
- activar dos veces → 400
- retirar → withdrawn
- retirar dos veces → 400
- el cupo liberado se puede usar
- el retirado no vuelve con el grupo lleno → 409
- filtrar por grupo y retiradas
- filtrar por alumno con nombres de curso y grupo
- estado desconocido → 400
- paginación

### Grupos y horarios

- curso con duración de clase 90 min
- duración fuera de rango → 400
- duración se puede quitar
- crear profesor (persona + cuenta + rol + profesor)
- correo repetido → 409
- lista de profesores con nombre
- el profesor tiene rol instructor
- crear grupo en borrador con 2 clases
- publicar sin profesor → 409
- editar borrador conserva la sesión con id
- asignar profesor
- publicar grupo completo
- publicado: no se edita el horario → 409
- publicado: sí se edita el enlace de una clase
- lista de grupos del curso (consultas separadas por colección)
- matricular en el grupo publicado
- cupos del grupo (1 de 8, publicado)

### Clases del alumno

- grupo publicado
- login real del alumno + cookie de renovación
- matrícula PENDIENTE: todavía sin clases
- matrícula ACTIVA: 2 clases en el rango por defecto (no la de 60 días)
- clase con curso, grupo, categoría, profesor y enlace
- clase sin enlace llega con virtualLink null
- ordenadas por inicio
- rango explícito de 70 días incluye la clase lejana
- rango invertido → 400
- admin sin matrículas → lista vacía (sin errores)
- sin token → 401

### Lecciones

- crear unidad
- la segunda unidad va después
- lección con video y 2 materiales en orden
- las lecciones se numeran por unidad y luego por orden
- video que no es https → 400 con mensaje claro
- más de 20 materiales → 400
- reordenar unidades renumera las lecciones
- un orden incompleto → 400
- mover una lección a otra unidad la pone al final
- borrar una unidad con lecciones → 409
- una unidad de otro curso → 404
- el alumno ve su curso
- ve solo las lecciones publicadas, numeradas sin huecos
- no ve borradores
- la lección trae video y materiales
- matrícula pendiente → 404
- un curso en el que no está → 404
- el alumno no puede administrar contenido → 403
- sin token → 401

### Asistencia

- la profesora ve la lista: solo matrículas activas, sin marcar
- un profesor de otro grupo → 403
- el padre no puede marcar → 403
- sin token → 401
- la profesora marca toda la lista en una petición
- a una matrícula pendiente → 400
- estado desconocido → 400
- una clase que todavía no empieza → 409
- una clase de un grupo en borrador → 404
- el equipo corrige la asistencia
- 4 guardados simultáneos: sin errores y una sola fila por alumno
- la alumna ve su asistencia con el curso
- la familia ve solo a su hija
- otra familia no ve nada

### Niveles de Matemáticas

- hay 8 niveles: 4 de primaria y 4 de secundaria
- crear los niveles que faltan
- repetir no crea nada nuevo
- cada nivel tiene al menos sus 14 sesiones
- nombres claros por etapa y nivel
- primaria 60 min y secundaria 90 min por clase
- las sesiones están en orden y numeradas
- el alumno ve su nivel de Matemáticas
- el alumno no administra niveles → 403

### Archivos de las lecciones

Al terminar, la suite borra todo lo que subió (pase o falle) y comprueba que el almacenamiento quedó vacío.

- permiso de subida para un PDF
- la subida va directo al almacenamiento
- tipo no permitido → 400
- material de más de 50 MB → 400
- video de más de 2 GB → 400
- un archivo que nunca se subió → 400
- la lección guarda video, archivo y enlace
- el alumno matriculado recibe enlaces firmados que vencen en 15 min
- el enlace firmado descarga el archivo
- sin matrícula activa no hay enlaces → 404
- el archivo sin firma no se puede descargar
- reemplazar el archivo
- el archivo reemplazado ya no existe
- borrar la lección elimina sus archivos
- la API no dejó archivos de la prueba en el almacenamiento
- al terminar no queda ningún archivo de la prueba

### Práctica

- el equipo carga ejercicios de opción múltiple y numéricos
- otra lección con su ejercicio
- un ejercicio sin respuesta correcta → 400
- una lección de otro curso → 404
- el alumno ve los ejercicios sin la respuesta ni la explicación
- matrícula pendiente → 404
- lección en borrador → 404
- el alumno no puede ver la versión del equipo → 403
- respuesta incorrecta: se corrige y llega la explicación
- respuesta correcta: avance de la lección 50 %
- numérica con coma decimal
- una respuesta ilegible → 400 y no se guarda
- sin matrícula activa no se puede responder → 404
- avance del nivel: 2 de 3 ejercicios (67 %), por lección
- historial: 3 intentos, del más reciente al más antiguo
- quitar un ejercicio lo archiva: el avance se recalcula y el historial sigue

### Panel del profesor

- la profesora ve solo su grupo, con alumnos activos y la próxima clase
- el grupo de otro profesor → 404
- una cuenta que no enseña ve una lista vacía
- sin token → 401
- marcar asistencia en 1 clic: los que faltaban quedan presentes y no se pisa lo marcado
- otro profesor no puede marcar ese grupo → 403
- el detalle trae sus clases con la asistencia contada
- y sus alumnos con asistencia y avance de práctica

### Familia

- el padre ve solo sus 2 hijos
- hijo activo con matrícula y 2 clases próximas (no la de 40 días)
- hijo pendiente con matrícula y sin clases
- el otro padre ve solo a su hijo
- cuenta sin hijos → lista vacía
- sin token → 401


### Diagnóstico de entrada

- el equipo carga el banco con nivel, tema y respuesta
- un nivel desconocido → 400 con el número de pregunta
- una etapa desconocida → 404
- resumen: primaria disponible y sin empezar
- el alumno empieza y ve las preguntas sin la respuesta ni la explicación
- empezar otra vez devuelve el mismo diagnóstico
- un profesor no puede rendirlo → 404
- quitar preguntas del banco no cambia el diagnóstico empezado
- una respuesta ilegible → 400 y no se guarda
- sin decir si está bien o mal: solo el avance
- cada pregunta se responde una vez → 409
- la última respuesta («No lo sé») termina y sugiere Básico
- resultado por tema: Conteo 100 %, Fracciones 0 %
- terminado: empezar otra vez devuelve el resultado
- el profesor ve el diagnóstico pendiente con el nombre del alumno
- detalle: cada respuesta, la correcta y la explicación
- el profesor confirma otro nivel
- un nivel desconocido → 400
- el alumno ve el nivel confirmado
- un alumno no puede ver la revisión → 403
- un estado desconocido → 400
- el banco de preguntas queda como estaba

### Asignación a grupo y cambio de grupo

- sin diagnóstico: no se recomienda ningún grupo y se ven sus plazas
- cada grupo trae su nivel, su cupo y si ya está matriculado
- los grupos en borrador no se ofrecen
- terminado sin confirmar: se muestra el nivel sugerido pero no se recomienda nada
- confirmado: se recomiendan los grupos de primaria Básico
- los recomendados van primero
- solo se ubica a alumnos → 400
- una persona que no existe → 404
- un alumno no puede usar la ubicación → 403
- el administrador la pasa al grupo recomendado
- el cupo de A se libera y el de B se ocupa
- la matrícula anterior queda retirada y la nueva pendiente
- el cambio queda registrado con quién, cuándo y por qué
- se puede buscar por el grupo al que entró o del que salió
- la matrícula ya retirada no se puede mover → 409
- al mismo grupo → 400
- a un grupo en borrador → 409
- a un grupo que no existe → 404
- una matrícula que no existe → 404
- un motivo demasiado largo → 400
- un alumno no puede cambiar grupos → 403
- al grupo lleno → 409 y nada cambia
- Ana sigue en B y C sigue con 1 de 1
- matricular a quien ya está en el grupo sigue dando 409
- una matrícula activa sigue activa y conserva el pago confirmado
- Ana tiene un solo cupo vivo
- 6 cambios simultáneos a un grupo de 1 cupo: entra solo 1
- los otros 5 siguen en A, sin perder su cupo
- 4 cambios simultáneos de la misma matrícula: se aplica uno y queda un solo cupo
- y se registra un solo cambio
- el banco de preguntas queda como estaba


### Consentimiento del padre

- el padre recibe el texto vigente, con versión y secciones
- ve a sus 3 hijos pendientes, por orden alfabético, y no al de otro padre
- un padre sin hijos recibe el texto y una lista vacía
- sin token → 401
- ficha del alumno con padre vinculado: pendiente, con la versión vigente
- ficha del alumno sin padre: «sin padre» (nadie puede dar el consentimiento)
- ficha del padre: no tiene consentimiento
- la madre acepta por Ana: queda la fecha y quién
- Beto sigue pendiente (solo se aceptó por Ana)
- el administrador ve en la ficha: aceptado, cuándo y por quién
- basta con un padre: el otro padre ve a Ana ya aceptada
- aceptar otra vez: nada cambia (la fecha sigue siendo la primera)
- 8 aceptaciones a la vez: todas responden bien y no hay error por duplicado
- quedan los 3 hijos aceptados
- en la base hay exactamente una aceptación por hijo (3 filas), sin duplicados
- todas las respuestas ven la misma fecha: se guardó una sola aceptación
- aceptar por el hijo de otro padre → 404
- y no se guardó nada para ese hijo
- todo o nada: con un hijo ajeno en la lista no se guarda ninguno → 404
- el hijo propio de esa lista sigue pendiente
- un texto que cambió mientras lo leía → 409 con indicación de recargar
- sin versión → 409
- sin hijos elegidos → 400
- demasiados hijos a la vez → 400
- un padre que no es de Ana no puede aceptar por ella → 404
- un alumno no puede aceptar por sí mismo → 404
- aceptar sin token → 401
- si se desvincula a quien aceptó, el alumno queda «sin padre»
- con otro padre vinculado, el consentimiento de la madre no vale: pendiente
- al volver a vincular a la madre, su aceptación vuelve a contar
- el administrador no puede aceptar por una familia (no tiene hijos vinculados)

## Cómo repetirlas

Las pruebas de `dotnet test` no necesitan Docker:

```bash
dotnet test
```

Para correr las pruebas contra la API real (necesitan Docker y Node.js 20 o superior):

```bash
cp .env.example .env          # completa los valores
docker compose up -d --build
sh tests/e2e/setup-admin.sh   # solo en una base nueva
node tests/e2e/run.mjs

# Con almacenamiento local, para que también corra la suite de archivos:
docker compose -f docker-compose.yml -f docker-compose.storage.yml up -d --build
```

Cada corrida deja sus datos ficticios en la base. El CI las corre en cada pull request, en el job «Container image and smoke test».
