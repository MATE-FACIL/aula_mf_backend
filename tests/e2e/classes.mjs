// Clases del alumno (SCRUM-50, SCRUM-51): GET /students/me/classes con matrícula pendiente y activa.
import { admin, check, finish, login, request as call, tag } from './lib.mjs';

const category = (await call('POST', '/course-categories', { name: `Matemáticas ${tag}` })).json;
const course = (await call('POST', '/courses', { title: `Álgebra ${tag}`, categoryId: category.id, sessionDurationMinutes: 90 })).json;
const teacher = (await call('POST', '/admin/instructors', { givenNames: 'Lucía', firstSurname: `Paredes${tag}`, email: `lucia.${tag}@example.com` })).json;

// Clase 1: empieza en 5 min (ventana «Entrar» abierta) con enlace. Clase 2: en 2 días SIN enlace. Clase 3: fuera de rango (60 días).
const at = (ms) => new Date(Math.floor((Date.now() + ms) / 60000) * 60000);
const s = (start, link) => ({ id: null, startsAt: start.toISOString(), endsAt: new Date(start.getTime() + 90 * 60000).toISOString(), virtualLink: link });
const sessions = [s(at(5 * 60000), 'https://meet.example.com/algebra'), s(at(2 * 86400000), null), s(at(60 * 86400000), null)];
const group = (await call('POST', `/courses/${course.id}/offerings`, { name: `Grupo Lun-Mié ${tag}`, maximumCapacity: 8, basePrice: 180, currencyCode: 'PEN', plannedSessionCount: 3, sessions })).json;
await call('PUT', `/courses/${course.id}/offerings/${group.id}/instructors`, { instructorIds: [teacher.instructorId] });
check('grupo publicado', (await call('POST', `/courses/${course.id}/offerings/${group.id}/publish`)).json?.status === 'published');

const student = (await call('POST', '/admin/students', { givenNames: 'Ana', firstSurname: `Clases${tag}`, documentType: 'DNI', documentNumber: `4${tag}` })).json;
const enrollment = (await call('POST', '/enrollments', { studentUserId: student.userId, courseOfferingId: group.id })).json;

// Login real del alumno (para tener su JWT y su cookie de renovación)
const session = await login('student', { username: student.loginIdentifier, password: student.initialPassword });
const studentToken = session.token;
check('login real del alumno + cookie de renovación', session.status === 200 && session.cookies.some((c) => c.startsWith('aula_virtual_refresh=')), session.status);

let r = await call('GET', '/students/me/classes', undefined, studentToken);
check('matrícula PENDIENTE: todavía sin clases', r.status === 200 && r.json.length === 0, r.json);
await call('POST', `/enrollments/${enrollment.id}/activate`, { paymentNote: 'Yape e2e' });

r = await call('GET', '/students/me/classes', undefined, studentToken);
check('matrícula ACTIVA: 2 clases en el rango por defecto (no la de 60 días)', r.status === 200 && r.json.length === 2, r.json);
check('clase con curso, grupo, categoría, profesor y enlace', r.json[0]?.courseTitle === `Álgebra ${tag}` && r.json[0].categoryName === `Matemáticas ${tag}` && r.json[0].teachers[0] === `Lucía Paredes${tag}` && r.json[0].virtualLink === 'https://meet.example.com/algebra', r.json[0]);
check('clase sin enlace llega con virtualLink null', r.json[1]?.virtualLink === null, r.json[1]);
check('ordenadas por inicio', Date.parse(r.json[0].startsAt) < Date.parse(r.json[1].startsAt));

const from = new Date(Date.now() - 86400000).toISOString(), to = new Date(Date.now() + 70 * 86400000).toISOString();
r = await call('GET', `/students/me/classes?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`, undefined, studentToken);
check('rango explícito de 70 días incluye la clase lejana', r.json?.length === 3, r.json?.length);
r = await call('GET', `/students/me/classes?from=${encodeURIComponent(to)}&to=${encodeURIComponent(from)}`, undefined, studentToken);
check('rango invertido → 400', r.status === 400, r.status);
r = await call('GET', '/students/me/classes', undefined, admin);
check('admin sin matrículas → lista vacía (sin errores)', r.status === 200 && r.json.length === 0, r);
r = await call('GET', '/students/me/classes', undefined, null);
check('sin token → 401', r.status === 401, r.status);

finish();
