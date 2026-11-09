# Pruebas del backend

Resultados del 9 de noviembre de 2026 sobre el commit `fdba55c` de la rama `feat/scrum-53-55-asistencia-lecciones`.

| Prueba | Dónde corre | Resultado |
|---|---|---|
| Unitarias y de integración (`dotnet test`) | En memoria | **206 de 206** |
| Matrícula (SCRUM-40, SCRUM-44) | API en Docker con PostgreSQL 17 | **24 de 24** |
| Grupos y horarios (SCRUM-41, SCRUM-42) | API en Docker con PostgreSQL 17 | **17 de 17** |
| Clases del alumno (SCRUM-50, SCRUM-51) | API en Docker con PostgreSQL 17 | **11 de 11** |
| Familia (SCRUM-67, SCRUM-35) | API en Docker con PostgreSQL 17 | **6 de 6** |
| Lecciones (SCRUM-55) | API en Docker con PostgreSQL 17 | **19 de 19** |
| Asistencia (SCRUM-53) | API en Docker con PostgreSQL 17 | **14 de 14** |
| Recorrido en navegador real | Aula conectada a la API en Docker | **50 de 50**, ver [pruebas del frontend](https://github.com/MATE-FACIL/aula_mf_frontend/blob/main/docs/pruebas/README.md) |

Las pruebas contra la API también pasaron en una base nueva, preparada igual que en el CI. Durante el recorrido en navegador, la API no registró ningún error ni excepción.

## Pruebas por módulo

| Módulo | Pruebas |
|---|---:|
| Administration | 18 |
| Attendance | 21 |
| Auth | 43 |
| Authorization | 12 |
| Courses | 73 |
| Enrollments | 26 |
| Users | 9 |
| SharedKernel.Storage | 4 |
| **Total** | **206** |

La lista completa está en [pruebas-unitarias.md](pruebas-unitarias.md). Las reglas de los endpoints de lecciones y asistencia están en [LECCIONES-Y-ASISTENCIA.md](../LECCIONES-Y-ASISTENCIA.md).

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

### Familia

- el padre ve solo sus 2 hijos
- hijo activo con matrícula y 2 clases próximas (no la de 40 días)
- hijo pendiente con matrícula y sin clases
- el otro padre ve solo a su hijo
- cuenta sin hijos → lista vacía
- sin token → 401

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
```

Cada corrida deja sus datos ficticios en la base. El CI las corre en cada pull request, en el job «Container image and smoke test».
