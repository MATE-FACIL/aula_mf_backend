// Práctica (SCRUM-59/60): ejercicios corregidos en el servidor, explicación tras responder, avance por lección y
// nivel, e historial de intentos. Solo alumnos con matrícula activa en lecciones publicadas.
import { check, finish, login, request as call, tag } from './lib.mjs';

const course = (await call('POST', '/courses', { title: `Práctica ${tag}` })).json;
const base = `/courses/${course.id}/content`;
const unit = (await call('POST', `${base}/units`, { title: 'Unidad 1' })).json;
const lessonOf = async (title, isPublished = true) =>
  (await call('POST', `${base}/units/${unit.id}/lessons`, { title, isPublished, materials: [] })).json;
const sumar = await lessonOf('Sumar fracciones');
const restar = await lessonOf('Restar fracciones');
const borrador = await lessonOf('En preparación', false);
const exercises = (lesson, list) => call('PUT', `/courses/${course.id}/lessons/${lesson.id}/exercises`, { exercises: list });

// El equipo carga los ejercicios
let r = await exercises(sumar, [
  { kind: 'multiple_choice', prompt: '¿Cuánto es 1/2 + 1/4?', options: ['3/4', '2/6', '1/6'], correctOption: 0, explanation: 'Común denominador: 2/4 + 1/4 = 3/4.' },
  { kind: 'numeric', prompt: '¿Cuánto es 7 ÷ 2?', correctNumber: 3.5, tolerance: 0, explanation: '7 ÷ 2 = 3,5.' },
]);
const [choice, numeric] = r.json ?? [];
check('el equipo carga ejercicios de opción múltiple y numéricos', r.status === 200 && r.json.length === 2 && choice.correctOption === 0, r);
r = await exercises(restar, [{ kind: 'numeric', prompt: '¿Cuánto es 3/4 − 1/4 en decimal?', correctNumber: 0.5 }]);
check('otra lección con su ejercicio', r.status === 200, r.status);
await exercises(borrador, [{ kind: 'numeric', prompt: 'Borrador', correctNumber: 1 }]);
r = await exercises(sumar, [{ kind: 'multiple_choice', prompt: 'Sin respuesta', options: ['A', 'B'] }]);
check('un ejercicio sin respuesta correcta → 400', r.status === 400, r.status);
r = await call('PUT', `/courses/${Guid()}/lessons/${sumar.id}/exercises`, { exercises: [] });
check('una lección de otro curso → 404', r.status === 404, r.status);

// Alumnos: activo y pendiente
const teacher = (await call('POST', '/admin/instructors', { givenNames: 'Lucía', firstSurname: `Practica${tag}`, email: `practica.${tag}@example.com` })).json;
const at = (days) => new Date(Date.now() + days * 864e5).toISOString();
const group = (await call('POST', `/courses/${course.id}/offerings`, {
  name: 'Grupo A', maximumCapacity: 8, basePrice: 100, currencyCode: 'PEN', plannedSessionCount: 1,
  sessions: [{ id: null, startsAt: at(3), endsAt: at(3.05), virtualLink: null }],
})).json;
await call('PUT', `/courses/${course.id}/offerings/${group.id}/instructors`, { instructorIds: [teacher.instructorId] });
await call('POST', `/courses/${course.id}/offerings/${group.id}/publish`);
const student = async (name, doc) => (await call('POST', '/admin/students', { givenNames: name, firstSurname: `Practica${tag}`, documentType: 'DNI', documentNumber: doc })).json;
const ana = await student('Ana', `5${tag}1`);
const luis = await student('Luis', `5${tag}2`);
const seat = (await call('POST', '/enrollments', { studentUserId: ana.userId, courseOfferingId: group.id })).json;
await call('POST', '/enrollments', { studentUserId: luis.userId, courseOfferingId: group.id });
await call('POST', `/enrollments/${seat.id}/activate`, {});
const anaToken = (await login('student', { username: ana.loginIdentifier, password: ana.initialPassword })).token;
const luisToken = (await login('student', { username: luis.loginIdentifier, password: luis.initialPassword })).token;
const me = (method, path, body, token = anaToken) => call(method, `/students/me${path}`, body, token);

r = await me('GET', `/lessons/${sumar.id}/practice`);
const raw = JSON.stringify(r.json ?? {});
check('el alumno ve los ejercicios sin la respuesta ni la explicación', r.status === 200 && r.json.exercises.length === 2 &&
  !/correctOption|correctNumber|explanation|Común denominador/i.test(raw), raw.slice(0, 200));
r = await me('GET', `/lessons/${sumar.id}/practice`, undefined, luisToken);
check('matrícula pendiente → 404', r.status === 404, r.status);
r = await me('GET', `/lessons/${borrador.id}/practice`);
check('lección en borrador → 404', r.status === 404, r.status);
r = await call('GET', `/courses/${course.id}/lessons/${sumar.id}/exercises`, undefined, anaToken);
check('el alumno no puede ver la versión del equipo → 403', r.status === 403, r.status);

// Responder
r = await me('POST', `/exercises/${choice.id}/answers`, { answer: '1' });
check('respuesta incorrecta: se corrige y llega la explicación', r.status === 200 && r.json.isCorrect === false && /Común denominador/.test(r.json.explanation), r.json);
r = await me('POST', `/exercises/${choice.id}/answers`, { answer: '0' });
check('respuesta correcta: avance de la lección 50 %', r.json?.isCorrect === true && r.json.lessonProgress.percent === 50, r.json);
r = await me('POST', `/exercises/${numeric.id}/answers`, { answer: '3,5' });
check('numérica con coma decimal', r.json?.isCorrect === true && r.json.lessonProgress.percent === 100, r.json);
r = await me('POST', `/exercises/${numeric.id}/answers`, { answer: 'tres y medio' });
check('una respuesta ilegible → 400 y no se guarda', r.status === 400, r.status);
r = await me('POST', `/exercises/${choice.id}/answers`, { answer: '0' }, luisToken);
check('sin matrícula activa no se puede responder → 404', r.status === 404, r.status);

// Avance e historial
r = await me('GET', `/courses/${course.id}/progress`);
check('avance del nivel: 2 de 3 ejercicios (67 %), por lección', r.status === 200 && r.json.progress.percent === 67 &&
  r.json.lessons.length === 2 && r.json.lessons[0].progress.percent === 100 && r.json.lessons[1].progress.percent === 0, r.json);
r = await me('GET', `/practice/attempts?lessonId=${sumar.id}&pageSize=2`);
check('historial: 3 intentos, del más reciente al más antiguo', r.status === 200 && r.json.total === 3 && r.json.items.length === 2 &&
  r.json.items[0].answer === '3,5', r.json);

// El equipo quita un ejercicio: se archiva y el historial queda
r = await exercises(sumar, [{ ...choice, id: choice.id }]);
r = await me('GET', `/lessons/${sumar.id}/practice`);
const history = await me('GET', `/practice/attempts?lessonId=${sumar.id}`);
check('quitar un ejercicio lo archiva: el avance se recalcula y el historial sigue', r.json?.exercises.length === 1 &&
  r.json.progress.percent === 100 && history.json?.total === 3, [r.json?.progress, history.json?.total]);

finish();

function Guid() { return '00000000-0000-4000-8000-000000000099'; }
