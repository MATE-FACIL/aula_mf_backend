// Diagnóstico de entrada (SCRUM-47/48): banco de preguntas por etapa, el alumno lo rinde una vez sin ver la
// respuesta, recibe el resultado por tema y un nivel sugerido, y el profesor lo revisa y confirma el nivel.
// El banco es único por etapa: la suite guarda el que haya y lo deja igual al terminar.
import { check, finish, login, request as call, tag } from './lib.mjs';

const STAGE = 'primary';
const bank = (questions) => call('PUT', `/diagnostics/questions/${STAGE}`, { questions });
const before = (await call('GET', `/diagnostics/questions/${STAGE}`)).json ?? [];

try {
  // El equipo carga el banco: 2 preguntas de Prebásico y 2 de Básico
  const q = (level, topic, prompt, extra) => ({ level, topic, prompt, kind: 'multiple_choice', options: ['Sí', 'No'], correctOption: 0, ...extra });
  let r = await bank([
    q('prebasic', `Conteo ${tag}`, '¿5 es mayor que 3?', { explanation: '5 está después de 3.' }),
    q('prebasic', `Conteo ${tag}`, '¿10 es par?'),
    { level: 'basic', topic: `Fracciones ${tag}`, prompt: '¿Cuánto es 1/2 + 1/4 en decimal?', kind: 'numeric', correctNumber: 0.75 },
    q('basic', `Fracciones ${tag}`, '¿1/2 es igual a 2/4?'),
  ]);
  check('el equipo carga el banco con nivel, tema y respuesta', r.status === 200 && r.json.length === 4 && r.json[2].correctNumber === 0.75, r);
  r = await bank([q('expert', 'Tema', 'Pregunta')]);
  check('un nivel desconocido → 400 con el número de pregunta', r.status === 400 && /Pregunta 1/.test(r.json?.detail), r.json);
  r = await call('GET', '/diagnostics/questions/college');
  check('una etapa desconocida → 404', r.status === 404, r.status);

  // Alumno y profesor
  const student = (await call('POST', '/admin/students', { givenNames: 'Ana', firstSurname: `Diagnostico${tag}`, documentType: 'DNI', documentNumber: `6${tag}1` })).json;
  const token = (await login('student', { username: student.loginIdentifier, password: student.initialPassword })).token;
  const teacher = (await call('POST', '/admin/instructors', { givenNames: 'Luis', firstSurname: `Diagnostico${tag}`, email: `diag.${tag}@example.com` })).json;
  const teacherToken = (await login('admin', { email: teacher.loginIdentifier, password: teacher.initialPassword })).token;
  const me = (method, path, body) => call(method, `/students/me/diagnostics${path}`, body, token);

  r = await me('GET', '');
  const primary = r.json?.find((s) => s.stage === STAGE);
  check('resumen: primaria disponible y sin empezar', r.status === 200 && primary?.available === true && primary.attemptId === null, r.json);

  r = await me('POST', '', { stage: STAGE });
  const attempt = r.json;
  const raw = JSON.stringify(attempt ?? {});
  check('el alumno empieza y ve las preguntas sin la respuesta ni la explicación', r.status === 200 && attempt.total === 4 &&
    !/correctOption|correctNumber|explanation|está después/i.test(raw), raw.slice(0, 200));
  r = await me('POST', '', { stage: STAGE });
  check('empezar otra vez devuelve el mismo diagnóstico', r.json?.attemptId === attempt.attemptId, r.json?.attemptId);
  r = await call('POST', '/students/me/diagnostics', { stage: STAGE }, teacherToken);
  check('un profesor no puede rendirlo → 404', r.status === 404, r.status);

  // Editar el banco no cambia un diagnóstico empezado
  const current = (await call('GET', `/diagnostics/questions/${STAGE}`)).json;
  await bank(current.slice(0, 1));
  r = await me('GET', `/${attempt.attemptId}`);
  check('quitar preguntas del banco no cambia el diagnóstico empezado', r.json?.questions.length === 4, r.json?.questions.length);
  await bank(current);

  // Responder: Prebásico bien, Básico mal → sugiere Básico
  const [a, b, c, d] = attempt.questions;
  const answer = (question, body) => me('POST', `/${attempt.attemptId}/answers`, { questionId: question.id, ...body });
  r = await answer(a, { answer: 'cinco' });
  check('una respuesta ilegible → 400 y no se guarda', r.status === 400, r.status);
  r = await answer(a, { answer: '0' });
  check('sin decir si está bien o mal: solo el avance', r.status === 200 && r.json.answered === 1 && r.json.completed === false &&
    !('isCorrect' in r.json), r.json);
  r = await answer(a, { answer: '1' });
  check('cada pregunta se responde una vez → 409', r.status === 409, r.status);
  await answer(b, { answer: '0' });
  await answer(c, { answer: '0,5' });
  r = await answer(d, { skip: true });
  check('la última respuesta («No lo sé») termina y sugiere Básico', r.status === 200 && r.json.completed === true &&
    r.json.result.suggested.code === 'basic' && r.json.result.suggested.label === 'Básico', r.json);
  check('resultado por tema: Conteo 100 %, Fracciones 0 %', JSON.stringify(r.json?.result.topics.map((t) => t.percent)) === '[100,0]', r.json?.result);
  r = await me('POST', '', { stage: STAGE });
  check('terminado: empezar otra vez devuelve el resultado', r.json?.status === 'completed' && r.json.result?.suggested.code === 'basic', r.json?.status);

  // El profesor revisa y confirma
  const review = (method, path, body) => call(method, `/diagnostics${path}`, body, teacherToken);
  r = await review('GET', `?status=pending&stage=${STAGE}&pageSize=100`);
  const item = r.json?.items?.find((i) => i.attemptId === attempt.attemptId);
  check('el profesor ve el diagnóstico pendiente con el nombre del alumno', r.status === 200 && item?.studentName.startsWith('Ana') &&
    item.suggested.code === 'basic' && item.confirmed === null, item);
  r = await review('GET', `/${attempt.attemptId}`);
  check('detalle: cada respuesta, la correcta y la explicación', r.status === 200 && r.json.answers.length === 4 &&
    r.json.answers[0].isCorrect === true && r.json.answers[0].explanation === '5 está después de 3.' &&
    r.json.answers[3].answered === true && r.json.answers[3].answer === null, r.json?.answers);
  r = await review('POST', `/${attempt.attemptId}/confirm`, { level: 'prebasic' });
  check('el profesor confirma otro nivel', r.status === 200 && r.json.summary.confirmed.code === 'prebasic' &&
    r.json.confirmedByName?.startsWith('Luis'), r.json?.summary);
  r = await review('POST', `/${attempt.attemptId}/confirm`, { level: 'expert' });
  check('un nivel desconocido → 400', r.status === 400, r.status);
  r = await me('GET', '');
  check('el alumno ve el nivel confirmado', r.json?.find((s) => s.stage === STAGE)?.result?.confirmed?.code === 'prebasic', r.json);
  r = await call('GET', '/diagnostics', undefined, token);
  check('un alumno no puede ver la revisión → 403', r.status === 403, r.status);
  r = await review('GET', '?status=raro');
  check('un estado desconocido → 400', r.status === 400, r.status);
} finally {
  // El banco queda como estaba (las preguntas de la prueba quedan archivadas: las usa el diagnóstico de la prueba).
  const restored = await bank(before.map(({ number, ...question }) => question));
  check('el banco de preguntas queda como estaba', restored.status === 200 && restored.json.length === before.length, restored.status);
}

finish();
