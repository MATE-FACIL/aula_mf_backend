// Familia (SCRUM-67, SCRUM-35): cada padre ve solo a sus hijos, con matrículas y clases.
import { admin, check, finish, login, request as call, tag } from './lib.mjs';

// Datos: curso publicado con grupo, profesor y clases
const category = (await call('POST', '/course-categories', { name: `Fam ${tag}` })).json;
const course = (await call('POST', '/courses', { title: `Geometría ${tag}`, categoryId: category.id })).json;
const teacher = (await call('POST', '/admin/instructors', { givenNames: 'Diego', firstSurname: `Salas${tag}`, email: `diego.${tag}@example.com` })).json;
const at = (d) => new Date(Math.floor((Date.now() + d * 86400000) / 60000) * 60000);
const s = (d) => ({ id: null, startsAt: at(d).toISOString(), endsAt: new Date(at(d).getTime() + 3600000).toISOString(), virtualLink: null });
const group = (await call('POST', `/courses/${course.id}/offerings`, { name: `Grupo Fam ${tag}`, maximumCapacity: 8, basePrice: 100, currencyCode: 'PEN', plannedSessionCount: 3, sessions: [s(1), s(3), s(40)] })).json;
await call('PUT', `/courses/${course.id}/offerings/${group.id}/instructors`, { instructorIds: [teacher.instructorId] });
await call('POST', `/courses/${course.id}/offerings/${group.id}/publish`);

// Familia 1 con dos hijos (activo y pendiente); familia 2 con un hijo distinto
const mom = (await call('POST', '/admin/guardians', { givenNames: 'Rosa', firstSurname: `Madre${tag}`, email: `rosa.${tag}@example.com` })).json;
const dad = (await call('POST', '/admin/guardians', { givenNames: 'Otro', firstSurname: `Padre${tag}`, email: `otro.${tag}@example.com` })).json;
const student = async (given, surname, doc, guardian) => (await call('POST', '/admin/students', { givenNames: given, firstSurname: surname, documentType: 'DNI', documentNumber: doc, guardianUserId: guardian })).json;
const ana = await student('Ana', `Hija${tag}`, `3${tag}1`, mom.userId);
const luis = await student('Luis', `Hijo${tag}`, `3${tag}2`, mom.userId);
const ajeno = await student('Ajeno', `Vecino${tag}`, `3${tag}3`, dad.userId);
const enroll = async (who) => (await call('POST', '/enrollments', { studentUserId: who.userId, courseOfferingId: group.id })).json;
const anaSeat = await enroll(ana); await enroll(luis); const ajenoSeat = await enroll(ajeno);
await call('POST', `/enrollments/${anaSeat.id}/activate`, { paymentNote: 'Yape' });
await call('POST', `/enrollments/${ajenoSeat.id}/activate`, {});

const signIn = async ({ loginIdentifier, initialPassword }) => (await login('admin', { email: loginIdentifier, password: initialPassword })).token;
const momToken = await signIn(mom);
const dadToken = await signIn(dad);

let r = await call('GET', '/families/me/children', undefined, momToken);
check('el padre ve solo sus 2 hijos', r.status === 200 && r.json.length === 2 && !r.json.some((c) => c.studentUserId === ajeno.userId), r.json?.map?.((c) => c.fullName));
const a = r.json.find((c) => c.studentUserId === ana.userId), l = r.json.find((c) => c.studentUserId === luis.userId);
check('hijo activo con matrícula y 2 clases próximas (no la de 40 días)', a?.enrollments[0]?.status === 'active' && a.upcomingClasses.length === 2 && a.upcomingClasses[0].teachers[0] === `Diego Salas${tag}`, a);
check('hijo pendiente con matrícula y sin clases', l?.enrollments[0]?.status === 'pending' && l.upcomingClasses.length === 0, l);
r = await call('GET', '/families/me/children', undefined, dadToken);
check('el otro padre ve solo a su hijo', r.json?.length === 1 && r.json[0].studentUserId === ajeno.userId, r.json);
r = await call('GET', '/families/me/children', undefined, admin);
check('cuenta sin hijos → lista vacía', r.status === 200 && r.json.length === 0, r);
r = await call('GET', '/families/me/children', undefined, null);
check('sin token → 401', r.status === 401, r.status);

finish();
