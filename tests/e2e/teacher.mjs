// Panel del profesor (SCRUM-69): solo sus grupos, sus clases con la asistencia, sus alumnos con asistencia y
// avance de práctica, y la asistencia en un clic.
import { check, finish, login, request as call, tag } from './lib.mjs';

const teacherOf = async (name) => {
  const created = (await call('POST', '/admin/instructors', { givenNames: name, firstSurname: `Panel${tag}`, email: `${name.toLowerCase()}.panel.${tag}@example.com` })).json;
  return { ...created, token: (await login('admin', { email: created.loginIdentifier, password: created.initialPassword })).token };
};
const lucia = await teacherOf('Lucia');
const diego = await teacherOf('Diego');

const course = (await call('POST', '/courses', { title: `Panel ${tag}` })).json;
const at = (minutes) => new Date(Math.floor((Date.now() + minutes * 60000) / 60000) * 60000);
const session = (minutes) => ({ id: null, startsAt: at(minutes).toISOString(), endsAt: new Date(at(minutes).getTime() + 3600000).toISOString(), virtualLink: null });
const groupFor = async (name, teacher, sessions) => {
  const group = (await call('POST', `/courses/${course.id}/offerings`, {
    name, maximumCapacity: 8, basePrice: 100, currencyCode: 'PEN', plannedSessionCount: sessions.length, sessions,
  })).json;
  await call('PUT', `/courses/${course.id}/offerings/${group.id}/instructors`, { instructorIds: [teacher.instructorId] });
  return (await call('POST', `/courses/${course.id}/offerings/${group.id}/publish`)).json;
};
const mine = await groupFor('Grupo Lucía', lucia, [session(5), session(2880)]);
const other = await groupFor('Grupo Diego', diego, [session(10)]);
const [today] = [...mine.sessions].sort((a, b) => a.startsAt.localeCompare(b.startsAt));

const student = async (name, doc, group) => {
  const created = (await call('POST', '/admin/students', { givenNames: name, firstSurname: `Panel${tag}`, documentType: 'DNI', documentNumber: doc })).json;
  const seat = (await call('POST', '/enrollments', { studentUserId: created.userId, courseOfferingId: group.id })).json;
  await call('POST', `/enrollments/${seat.id}/activate`, {});
  return created;
};
const ana = await student('Ana', `4${tag}7`, mine);
const luis = await student('Luis', `4${tag}8`, mine);
await student('Ajeno', `4${tag}9`, other);

// Solo sus grupos
let r = await call('GET', '/teachers/me/groups', undefined, lucia.token);
const groups = r.json ?? [];
check('la profesora ve solo su grupo, con alumnos activos y la próxima clase', r.status === 200 && groups.length === 1 &&
  groups[0].offeringId === mine.id && groups[0].activeStudents === 2 && groups[0].nextSessionId === today.id, groups);
r = await call('GET', `/teachers/me/groups/${other.id}`, undefined, lucia.token);
check('el grupo de otro profesor → 404', r.status === 404, r.status);
const adminGroups = await call('GET', '/teachers/me/groups');
check('una cuenta que no enseña ve una lista vacía', adminGroups.status === 200 && adminGroups.json.length === 0, adminGroups.json);
r = await call('GET', '/teachers/me/groups', undefined, null);
check('sin token → 401', r.status === 401, r.status);

// Asistencia en un clic: Luis ya estaba marcado tarde; Ana queda presente
await call('PUT', `/attendance/sessions/${today.id}`, { records: [{ studentUserId: luis.userId, status: 'late', note: 'Entró 10 min tarde' }] }, lucia.token);
r = await call('POST', `/attendance/sessions/${today.id}/present-all`, undefined, lucia.token);
const byName = Object.fromEntries((r.json?.students ?? []).map((s) => [s.studentUserId, s.status]));
check('marcar asistencia en 1 clic: los que faltaban quedan presentes y no se pisa lo marcado', r.status === 200 &&
  byName[ana.userId] === 'present' && byName[luis.userId] === 'late', r.json);
r = await call('POST', `/attendance/sessions/${today.id}/present-all`, undefined, diego.token);
check('otro profesor no puede marcar ese grupo → 403', r.status === 403, r.status);

// Detalle del grupo
r = await call('GET', `/teachers/me/groups/${mine.id}`, undefined, lucia.token);
const detail = r.json;
check('el detalle trae sus clases con la asistencia contada', r.status === 200 && detail.sessions.length === 2 &&
  detail.sessions[0].marked === 2 && detail.sessions[0].late === 1, detail?.sessions);
check('y sus alumnos con asistencia y avance de práctica', detail?.students.length === 2 &&
  detail.students.every((s) => s.attendance.rate === 100 && s.practice.total === 0), detail?.students);

finish();
