// Niveles de Matemáticas (SCRUM-39): ocho niveles (cuatro por etapa), cada uno un curso con sus 14 sesiones.
import { check, finish, login, request as call, tag } from './lib.mjs';

let r = await call('GET', '/math-levels');
check('hay 8 niveles: 4 de primaria y 4 de secundaria', r.status === 200 && r.json.length === 8 &&
  r.json.filter((l) => l.stage === 'primary').length === 4, r.json?.length);

r = await call('POST', '/math-levels/provision');
const first = r.json;
check('crear los niveles que faltan', r.status === 200 && first.levels.every((l) => l.courseId), r);
r = await call('POST', '/math-levels/provision');
check('repetir no crea nada nuevo', r.status === 200 && r.json.created === 0 &&
  r.json.levels.map((l) => l.courseId).join() === first.levels.map((l) => l.courseId).join(), r.json?.created);

const levels = r.json.levels;
check('cada nivel tiene al menos sus 14 sesiones', levels.every((l) => l.lessons >= 14), levels.map((l) => l.lessons));
const prebasic = levels.find((l) => l.stage === 'primary' && l.level === 'prebasic');
const advanced = levels.find((l) => l.stage === 'secondary' && l.level === 'advanced');
check('nombres claros por etapa y nivel', prebasic.courseTitle === 'Matemáticas · Primaria Prebásico' && advanced.courseTitle === 'Matemáticas · Secundaria Avanzado', [prebasic.courseTitle, advanced.courseTitle]);
r = await call('GET', `/courses/${prebasic.courseId}`);
const primaryMinutes = r.json?.sessionDurationMinutes;
r = await call('GET', `/courses/${advanced.courseId}`);
check('primaria 60 min y secundaria 90 min por clase', primaryMinutes === 60 && r.json?.sessionDurationMinutes === 90, [primaryMinutes, r.json?.sessionDurationMinutes]);
r = await call('GET', `/courses/${advanced.courseId}/content`);
const sessions = r.json?.units?.flatMap((u) => u.lessons) ?? [];
check('las sesiones están en orden y numeradas', sessions.length >= 14 && sessions[0].title === 'Sesión 1' && sessions[13].number === 14, sessions.slice(0, 2));

// El alumno ve el nivel de su curso
const teacher = (await call('POST', '/admin/instructors', { givenNames: 'Lucía', firstSurname: `Niveles${tag}`, email: `niveles.${tag}@example.com` })).json;
const at = (days) => new Date(Date.now() + days * 864e5).toISOString();
const group = (await call('POST', `/courses/${advanced.courseId}/offerings`, {
  name: `Grupo Niveles ${tag}`, maximumCapacity: 12, basePrice: 180, currencyCode: 'PEN', plannedSessionCount: 1,
  sessions: [{ id: null, startsAt: at(5), endsAt: at(5.06), virtualLink: null }],
})).json;
await call('PUT', `/courses/${advanced.courseId}/offerings/${group.id}/instructors`, { instructorIds: [teacher.instructorId] });
await call('POST', `/courses/${advanced.courseId}/offerings/${group.id}/publish`);
const student = (await call('POST', '/admin/students', { givenNames: 'Ana', firstSurname: `Niveles${tag}`, documentType: 'DNI', documentNumber: `6${tag}9` })).json;
const seat = (await call('POST', '/enrollments', { studentUserId: student.userId, courseOfferingId: group.id })).json;
await call('POST', `/enrollments/${seat.id}/activate`, {});
const token = (await login('student', { username: student.loginIdentifier, password: student.initialPassword })).token;
r = await call('GET', '/students/me/courses', undefined, token);
const level = r.json?.find((c) => c.courseId === advanced.courseId)?.level;
check('el alumno ve su nivel de Matemáticas', level?.stage === 'secondary' && level.level === 'advanced' && level.levelLabel === 'Avanzado', r.json);
r = await call('GET', '/math-levels', undefined, token);
check('el alumno no administra niveles → 403', r.status === 403, r.status);

finish();
