// Evaluaciones con nota automática y plazo (SCRUM-62): el profesor las arma y las publica en su grupo, el alumno las
// rinde dentro del plazo, el servidor corrige y calcula la nota sobre 20 con el resultado por competencia.
import { check, finish, login, request as call, tag } from './lib.mjs';

const teacherOf = async (name) => {
  const created = (await call('POST', '/admin/instructors', { givenNames: name, firstSurname: `Eval${tag}`, email: `${name.toLowerCase()}.eval.${tag}@example.com` })).json;
  return { ...created, token: (await login('admin', { email: created.loginIdentifier, password: created.initialPassword })).token };
};
const lucia = await teacherOf('Lucia');
const diego = await teacherOf('Diego');

const course = (await call('POST', '/courses', { title: `Evaluaciones ${tag}` })).json;
const at = (minutes) => new Date(Math.floor((Date.now() + minutes * 60000) / 60000) * 60000);
const groupFor = async (name, teacher) => {
  const group = (await call('POST', `/courses/${course.id}/offerings`, {
    name, maximumCapacity: 8, basePrice: 100, currencyCode: 'PEN', plannedSessionCount: 1,
    sessions: [{ id: null, startsAt: at(2880).toISOString(), endsAt: new Date(at(2880).getTime() + 3600000).toISOString(), virtualLink: null }],
  })).json;
  await call('PUT', `/courses/${course.id}/offerings/${group.id}/instructors`, { instructorIds: [teacher.instructorId] });
  return (await call('POST', `/courses/${course.id}/offerings/${group.id}/publish`)).json;
};
const mine = await groupFor('Grupo Lucía', lucia);
const other = await groupFor('Grupo Diego', diego);

const student = async (name, doc, group) => {
  const created = (await call('POST', '/admin/students', { givenNames: name, firstSurname: `Eval${tag}`, documentType: 'DNI', documentNumber: doc })).json;
  if (group) {
    const seat = (await call('POST', '/enrollments', { studentUserId: created.userId, courseOfferingId: group.id })).json;
    await call('POST', `/enrollments/${seat.id}/activate`, {});
  }
  return { ...created, token: (await login('student', { username: created.loginIdentifier, password: created.initialPassword })).token };
};
const ana = await student('Ana', `5${tag}1`, mine);
const marta = await student('Marta', `5${tag}2`, mine);
const luis = await student('Luis', `5${tag}3`, null);
const pedro = await student('Pedro', `5${tag}4`, other);

const questions = [
  { competency: 'calculo', kind: 'multiple_choice', prompt: '¿1/2 + 1/4?', options: ['3/4', '2/6'], correctOption: 0, explanation: `Común denominador ${tag}` },
  { competency: 'calculo', kind: 'numeric', prompt: '¿7 ÷ 2?', correctNumber: 3.5, tolerance: 0 },
  { competency: 'problemas', kind: 'multiple_choice', prompt: 'Si tengo 3 y compro 2 más, ¿cuántos tengo?', options: ['4', '5', '6'], correctOption: 1 },
  { competency: 'razonamiento', kind: 'numeric', prompt: '¿Qué número sigue: 2, 4, 6, …?', correctNumber: 8, tolerance: 0 },
];
const input = (extra = {}) => ({ kind: 'weekly', title: `Semanal ${tag}`, opensAt: at(-1).toISOString(), closesAt: at(60).toISOString(), durationMinutes: 30, isPublished: true, questions, ...extra });
const forGroup = `/teachers/me/groups/${mine.id}/evaluations`;

// ─── El profesor arma la evaluación ────────────────────────────────────
let r = await call('GET', '/teachers/me/competencies', undefined, lucia.token);
check('las competencias genéricas están disponibles', r.status === 200 && r.json.length === 3 && r.json[0].code === 'calculo', r.json);

r = await call('POST', forGroup, input({ isPublished: false }), lucia.token);
const draft = r.json;
check('la profesora crea un borrador con sus 4 preguntas (con respuesta)', r.status === 201 && draft.questions.length === 4 && draft.questions[0].correctOption === 0
  && draft.questions[0].competencyLabel === 'Cálculo y operaciones' && draft.evaluation.isPublished === false, r.json);
r = await call('GET', '/students/me/evaluations', undefined, ana.token);
check('un borrador no lo ve ningún alumno', r.status === 200 && r.json.length === 0, r.json);
r = await call('POST', `/students/me/evaluations/${draft.evaluation.id}/attempts`, undefined, ana.token);
check('ni se puede iniciar (404)', r.status === 404, r.status);

r = await call('POST', forGroup, input({ closesAt: at(-1).toISOString() }), lucia.token);
check('el cierre debe ser posterior a la apertura → 400', r.status === 400, r.json);
r = await call('POST', forGroup, input({ questions: [] }), lucia.token);
check('para publicarla necesita preguntas → 400', r.status === 400, r.json);
r = await call('POST', forGroup, input({ questions: [{ ...questions[0], competency: 'otra' }] }), lucia.token);
check('una competencia desconocida → 400 con la pregunta', r.status === 400 && String(r.json?.detail).includes('Pregunta 1'), r.json);
r = await call('POST', forGroup, input({ durationMinutes: 2 }), lucia.token);
check('un tiempo fuera de 5–180 minutos → 400', r.status === 400, r.json);
r = await call('POST', `/teachers/me/groups/${other.id}/evaluations`, input(), lucia.token);
check('crear en el grupo de otro profesor → 404', r.status === 404, r.status);

r = await call('PUT', `/teachers/me/evaluations/${draft.evaluation.id}`, input(), lucia.token);
check('la profesora la publica', r.status === 200 && r.json.evaluation.isPublished === true, r.json);
const evaluationId = draft.evaluation.id;

// ─── Otras ventanas ────────────────────────────────────────────────────
const future = (await call('POST', forGroup, input({ title: `Futura ${tag}`, opensAt: at(120).toISOString(), closesAt: at(180).toISOString() }), lucia.token)).json;
const past = (await call('POST', forGroup, input({ title: `Pasada ${tag}`, opensAt: at(-120).toISOString(), closesAt: at(-60).toISOString() }), lucia.token)).json;
r = await call('GET', '/students/me/evaluations', undefined, ana.token);
const states = Object.fromEntries(r.json.map((item) => [item.title, item.status]));
check('el alumno ve su estado: abierta, próxima y perdida', states[`Semanal ${tag}`] === 'open' && states[`Futura ${tag}`] === 'upcoming' && states[`Pasada ${tag}`] === 'missed', states);
r = await call('POST', `/students/me/evaluations/${future.evaluation.id}/attempts`, undefined, ana.token);
check('antes de la apertura no se puede iniciar → 409', r.status === 409, r.json);
r = await call('POST', `/students/me/evaluations/${past.evaluation.id}/attempts`, undefined, ana.token);
check('después del cierre no se puede iniciar → 409', r.status === 409, r.json);

// ─── Solo los alumnos del grupo ────────────────────────────────────────
r = await call('GET', '/students/me/evaluations', undefined, luis.token);
check('un alumno sin matrícula no ve ninguna', r.status === 200 && r.json.length === 0, r.json);
r = await call('POST', `/students/me/evaluations/${evaluationId}/attempts`, undefined, pedro.token);
check('un alumno de otro grupo no puede iniciarla → 404', r.status === 404, r.status);
r = await call('POST', `/students/me/evaluations/${evaluationId}/attempts`, undefined, null);
check('sin token → 401', r.status === 401, r.status);

// ─── Ana rinde ─────────────────────────────────────────────────────────
r = await call('POST', `/students/me/evaluations/${evaluationId}/attempts`, undefined, ana.token);
const started = r.json;
check('iniciar entrega las preguntas SIN respuesta y con el plazo del servidor', r.status === 201 && started.questions.length === 4 && started.durationSeconds === 1800
  && !JSON.stringify(started).match(/correctOption|correctNumber|explanation|Común denominador/) && Date.parse(started.deadline) - Date.parse(started.startedAt) === 1800000, r.json);
r = await call('POST', `/students/me/evaluations/${evaluationId}/attempts`, undefined, ana.token);
check('iniciar otra vez devuelve el mismo intento (el reloj no se reinicia)', r.status === 201 && r.json.attemptId === started.attemptId && r.json.startedAt === started.startedAt, r.json);

const byPrompt = Object.fromEntries(started.questions.map((q) => [q.prompt, q.id]));
const answers = { [byPrompt['¿1/2 + 1/4?']]: '0', [byPrompt['¿7 ÷ 2?']]: '3,5', [byPrompt['Si tengo 3 y compro 2 más, ¿cuántos tengo?']]: '2', [byPrompt['¿Qué número sigue: 2, 4, 6, …?']]: 'ocho' };
r = await call('POST', `/students/me/evaluations/attempts/${started.attemptId}/submit`, { answers }, luis.token);
check('el intento de otro alumno → 404', r.status === 404, r.status);
r = await call('POST', `/students/me/evaluations/attempts/${started.attemptId}/submit`, { answers }, ana.token);
const graded = r.json;
check('el servidor corrige: 2 de 4 → 10 sobre 20 y 50 %', r.status === 200 && graded.score === 10 && graded.percent === 50 && graded.correctCount === 2 && graded.total === 4 && graded.late === false, r.json);
const calc = graded.byCompetency.find((c) => c.competency === 'calculo');
const reasoning = graded.byCompetency.find((c) => c.competency === 'razonamiento');
check('resultado por competencia: cálculo 2/2, razonamiento 0/1 (una respuesta ilegible cuenta como mala)', calc?.correct === 2 && calc?.total === 2 && reasoning?.correct === 0 && reasoning?.total === 1, graded.byCompetency);
check('trae la explicación de cada pregunta', graded.questions.find((q) => q.explanation === `Común denominador ${tag}`)?.isCorrect === true, graded.questions);
r = await call('POST', `/students/me/evaluations/attempts/${started.attemptId}/submit`, { answers }, ana.token);
check('enviarla otra vez → 409 (nunca una segunda nota)', r.status === 409, r.json);
r = await call('POST', `/students/me/evaluations/${evaluationId}/attempts`, undefined, ana.token);
check('ya rendida: no se puede iniciar de nuevo → 409', r.status === 409, r.json);
r = await call('GET', `/students/me/evaluations/${evaluationId}/result`, undefined, ana.token);
check('el alumno puede volver a ver su resultado', r.status === 200 && r.json.score === 10, r.json);
r = await call('GET', '/students/me/evaluations', undefined, ana.token);
const mineRow = r.json.find((item) => item.id === evaluationId);
check('en su lista queda calificada con la nota', mineRow?.status === 'graded' && mineRow.score === 10 && mineRow.percent === 50, mineRow);

// ─── Marta empieza y no envía; el profesor ve a todos ──────────────────
await call('POST', `/students/me/evaluations/${evaluationId}/attempts`, undefined, marta.token);
r = await call('GET', `/teachers/me/evaluations/${evaluationId}/results`, undefined, lucia.token);
const byName = Object.fromEntries((r.json?.students ?? []).map((s) => [s.fullName.split(' ')[0], s]));
check('resultados del profesor: Ana calificada, Marta sin enviar, y no aparecen los de otro grupo', r.status === 200 && byName.Ana?.status === 'graded' && byName.Ana.score === 10
  && byName.Marta?.status === 'in_progress' && !byName.Pedro && !byName.Luis, r.json);
check('promedio del grupo y por competencia', r.json.averageScore === 10 && r.json.byCompetency.find((c) => c.competency === 'calculo')?.total === 2, r.json);
check('resultados: la lista de preguntas por competencia de Ana', byName.Ana?.byCompetency.length === 3, byName.Ana?.byCompetency);
r = await call('GET', forGroup, undefined, lucia.token);
const row = r.json.find((item) => item.id === evaluationId);
check('el resumen cuenta: 4 preguntas, 2 empezaron, 1 envió, 2 en el grupo', row?.questionCount === 4 && row.started === 2 && row.submitted === 1 && row.enrolled === 2, row);

// ─── Ya empezaron: las preguntas no cambian ────────────────────────────
r = await call('PUT', `/teachers/me/evaluations/${evaluationId}`, input({ questions: questions.slice(0, 2) }), lucia.token);
check('cambiar las preguntas con alumnos que ya empezaron → 409', r.status === 409, r.json);
r = await call('PUT', `/teachers/me/evaluations/${evaluationId}`, input({ closesAt: at(90).toISOString(), title: `Semanal ${tag} (ampliada)` }), lucia.token);
check('pero sí se puede ampliar el plazo con las mismas preguntas', r.status === 200 && r.json.evaluation.title.endsWith('(ampliada)') && r.json.questions.length === 4, r.json);
r = await call('DELETE', `/teachers/me/evaluations/${evaluationId}`, undefined, lucia.token);
check('no se puede borrar una evaluación ya empezada → 409', r.status === 409, r.json);
r = await call('PUT', `/teachers/me/evaluations/${evaluationId}`, input({ isPublished: false }), lucia.token);
check('ni volverla a borrador → 409', r.status === 409, r.json);

// ─── Otro profesor no toca nada ────────────────────────────────────────
for (const [label, method, path, body] of [
  ['ver', 'GET', `/teachers/me/evaluations/${evaluationId}`], ['editar', 'PUT', `/teachers/me/evaluations/${evaluationId}`, input()],
  ['borrar', 'DELETE', `/teachers/me/evaluations/${evaluationId}`], ['ver resultados de', 'GET', `/teachers/me/evaluations/${evaluationId}/results`],
  ['listar', 'GET', forGroup],
]) {
  r = await call(method, path, body, diego.token);
  check(`otro profesor no puede ${label} la evaluación → 404`, r.status === 404, r.status);
}

// ─── Borrar una sin empezar ────────────────────────────────────────────
r = await call('DELETE', `/teachers/me/evaluations/${past.evaluation.id}`, undefined, lucia.token);
check('una evaluación sin intentos se puede borrar', r.status === 204, r.status);
r = await call('GET', `/teachers/me/evaluations/${past.evaluation.id}`, undefined, lucia.token);
check('y ya no existe', r.status === 404, r.status);

finish();
