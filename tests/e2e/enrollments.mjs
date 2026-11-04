// Matrícula (SCRUM-40, SCRUM-44): cupos con concurrencia, grupos en borrador, activación y retiro.
import { randomUUID } from 'node:crypto';
import { admin, adminUser, check, finish, login, request, tag } from './lib.mjs';

const call = (method, path, token, body) => request(method, path, body, token ?? null);

// Curso y grupo con cupo 3
let r = await call('POST', '/courses', admin, { title: `Mate e2e ${tag}` });
const courseId = r.json?.id;
const start = new Date(Date.now() + 7 * 864e5);
const sessions = [0, 1].map((i) => ({ id: null, startsAt: new Date(start.getTime() + i * 864e5).toISOString(), endsAt: new Date(start.getTime() + i * 864e5 + 36e5).toISOString(), virtualLink: null }));
r = await call('POST', `/courses/${courseId}/offerings`, admin, { name: `Grupo A ${tag}`, maximumCapacity: 3, basePrice: 120, currencyCode: 'PEN', plannedSessionCount: 2, sessions });
const offeringId = r.json?.id;
check('crear curso y grupo con cupo 3', r.status === 201 && offeringId, r);
const teacher = await call('POST', '/admin/instructors', admin, { givenNames: 'Lucía', firstSurname: `Paredes${tag}`, email: `lucia.${tag}@example.com` });
const assignTeacher = (id) => call('PUT', `/courses/${courseId}/offerings/${id}/instructors`, admin, { instructorIds: [teacher.json.instructorId] });
await assignTeacher(offeringId);

// Permisos
// Un padre real: tiene sesión, pero no el permiso enrollments.manage
const parent = (await call('POST', '/admin/guardians', admin, { givenNames: 'Sin', firstSurname: `Permiso${tag}`, email: `sin.${tag}@example.com` })).json;
const plain = (await login('admin', { email: parent.loginIdentifier, password: parent.initialPassword })).token;
r = await call('GET', '/enrollments', plain);
check('sin enrollments.manage → 403', r.status === 403, r.status);
r = await call('GET', '/enrollments');
check('sin token → 401', r.status === 401, r.status);

// 10 alumnos
const students = [];
for (let i = 0; i < 10; i++) {
  r = await call('POST', '/admin/students', admin, { givenNames: `Alumno${i}`, firstSurname: 'Prueba', documentType: 'DNI', documentNumber: `6${tag}${i}` });
  students.push(r.json?.userId);
}
check('10 alumnos creados', students.every(Boolean), students);

// Borrador: nadie ocupa cupo hasta publicar
r = await call('POST', '/enrollments', admin, { studentUserId: students[0], courseOfferingId: offeringId, paymentNote: null });
check('grupo en borrador → 409', r.status === 409 && /borrador/.test(r.json?.detail), r);
r = await call('POST', `/courses/${courseId}/offerings/${offeringId}/publish`, admin);
check('publicar el grupo', r.status === 200 && r.json.status === 'published', r);

// Cupo bajo concurrencia: 10 altas simultáneas en un grupo de 3
const results = await Promise.all(students.map((s) => call('POST', '/enrollments', admin, { studentUserId: s, courseOfferingId: offeringId, paymentNote: null })));
const created = results.filter((x) => x.status === 201);
const full = results.filter((x) => x.status === 409 && /lleno/.test(x.json?.detail));
check('10 altas simultáneas: exactamente 3 entran', created.length === 3, results.map((x) => x.status));
check('las otras 7 → 409 «grupo lleno»', full.length === 7, results.map((x) => x.json?.detail));

r = await call('GET', '/enrollments/offerings', admin);
const seats = r.json?.find((o) => o.id === offeringId);
check('cupos del grupo: 3 de 3, 0 libres', seats?.takenSeats === 3 && seats?.freeSeats === 0, seats);

// Duplicado simultáneo del mismo alumno en otro grupo con espacio
r = await call('POST', `/courses/${courseId}/offerings`, admin, { name: `Grupo B ${tag}`, maximumCapacity: 8, basePrice: 120, currencyCode: 'PEN', plannedSessionCount: 2, sessions });
const groupB = r.json?.id;
await assignTeacher(groupB);
await call('POST', `/courses/${courseId}/offerings/${groupB}/publish`, admin);
const dup = await Promise.all([1, 2, 3, 4].map(() => call('POST', '/enrollments', admin, { studentUserId: students[9], courseOfferingId: groupB })));
check('4 altas simultáneas del mismo alumno: 1 entra, 3 «ya matriculado»',
  dup.filter((x) => x.status === 201).length === 1 && dup.filter((x) => x.status === 409 && /ya está matriculado/.test(x.json?.detail)).length === 3,
  dup.map((x) => x.status));

// Validaciones
r = await call('POST', '/enrollments', admin, { studentUserId: adminUser, courseOfferingId: groupB });
check('matricular a quien no es alumno → 400', r.status === 400, r);
r = await call('POST', '/enrollments', admin, { studentUserId: students[8], courseOfferingId: randomUUID() });
check('grupo inexistente → 404', r.status === 404, r);
r = await call('POST', '/enrollments', admin, { studentUserId: students[8], courseOfferingId: groupB, paymentNote: 'x'.repeat(301) });
check('nota de pago larga → 400', r.status === 400, r);

// Flujo: pendiente → activa (con quién y cuándo) → retirada libera cupo
const first = created[0].json;
check('alta queda pendiente con quién la registró', first.status === 'pending' && first.createdByName, first);
r = await call('POST', `/enrollments/${first.id}/activate`, admin, { paymentNote: 'Yape op. 123' });
check('activar registra quién, cuándo y nota', r.status === 200 && r.json.status === 'active' && r.json.activatedAt && r.json.activatedByName && r.json.paymentNote === 'Yape op. 123', r.json);
r = await call('POST', `/enrollments/${first.id}/activate`, admin, {});
check('activar dos veces → 400', r.status === 400, r.status);
r = await call('POST', `/enrollments/${first.id}/withdraw`, admin);
check('retirar → withdrawn', r.status === 200 && r.json.status === 'withdrawn' && r.json.withdrawnByName, r.json);
r = await call('POST', `/enrollments/${first.id}/withdraw`, admin);
check('retirar dos veces → 400', r.status === 400, r.status);
const waiting = students.find((s) => !created.some((c) => c.json.studentUserId === s) && s !== students[9]);
r = await call('POST', '/enrollments', admin, { studentUserId: waiting, courseOfferingId: offeringId });
check('el cupo liberado se puede usar', r.status === 201, r);
r = await call('POST', '/enrollments', admin, { studentUserId: first.studentUserId, courseOfferingId: offeringId });
check('el retirado no vuelve con el grupo lleno → 409', r.status === 409, r);

// Listado y filtros
r = await call('GET', `/enrollments?courseOfferingId=${offeringId}&status=withdrawn`, admin);
check('filtrar por grupo y retiradas', r.status === 200 && r.json.total === 1 && r.json.items[0].id === first.id, r.json);
r = await call('GET', `/enrollments?studentUserId=${students[9]}`, admin);
check('filtrar por alumno con nombres de curso y grupo', r.json?.items?.[0]?.courseTitle === `Mate e2e ${tag}` && r.json.items[0].offeringName === `Grupo B ${tag}`, r.json);
r = await call('GET', '/enrollments?status=paid', admin);
check('estado desconocido → 400', r.status === 400, r.status);
r = await call('GET', '/enrollments?page=1&pageSize=2', admin);
check('paginación', r.status === 200 && r.json.items.length <= 2 && r.json.total >= 5, r.json);

finish();
