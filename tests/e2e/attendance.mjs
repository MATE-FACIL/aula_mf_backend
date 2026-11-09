// Asistencia (SCRUM-53): quién puede marcarla, cuándo, a quién, correcciones, concurrencia y lo que ven
// el alumno y la familia.
import { check, finish, login, request as call, tag } from './lib.mjs';

const course = (await call('POST', '/courses', { title: `Asistencia ${tag}` })).json;
const teacherOf = async (name, email) => {
  const created = (await call('POST', '/admin/instructors', { givenNames: name, firstSurname: `Prof${tag}`, email })).json;
  return { ...created, token: (await login('admin', { email: created.loginIdentifier, password: created.initialPassword })).token };
};
const lucia = await teacherOf('Lucía', `lucia.asis.${tag}@example.com`);
const diego = await teacherOf('Diego', `diego.asis.${tag}@example.com`);

const at = (minutes) => new Date(Math.floor((Date.now() + minutes * 60000) / 60000) * 60000);
const session = (minutes) => ({ id: null, startsAt: at(minutes).toISOString(), endsAt: new Date(at(minutes).getTime() + 3600000).toISOString(), virtualLink: null });
const group = (await call('POST', `/courses/${course.id}/offerings`, {
  name: 'Grupo A', maximumCapacity: 8, basePrice: 100, currencyCode: 'PEN', plannedSessionCount: 2, sessions: [session(5), session(2880)],
})).json;
await call('PUT', `/courses/${course.id}/offerings/${group.id}/instructors`, { instructorIds: [lucia.instructorId] });
const published = (await call('POST', `/courses/${course.id}/offerings/${group.id}/publish`)).json;
const [today, later] = [...published.sessions].sort((a, b) => a.startsAt.localeCompare(b.startsAt)).map((s) => s.id);
const draft = (await call('POST', `/courses/${course.id}/offerings`, {
  name: 'Grupo borrador', maximumCapacity: 8, basePrice: 100, currencyCode: 'PEN', plannedSessionCount: 1, sessions: [session(5)],
})).json;

const parent = (await call('POST', '/admin/guardians', { givenNames: 'Rosa', firstSurname: `Asistencia${tag}`, email: `rosa.asis.${tag}@example.com` })).json;
const student = async (name, doc, guardianUserId) => (await call('POST', '/admin/students', {
  givenNames: name, firstSurname: `Asis${tag}`, documentType: 'DNI', documentNumber: doc, ...(guardianUserId && { guardianUserId }),
})).json;
const ana = await student('Ana', `8${tag}1`, parent.userId);
const luis = await student('Luis', `8${tag}2`);
const carla = await student('Carla', `8${tag}3`);
const enroll = async (who, activate) => {
  const seat = (await call('POST', '/enrollments', { studentUserId: who.userId, courseOfferingId: group.id })).json;
  if (activate) await call('POST', `/enrollments/${seat.id}/activate`, {});
};
await enroll(ana, true);
await enroll(luis, true);
await enroll(carla, false);

const url = (id) => `/attendance/sessions/${id}`;
const mark = (id, records, token) => call('PUT', url(id), { records }, token);

// Lista de la clase
let r = await call('GET', url(today), undefined, lucia.token);
check('la profesora ve la lista: solo matrículas activas, sin marcar', r.status === 200 && r.json.students.length === 2 &&
  r.json.students.every((s) => s.status === null && s.enrolled) && r.json.canMark === true, r.json);

// Quién puede marcar
r = await mark(today, [{ studentUserId: ana.userId, status: 'present' }], diego.token);
check('un profesor de otro grupo → 403', r.status === 403, r.status);
const parentToken = (await login('admin', { email: parent.loginIdentifier, password: parent.initialPassword })).token;
r = await mark(today, [{ studentUserId: ana.userId, status: 'present' }], parentToken);
check('el padre no puede marcar → 403', r.status === 403, r.status);
r = await call('GET', url(today), undefined, null);
check('sin token → 401', r.status === 401, r.status);

// Marcar la clase de hoy
r = await mark(today, [{ studentUserId: ana.userId, status: 'present' }, { studentUserId: luis.userId, status: 'late', note: 'Entró 10 min tarde' }], lucia.token);
check('la profesora marca toda la lista en una petición', r.status === 200 &&
  r.json.students.find((s) => s.studentUserId === luis.userId)?.status === 'late', r.json);
r = await mark(today, [{ studentUserId: carla.userId, status: 'present' }], lucia.token);
check('a una matrícula pendiente → 400', r.status === 400, r.status);
r = await mark(today, [{ studentUserId: ana.userId, status: 'presente' }], lucia.token);
check('estado desconocido → 400', r.status === 400, r.status);
r = await mark(later, [{ studentUserId: ana.userId, status: 'present' }], lucia.token);
check('una clase que todavía no empieza → 409', r.status === 409, r.status);
r = await call('GET', url(draft.sessions[0].id));
check('una clase de un grupo en borrador → 404', r.status === 404, r.status);

// Corrección por el equipo
r = await mark(today, [{ studentUserId: luis.userId, status: 'absent', note: 'Avisó que estaba enfermo' }]);
const corrected = r.json?.students?.find((s) => s.studentUserId === luis.userId);
check('el equipo corrige la asistencia', r.status === 200 && corrected?.status === 'absent' && corrected.note === 'Avisó que estaba enfermo', corrected);

// Cuatro guardados simultáneos de la misma clase: nunca filas duplicadas
const parallel = await Promise.all(['present', 'late', 'absent', 'present'].map((status) =>
  mark(today, [{ studentUserId: ana.userId, status }, { studentUserId: luis.userId, status }], lucia.token)));
r = await call('GET', url(today), undefined, lucia.token);
check('4 guardados simultáneos: sin errores y una sola fila por alumno', parallel.every((x) => x.status === 200 || x.status === 409) &&
  r.json.students.length === 2 && r.json.students.every((s) => s.status), parallel.map((x) => x.status));
await mark(today, [{ studentUserId: ana.userId, status: 'present' }, { studentUserId: luis.userId, status: 'absent', note: 'Avisó que estaba enfermo' }], lucia.token);

// Lo que ven el alumno y la familia
const anaToken = (await login('student', { username: ana.loginIdentifier, password: ana.initialPassword })).token;
r = await call('GET', '/students/me/attendance', undefined, anaToken);
check('la alumna ve su asistencia con el curso', r.status === 200 && r.json.summary.present === 1 && r.json.summary.rate === 100 &&
  r.json.recent[0]?.courseTitle === course.title, r.json);
r = await call('GET', '/families/me/attendance', undefined, parentToken);
check('la familia ve solo a su hija', r.status === 200 && r.json.length === 1 && r.json[0].studentUserId === ana.userId && r.json[0].summary.total === 1, r.json);
const otherParent = (await call('POST', '/admin/guardians', { givenNames: 'Otro', firstSurname: `Padre${tag}`, email: `otro.asis.${tag}@example.com` })).json;
const otherToken = (await login('admin', { email: otherParent.loginIdentifier, password: otherParent.initialPassword })).token;
r = await call('GET', '/families/me/attendance', undefined, otherToken);
check('otra familia no ve nada', r.status === 200 && r.json.length === 0, r.json);

finish();
