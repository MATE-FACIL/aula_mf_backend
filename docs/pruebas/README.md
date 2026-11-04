# Pruebas del backend

Resultados del 4 de noviembre de 2026 sobre el commit `c189c57` de la rama `feat/scrum-67-68-familia`.

| Prueba | Dónde corre | Resultado |
|---|---|---|
| Unitarias y de integración (`dotnet test`) | En memoria | **165 de 165** |
| Matrícula (SCRUM-40, SCRUM-44) | API en Docker con PostgreSQL 17 | **22 de 22** |
| Grupos y horarios (SCRUM-41, SCRUM-42) | API en Docker con PostgreSQL 17 | **17 de 17** |
| Familia (SCRUM-67, SCRUM-35) | API en Docker con PostgreSQL 17 | **6 de 6** |
| Recorrido en navegador real | Aula conectada a la API en Docker | **24 de 24**, ver [pruebas del frontend](https://github.com/MATE-FACIL/aula_mf_frontend/blob/main/docs/pruebas/README.md) |

Durante el recorrido en navegador, la API no registró ningún error ni excepción.

## Pruebas por módulo

| Módulo | Pruebas |
|---|---:|
| Administration | 18 |
| Auth | 43 |
| Authorization | 12 |
| Courses | 57 |
| Enrollments | 22 |
| Users | 9 |
| SharedKernel.Storage | 4 |
| **Total** | **165** |

La lista completa está en [pruebas-unitarias.md](pruebas-unitarias.md).

## Pruebas contra la API real

Cada corrida crea sus propios datos ficticios en PostgreSQL. Esto es lo que se comprobó:

### Matrícula

- crear curso y grupo con cupo 3
- sin enrollments.manage → 403
- sin token → 401
- 10 alumnos creados
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

Para levantar la API real:

```bash
cp .env.example .env   # completa los valores
docker compose up -d --build
```

Los scripts de las pruebas contra la API todavía no están en este repositorio. Son scripts locales de Node.js, así que por ahora estos resultados no se pueden repetir solo con el repo.
