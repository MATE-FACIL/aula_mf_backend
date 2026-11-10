// Asignación a grupo (SCRUM-45): los grupos de Matemáticas del nivel que el profesor confirmó en el diagnóstico, el
// cambio de grupo con su registro (quién, cuándo y por qué) y el tope del grupo de destino, también con pedidos
// simultáneos. El banco del diagnóstico es único por etapa: la suite guarda el que haya y lo deja igual.
import { adminUser, check, finish, login, request as call, tag } from './lib.mjs';

const STAGE = 'primary';
const levels = (await call('POST', '/math-levels/provision')).json.levels;
const course = (stage, level) => levels.find((l) => l.stage === stage && l.level === level).courseId;
const teacher = (await call('POST', '/admin/instructors', { givenNames: 'Lucía', firstSurname: `Ubicacion${tag}`, email: `ubicacion.${tag}@example.com` })).json;
const at = (days) => new Date(Date.now() + days * 864e5).toISOString();

async function group(courseId, name, capacity, published = true) {
  const created = (await call('POST', `/courses/${courseId}/offerings`, {
    name: `${name} ${tag}`, maximumCapacity: capacity, basePrice: 180, currencyCode: 'PEN', plannedSessionCount: 1,
    sessions: [{ id: null, startsAt: at(5), endsAt: at(5.06), virtualLink: null }],
  })).json;
  await call('PUT', `/courses/${courseId}/offerings/${created.id}/instructors`, { instructorIds: [teacher.instructorId] });
  if (published) await call('POST', `/courses/${courseId}/offerings/${created.id}/publish`);
  return created.id;
}

let seq = 0;
const newStudent = async (name) => (await call('POST', '/admin/students', {
  givenNames: name, firstSurname: `Ubicacion${tag}`, documentType: 'DNI', documentNumber: `8${tag}${seq++}`,
})).json;
const enroll = async (student, groupId) => (await call('POST', '/enrollments', { studentUserId: student.userId, courseOfferingId: groupId })).json;
const placement = async (student) => (await call('GET', `/enrollments/placement?studentUserId=${student.userId}`)).json;
const seatsOf = async () => Object.fromEntries((await call('GET', '/enrollments/offerings')).json.map((o) => [o.id, o]));
const mine = (groups, ...ids) => ids.map((id) => groups.find((g) => g.offeringId === id));

// Grupos de la prueba: primaria Prebásico (A), primaria Básico (B, C de 1 cupo, borrador), secundaria Básico (S)
const A = await group(course('primary', 'prebasic'), 'Grupo A', 8);
const B = await group(course('primary', 'basic'), 'Grupo B', 8);
const C = await group(course('primary', 'basic'), 'Grupo C', 1);
const Draft = await group(course('primary', 'basic'), 'Borrador', 8, false);
const S = await group(course('secondary', 'basic'), 'Grupo S', 8);

const before = (await call('GET', `/diagnostics/questions/${STAGE}`)).json ?? [];
try {
  const ana = await newStudent('Ana');
  const seat = await enroll(ana, A);

  // ─── Antes del diagnóstico ───────────────────────────────────────
  let r = await call('GET', `/enrollments/placement?studentUserId=${ana.userId}`);
  let p = r.json;
  check('sin diagnóstico: no se recomienda ningún grupo y se ven sus plazas', r.status === 200 && p.diagnostics.length === 0 &&
    p.groups.every((g) => !g.recommended) && p.enrollments.some((e) => e.offeringId === A && e.status === 'pending'), r);
  const [gA, gB, gC, gS] = mine(p.groups, A, B, C, S);
  check('cada grupo trae su nivel, su cupo y si ya está matriculado', gA.alreadyEnrolled === true && gA.freeSeats === 7 &&
    gB.freeSeats === 8 && gC.maximumCapacity === 1 && gB.stageLabel === 'Primaria' && gB.levelLabel === 'Básico' &&
    gS.stageLabel === 'Secundaria', [gA, gB, gS]);
  check('los grupos en borrador no se ofrecen', !p.groups.some((g) => g.offeringId === Draft), p.groups.length);

  // ─── El alumno rinde el diagnóstico: Prebásico bien, Básico mal → sugiere Básico ──
  const q = (level, topic, prompt) => ({ level, topic, prompt, kind: 'multiple_choice', options: ['Sí', 'No'], correctOption: 0 });
  await call('PUT', `/diagnostics/questions/${STAGE}`, { questions: [
    q('prebasic', `Conteo ${tag}`, '¿5 es mayor que 3?'), q('prebasic', `Conteo ${tag}`, '¿10 es par?'),
    q('basic', `Fracciones ${tag}`, '¿1/2 es igual a 2/4?'), q('basic', `Fracciones ${tag}`, '¿1/3 es mayor que 1/2?'),
  ] });
  const token = (await login('student', { username: ana.loginIdentifier, password: ana.initialPassword })).token;
  const attempt = (await call('POST', '/students/me/diagnostics', { stage: STAGE }, token)).json;
  for (const [i, question] of attempt.questions.entries()) {
    await call('POST', `/students/me/diagnostics/${attempt.attemptId}/answers`, { questionId: question.id, answer: i < 2 ? '0' : '1' }, token);
  }
  p = await placement(ana);
  check('terminado sin confirmar: se muestra el nivel sugerido pero no se recomienda nada', p.diagnostics[0]?.suggested?.code === 'basic' &&
    p.diagnostics[0].confirmed === null && p.groups.every((g) => !g.recommended), p.diagnostics);

  // ─── El profesor confirma Básico ─────────────────────────────────
  await call('POST', `/diagnostics/${attempt.attemptId}/confirm`, { level: 'basic' });
  p = await placement(ana);
  const [rB, rC, rA, rS] = mine(p.groups, B, C, A, S);
  check('confirmado: se recomiendan los grupos de primaria Básico', p.diagnostics[0].confirmed.code === 'basic' && p.diagnostics[0].attemptId === attempt.attemptId && rB.recommended && rC.recommended &&
    !rA.recommended && !rS.recommended, p.groups.filter((g) => g.recommended).length);
  const firstOther = p.groups.findIndex((g) => !g.recommended);
  check('los recomendados van primero', firstOther === -1 || p.groups.slice(firstOther).every((g) => !g.recommended), p.groups.map((g) => g.recommended));

  r = await call('GET', `/enrollments/placement?studentUserId=${adminUser}`);
  check('solo se ubica a alumnos → 400', r.status === 400, r.status);
  r = await call('GET', '/enrollments/placement?studentUserId=00000000-0000-4000-8000-000000000099');
  check('una persona que no existe → 404', r.status === 404, r.status);
  r = await call('GET', `/enrollments/placement?studentUserId=${ana.userId}`, undefined, token);
  check('un alumno no puede usar la ubicación → 403', r.status === 403, r.status);

  // ─── Cambio de grupo ─────────────────────────────────────────────
  r = await call('POST', `/enrollments/${seat.id}/transfer`, { courseOfferingId: B, reason: ' El diagnóstico confirmó Básico ' });
  const moved = r.json;
  check('el administrador la pasa al grupo recomendado', r.status === 200 && moved.fromOfferingName === `Grupo A ${tag}` &&
    moved.toOfferingName === `Grupo B ${tag}` && moved.reason === 'El diagnóstico confirmó Básico' && moved.studentName.startsWith('Ana'), r);
  let seats = await seatsOf();
  check('el cupo de A se libera y el de B se ocupa', seats[A].takenSeats === 0 && seats[B].takenSeats === 1, [seats[A].takenSeats, seats[B].takenSeats]);
  const history = (await call('GET', `/enrollments?studentUserId=${ana.userId}&pageSize=100`)).json.items;
  check('la matrícula anterior queda retirada y la nueva pendiente', history.find((e) => e.id === seat.id)?.status === 'withdrawn' &&
    history.find((e) => e.id === moved.toEnrollmentId)?.status === 'pending', history.map((e) => e.status));
  r = await call('GET', `/enrollments/transfers?studentUserId=${ana.userId}`);
  check('el cambio queda registrado con quién, cuándo y por qué', r.status === 200 && r.json.total === 1 && r.json.items[0].reason === 'El diagnóstico confirmó Básico' &&
    r.json.items[0].fromEnrollmentId === seat.id && r.json.items[0].toEnrollmentId === moved.toEnrollmentId && r.json.items[0].fromOfferingId === A && r.json.items[0].toOfferingId === B && !!r.json.items[0].transferredAt, r.json);
  r = await call('GET', `/enrollments/transfers?courseOfferingId=${B}`);
  check('se puede buscar por el grupo al que entró o del que salió', r.json.total === 1 && (await call('GET', `/enrollments/transfers?courseOfferingId=${A}`)).json.total === 1, r.json.total);

  // Reglas
  r = await call('POST', `/enrollments/${seat.id}/transfer`, { courseOfferingId: C });
  check('la matrícula ya retirada no se puede mover → 409', r.status === 409, r.status);
  r = await call('POST', `/enrollments/${moved.toEnrollmentId}/transfer`, { courseOfferingId: B });
  check('al mismo grupo → 400', r.status === 400, r.status);
  r = await call('POST', `/enrollments/${moved.toEnrollmentId}/transfer`, { courseOfferingId: Draft });
  check('a un grupo en borrador → 409', r.status === 409, r.status);
  r = await call('POST', `/enrollments/${moved.toEnrollmentId}/transfer`, { courseOfferingId: '00000000-0000-4000-8000-000000000099' });
  check('a un grupo que no existe → 404', r.status === 404, r.status);
  r = await call('POST', '/enrollments/00000000-0000-4000-8000-000000000099/transfer', { courseOfferingId: A });
  check('una matrícula que no existe → 404', r.status === 404, r.status);
  r = await call('POST', `/enrollments/${moved.toEnrollmentId}/transfer`, { courseOfferingId: A, reason: 'x'.repeat(301) });
  check('un motivo demasiado largo → 400', r.status === 400, r.status);
  r = await call('POST', `/enrollments/${moved.toEnrollmentId}/transfer`, { courseOfferingId: A }, token);
  check('un alumno no puede cambiar grupos → 403', r.status === 403, r.status);

  // Tope: C tiene 1 cupo y lo ocupa Luis
  const luis = await newStudent('Luis');
  await enroll(luis, C);
  r = await call('POST', `/enrollments/${moved.toEnrollmentId}/transfer`, { courseOfferingId: C });
  check('al grupo lleno → 409 y nada cambia', r.status === 409 && /lleno/.test(r.json?.detail), r);
  seats = await seatsOf();
  check('Ana sigue en B y C sigue con 1 de 1', seats[B].takenSeats === 1 && seats[C].takenSeats === 1, [seats[B].takenSeats, seats[C].takenSeats]);
  r = await call('POST', '/enrollments', { studentUserId: ana.userId, courseOfferingId: B });
  check('matricular a quien ya está en el grupo sigue dando 409', r.status === 409, r.status);

  // Una matrícula activa sigue activa, con quién confirmó el pago
  await call('POST', `/enrollments/${moved.toEnrollmentId}/activate`, { paymentNote: 'Yape 12/11' });
  const activeBefore = (await call('GET', `/enrollments?studentUserId=${ana.userId}&status=active`)).json.items[0];
  r = await call('POST', `/enrollments/${moved.toEnrollmentId}/transfer`, { courseOfferingId: A, reason: 'Pidió el horario de A' });
  const activeAfter = (await call('GET', `/enrollments?studentUserId=${ana.userId}&status=active`)).json.items[0];
  check('una matrícula activa sigue activa y conserva el pago confirmado', r.status === 200 && activeAfter.offeringName === `Grupo A ${tag}` &&
    activeAfter.paymentNote === 'Yape 12/11' && activeAfter.activatedAt === activeBefore.activatedAt, [activeBefore, activeAfter]);
  check('Ana tiene un solo cupo vivo', (await call('GET', `/enrollments?studentUserId=${ana.userId}&pageSize=100`)).json.items.filter((e) => e.status !== 'withdrawn').length === 1);

  // ─── Pedidos simultáneos ─────────────────────────────────────────
  // Seis alumnos de A piden pasar al mismo grupo de 1 cupo: solo uno entra.
  const X = await group(course('primary', 'basic'), 'Grupo X', 1);
  const racers = await Promise.all(['R1', 'R2', 'R3', 'R4', 'R5', 'R6'].map(newStudent));
  const racerSeats = await Promise.all(racers.map((s) => enroll(s, A)));
  const results = await Promise.all(racerSeats.map((s) => call('POST', `/enrollments/${s.id}/transfer`, { courseOfferingId: X })));
  const ok = results.filter((x) => x.status === 200).length;
  seats = await seatsOf();
  check('6 cambios simultáneos a un grupo de 1 cupo: entra solo 1', ok === 1 && results.filter((x) => x.status === 409).length === 5 &&
    seats[X].takenSeats === 1, results.map((x) => x.status));
  check('los otros 5 siguen en A, sin perder su cupo', seats[A].takenSeats === 1 + 5, seats[A].takenSeats);

  // Dos cambios de la MISMA matrícula a grupos distintos: solo uno se aplica.
  const Y = await group(course('primary', 'basic'), 'Grupo Y', 8);
  const Z = await group(course('primary', 'basic'), 'Grupo Z', 8);
  const twin = await newStudent('Gemela');
  const twinSeat = await enroll(twin, A);
  const both = await Promise.all([Y, Z, Y, Z].map((g) => call('POST', `/enrollments/${twinSeat.id}/transfer`, { courseOfferingId: g })));
  const live = (await call('GET', `/enrollments?studentUserId=${twin.userId}&pageSize=100`)).json.items.filter((e) => e.status !== 'withdrawn');
  check('4 cambios simultáneos de la misma matrícula: se aplica uno y queda un solo cupo', both.filter((x) => x.status === 200).length === 1 &&
    both.filter((x) => x.status === 409).length === 3 && live.length === 1, [both.map((x) => x.status), live.length]);
  check('y se registra un solo cambio', (await call('GET', `/enrollments/transfers?studentUserId=${twin.userId}`)).json.total === 1);
} finally {
  const restored = await call('PUT', `/diagnostics/questions/${STAGE}`, { questions: before });
  check('el banco de preguntas queda como estaba', restored.status === 200 && restored.json.length === before.length, restored.status);
}

finish();
