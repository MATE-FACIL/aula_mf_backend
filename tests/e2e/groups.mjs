// Grupos y horarios (SCRUM-41, SCRUM-42): duración, profesores, publicación y enlaces de clase.
import { check, finish, request as call, tag } from './lib.mjs';

// SCRUM-42: duración por curso
let r = await call('POST', '/courses', { title: `Secundaria ${tag}`, sessionDurationMinutes: 90 });
const courseId = r.json?.id;
check('curso con duración de clase 90 min', r.status === 201 && r.json.sessionDurationMinutes === 90, r);
r = await call('PUT', `/courses/${courseId}`, { title: `Secundaria ${tag}`, sessionDurationMinutes: 10 });
check('duración fuera de rango → 400', r.status === 400, r.status);
r = await call('PUT', `/courses/${courseId}`, { title: `Secundaria ${tag}`, sessionDurationMinutes: null });
check('duración se puede quitar', r.status === 200 && r.json.sessionDurationMinutes === null, r.json);

// Profesores con nombre
r = await call('POST', '/admin/instructors', { givenNames: 'Lucía', firstSurname: `Paredes${tag}`, email: `lucia.${tag}@example.com` });
const instructorId = r.json?.instructorId;
check('crear profesor (persona + cuenta + rol + profesor)', r.status === 201 && instructorId && r.json.initialPassword, r);
r = await call('POST', '/admin/instructors', { givenNames: 'Otra', firstSurname: 'Persona', email: `LUCIA.${tag}@example.com` });
check('correo repetido → 409', r.status === 409, r.status);
r = await call('GET', '/admin/instructors');
check('lista de profesores con nombre', r.status === 200 && r.json.some((i) => i.id === instructorId && i.fullName === `Lucía Paredes${tag}`), r.json);
r = await call('GET', `/admin/users?role=instructor&search=Paredes${tag}`);
check('el profesor tiene rol instructor', r.json?.items?.[0]?.roles?.includes('instructor'), r.json);

// Grupo con horario (lunes y miércoles 5 p. m. Lima = 22:00 UTC, 90 min)
const day = (n) => new Date(Date.now() + n * 864e5).toISOString().slice(0, 10);
const sessions = [day(30), day(32)].map((d) => ({ id: null, startsAt: `${d}T22:00:00Z`, endsAt: `${d}T23:30:00Z`, virtualLink: 'https://meet.example.com/a' }));
r = await call('POST', `/courses/${courseId}/offerings`, { name: 'Grupo A', maximumCapacity: 8, basePrice: 180, currencyCode: 'PEN', plannedSessionCount: 2, sessions });
const offeringId = r.json?.id;
check('crear grupo en borrador con 2 clases', r.status === 201 && r.json.status === 'draft' && r.json.sessions.length === 2, r);
r = await call('POST', `/courses/${courseId}/offerings/${offeringId}/publish`);
check('publicar sin profesor → 409', r.status === 409, r);

// Editar el borrador conservando una sesión (id) y cambiando la otra
const kept = r.status ? (await call('GET', `/courses/${courseId}/offerings/${offeringId}`)).json.sessions[0] : null;
r = await call('PUT', `/courses/${courseId}/offerings/${offeringId}`, {
  name: 'Grupo A · Lun y Mié', maximumCapacity: 8, basePrice: 180, currencyCode: 'PEN', plannedSessionCount: 2,
  sessions: [{ ...kept }, { id: null, startsAt: `${day(34)}T22:00:00Z`, endsAt: `${day(34)}T23:30:00Z`, virtualLink: null }],
});
check('editar borrador conserva la sesión con id', r.status === 200 && r.json.sessions.some((s) => s.id === kept.id), r);

r = await call('PUT', `/courses/${courseId}/offerings/${offeringId}/instructors`, { instructorIds: [instructorId] });
check('asignar profesor', r.status === 200 && r.json.instructorIds[0] === instructorId, r);
r = await call('POST', `/courses/${courseId}/offerings/${offeringId}/publish`);
check('publicar grupo completo', r.status === 200 && r.json.status === 'published', r);
r = await call('PUT', `/courses/${courseId}/offerings/${offeringId}`, { name: 'x', maximumCapacity: 8, basePrice: 1, currencyCode: 'PEN', plannedSessionCount: 2, sessions });
check('publicado: no se edita el horario → 409', r.status === 409, r.status);
const sessionId = (await call('GET', `/courses/${courseId}/offerings/${offeringId}`)).json.sessions[1].id;
r = await call('PATCH', `/courses/${courseId}/offerings/${offeringId}/sessions/${sessionId}/virtual-link`, { virtualLink: 'https://zoom.example.com/j/1' });
check('publicado: sí se edita el enlace de una clase', r.status === 200 && r.json.sessions.find((s) => s.id === sessionId).virtualLink === 'https://zoom.example.com/j/1', r);

// Lista de grupos del curso y matrícula sobre el grupo publicado
r = await call('GET', `/courses/${courseId}/offerings`);
check('lista de grupos del curso (consultas separadas por colección)', r.status === 200 && r.json.length === 1 && r.json[0].sessions.length === 2 && r.json[0].instructorIds.length === 1, r.json);
r = await call('POST', '/admin/students', { givenNames: 'Ana', firstSurname: 'Grupo', documentType: 'DNI', documentNumber: `5${tag}` });
r = await call('POST', '/enrollments', { studentUserId: r.json.userId, courseOfferingId: offeringId });
check('matricular en el grupo publicado', r.status === 201 && r.json.offeringName === 'Grupo A · Lun y Mié', r);
r = await call('GET', '/enrollments/offerings');
const seat = r.json.find((o) => o.id === offeringId);
check('cupos del grupo (1 de 8, publicado)', seat?.takenSeats === 1 && seat.isPublished === true, seat);

finish();
