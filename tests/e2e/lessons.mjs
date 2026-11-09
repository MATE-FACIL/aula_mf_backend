// Lecciones (SCRUM-55): unidades y lecciones ordenadas, materiales, borradores y lo que ve el alumno.
import { check, finish, login, request as call, tag } from './lib.mjs';

const course = (await call('POST', '/courses', { title: `Fracciones ${tag}` })).json;
const other = (await call('POST', '/courses', { title: `Otro curso ${tag}` })).json;
const base = `/courses/${course.id}/content`;

// Unidades y lecciones
let r = await call('POST', `${base}/units`, { title: 'Unidad 1 · Fracciones' });
const unit1 = r.json;
check('crear unidad', r.status === 201 && unit1.position === 1, r);
const unit2 = (await call('POST', `${base}/units`, { title: 'Unidad 2 · Decimales' })).json;
check('la segunda unidad va después', unit2.position === 2, unit2);

const lesson = (unitId, title, extra = {}) => call('POST', `${base}/units/${unitId}/lessons`, {
  title, summary: null, videoUrl: null, isPublished: true, materials: [], ...extra,
});
r = await lesson(unit1.id, 'Sumar fracciones', {
  summary: 'Con distinto denominador', videoUrl: 'https://www.youtube.com/watch?v=frac1',
  materials: [{ title: 'Guía de práctica', url: 'https://cdn.example.com/guia.pdf' }, { title: 'Pizarra', url: 'https://cdn.example.com/pizarra.png' }],
});
const sumar = r.json;
check('lección con video y 2 materiales en orden', r.status === 201 && sumar.number === 1 && sumar.materials.map((m) => m.title).join() === 'Guía de práctica,Pizarra', r);
const decimales = (await lesson(unit2.id, 'Leer decimales')).json;
const borrador = (await lesson(unit1.id, 'Restar fracciones (en preparación)', { isPublished: false })).json;
// Una lección nueva en la unidad 1 corre a las de la unidad 2: los números salen del orden completo.
const numbered = (await call('GET', base)).json.units.flatMap((u) => u.lessons.map((l) => `${l.number}:${l.title}`));
check('las lecciones se numeran por unidad y luego por orden', borrador.number === 2 && decimales.id &&
  numbered.join('|') === '1:Sumar fracciones|2:Restar fracciones (en preparación)|3:Leer decimales', numbered);

r = await lesson(unit1.id, 'Video inseguro', { videoUrl: 'http://video.example.com/a.mp4' });
check('video que no es https → 400 con mensaje claro', r.status === 400 && /https/.test(r.json?.detail), r);
r = await lesson(unit1.id, 'Muchos materiales', { materials: Array.from({ length: 21 }, (_, i) => ({ title: `A${i}`, url: `https://cdn.example.com/${i}.pdf` })) });
check('más de 20 materiales → 400', r.status === 400, r.status);

// Orden y cambios
r = await call('PUT', `${base}/units/order`, { ids: [unit2.id, unit1.id] });
const order = r.json?.units?.flatMap((u) => u.lessons.map((l) => `${l.number}:${l.title}`));
check('reordenar unidades renumera las lecciones', r.status === 200 && order?.[0] === '1:Leer decimales', order);
r = await call('PUT', `${base}/units/order`, { ids: [unit2.id] });
check('un orden incompleto → 400', r.status === 400, r.status);
r = await call('PUT', `${base}/lessons/${borrador.id}`, { unitId: unit2.id, title: 'Restar fracciones', summary: null, videoUrl: null, isPublished: false, materials: [] });
check('mover una lección a otra unidad la pone al final', r.status === 200 && r.json.unitId === unit2.id && r.json.number === 2, r.json);
r = await call('DELETE', `${base}/units/${unit1.id}`);
check('borrar una unidad con lecciones → 409', r.status === 409, r.status);
r = await call('POST', `/courses/${other.id}/content/units/${unit1.id}/lessons`, { title: 'Intrusa', isPublished: true, materials: [] });
check('una unidad de otro curso → 404', r.status === 404, r.status);

// Lo que ve el alumno: solo cursos con matrícula activa en un grupo publicado, y solo lecciones publicadas
const teacher = (await call('POST', '/admin/instructors', { givenNames: 'Lucía', firstSurname: `Lecciones${tag}`, email: `lecciones.${tag}@example.com` })).json;
const at = (days) => new Date(Date.now() + days * 864e5).toISOString();
const group = (await call('POST', `/courses/${course.id}/offerings`, {
  name: 'Grupo A', maximumCapacity: 8, basePrice: 100, currencyCode: 'PEN', plannedSessionCount: 1,
  sessions: [{ id: null, startsAt: at(3), endsAt: at(3.05), virtualLink: null }],
})).json;
await call('PUT', `/courses/${course.id}/offerings/${group.id}/instructors`, { instructorIds: [teacher.instructorId] });
await call('POST', `/courses/${course.id}/offerings/${group.id}/publish`);
const student = async (name, doc) => (await call('POST', '/admin/students', { givenNames: name, firstSurname: `Lecciones${tag}`, documentType: 'DNI', documentNumber: doc })).json;
const active = await student('Ana', `7${tag}1`);
const pending = await student('Luis', `7${tag}2`);
const seat = (await call('POST', '/enrollments', { studentUserId: active.userId, courseOfferingId: group.id })).json;
await call('POST', '/enrollments', { studentUserId: pending.userId, courseOfferingId: group.id });
await call('POST', `/enrollments/${seat.id}/activate`, {});

const activeToken = (await login('student', { username: active.loginIdentifier, password: active.initialPassword })).token;
const pendingToken = (await login('student', { username: pending.loginIdentifier, password: pending.initialPassword })).token;

r = await call('GET', '/students/me/courses', undefined, activeToken);
check('el alumno ve su curso', r.status === 200 && r.json.length === 1 && r.json[0].courseId === course.id, r.json);
r = await call('GET', `/students/me/courses/${course.id}/lessons`, undefined, activeToken);
const seen = r.json?.units?.flatMap((u) => u.lessons) ?? [];
check('ve solo las lecciones publicadas, numeradas sin huecos', r.status === 200 && seen.length === 2 && seen.map((l) => l.number).join() === '1,2', seen);
check('no ve borradores', !seen.some((l) => l.title === 'Restar fracciones'));
check('la lección trae video y materiales', seen.some((l) => l.videoUrl && l.materials.length === 2));
r = await call('GET', `/students/me/courses/${course.id}/lessons`, undefined, pendingToken);
check('matrícula pendiente → 404', r.status === 404, r.status);
r = await call('GET', `/students/me/courses/${other.id}/lessons`, undefined, activeToken);
check('un curso en el que no está → 404', r.status === 404, r.status);
r = await call('GET', base, undefined, activeToken);
check('el alumno no puede administrar contenido → 403', r.status === 403, r.status);
r = await call('GET', `/students/me/courses/${course.id}/lessons`, undefined, null);
check('sin token → 401', r.status === 401, r.status);

finish();
