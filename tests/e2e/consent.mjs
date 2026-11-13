// Consentimiento del padre para los datos del menor (SCRUM-38): el padre lee el texto vigente y lo acepta por sus
// hijos; queda la fecha y la versión; el administrador ve en la ficha del alumno cómo va; un padre solo acepta por
// sus propios hijos; y aceptar varias veces a la vez no duplica nada.
import { admin, check, finish, login, psql, request as call, tag } from './lib.mjs';

const signIn = async ({ loginIdentifier, initialPassword }) => (await login('admin', { email: loginIdentifier, password: initialPassword })).token;
const guardian = async (name) => (await call('POST', '/admin/guardians', { givenNames: name, firstSurname: `Consent${tag}`, email: `${name.toLowerCase()}.consent.${tag}@example.com` })).json;
let seq = 0;
const student = async (name, guardianUserId) => (await call('POST', '/admin/students', {
  givenNames: name, firstSurname: `Consent${tag}`, documentType: 'DNI', documentNumber: `5${tag}${seq++}`, ...(guardianUserId && { guardianUserId }),
})).json;
const detail = async (user) => (await call('GET', `/admin/users/${user.userId}`)).json;

const mom = await guardian('Rosa');
const dad = await guardian('Luis');
const stranger = await guardian('Carla');
const ana = await student('Ana', mom.userId);     // de los dos padres
const beto = await student('Beto', mom.userId);   // solo de la madre
const eva = await student('Eva', mom.userId);     // solo de la madre, para probar «todo o nada»
const dani = await student('Dani', dad.userId);   // solo del padre
const solo = await student('Solo');               // sin padre vinculado
await call('PUT', `/admin/guardians/${dad.userId}/students/${ana.userId}`);
const momToken = await signIn(mom);
const dadToken = await signIn(dad);
const strangerToken = await signIn(stranger);

// ─── El padre lee el texto y ve a sus hijos pendientes ───────────────
let r = await call('GET', '/families/me/consent', undefined, momToken);
const version = r.json?.text?.version;
check('el padre recibe el texto vigente, con versión y secciones', r.status === 200 && !!version && r.json.text.title.length > 0 &&
  r.json.text.sections.length >= 3 && r.json.text.sections.every((s) => s.heading && s.paragraphs.length > 0), r.json?.text);
check('ve a sus 3 hijos pendientes, por orden alfabético, y no al de otro padre', r.json.children.map((c) => c.fullName.split(' ')[0]).join() === 'Ana,Beto,Eva' &&
  r.json.children.every((c) => c.status === 'pending' && c.acceptedAt === null) && r.json.pending === 3, r.json?.children);
r = await call('GET', '/families/me/consent', undefined, strangerToken);
check('un padre sin hijos recibe el texto y una lista vacía', r.status === 200 && r.json.children.length === 0 && r.json.pending === 0 && !!r.json.text?.version, r);
r = await call('GET', '/families/me/consent', undefined, null);
check('sin token → 401', r.status === 401, r.status);

// ─── El administrador ve cómo va cada alumno ─────────────────────────
let d = await detail(ana);
check('ficha del alumno con padre vinculado: pendiente, con la versión vigente', d.consent?.status === 'pending' && d.consent.currentVersion === version && d.consent.acceptedAt === null, d.consent);
check('ficha del alumno sin padre: «sin padre» (nadie puede dar el consentimiento)', (await detail(solo)).consent?.status === 'no_guardian');
check('ficha del padre: no tiene consentimiento', (await detail(mom)).consent === null);

// ─── Aceptar ─────────────────────────────────────────────────────────
r = await call('POST', '/families/me/consent', { version, studentUserIds: [ana.userId] }, momToken);
const acceptedAt = r.json?.children.find((c) => c.studentUserId === ana.userId)?.acceptedAt;
check('la madre acepta por Ana: queda la fecha y quién', r.status === 200 && r.json.children.find((c) => c.studentUserId === ana.userId).status === 'accepted' &&
  !!acceptedAt && r.json.children.find((c) => c.studentUserId === ana.userId).acceptedByName.startsWith('Rosa'), r.json);
check('Beto sigue pendiente (solo se aceptó por Ana)', r.json.children.find((c) => c.studentUserId === beto.userId).status === 'pending' && r.json.pending === 2);
d = await detail(ana);
check('el administrador ve en la ficha: aceptado, cuándo y por quién', d.consent.status === 'accepted' && d.consent.acceptedAt === acceptedAt &&
  d.consent.acceptedByName.startsWith('Rosa') && typeof d.consent.isProvisional === 'boolean', d.consent);
r = await call('GET', '/families/me/consent', undefined, dadToken);
check('basta con un padre: el otro padre ve a Ana ya aceptada', r.json.children.find((c) => c.studentUserId === ana.userId).status === 'accepted' &&
  r.json.children.find((c) => c.studentUserId === dani.userId).status === 'pending', r.json?.children);

// ─── Repetir no cambia nada ──────────────────────────────────────────
r = await call('POST', '/families/me/consent', { version, studentUserIds: [ana.userId, ana.userId] }, momToken);
check('aceptar otra vez: nada cambia (la fecha sigue siendo la primera)', r.status === 200 &&
  r.json.children.find((c) => c.studentUserId === ana.userId).acceptedAt === acceptedAt, r.json);

// ─── Pedidos simultáneos ─────────────────────────────────────────────
const parallel = await Promise.all(Array.from({ length: 8 }, () => call('POST', '/families/me/consent', { version, studentUserIds: [beto.userId, eva.userId] }, momToken)));
check('8 aceptaciones a la vez: todas responden bien y no hay error por duplicado', parallel.every((x) => x.status === 200), parallel.map((x) => x.status));
check('quedan los 3 hijos aceptados', (await call('GET', '/families/me/consent', undefined, momToken)).json.pending === 0);
const rows = Number(psql(`select count(*) from users.guardian_consents where guardian_user_id = '${mom.userId}' and student_user_id in ('${ana.userId}', '${beto.userId}', '${eva.userId}')`));
check('en la base hay exactamente una aceptación por hijo (3 filas), sin duplicados', rows === 3, rows);
const once = parallel.map((x) => x.json.children.find((c) => c.studentUserId === beto.userId).acceptedAt);
check('todas las respuestas ven la misma fecha: se guardó una sola aceptación', new Set(once).size === 1, once);

// ─── Reglas ──────────────────────────────────────────────────────────
r = await call('POST', '/families/me/consent', { version, studentUserIds: [dani.userId] }, momToken);
check('aceptar por el hijo de otro padre → 404', r.status === 404, r.status);
r = await call('GET', '/families/me/consent', undefined, dadToken);
check('y no se guardó nada para ese hijo', r.json.children.find((c) => c.studentUserId === dani.userId).status === 'pending');
const fresh = await student('Fresca', mom.userId);
r = await call('POST', '/families/me/consent', { version, studentUserIds: [fresh.userId, dani.userId] }, momToken);
check('todo o nada: con un hijo ajeno en la lista no se guarda ninguno → 404', r.status === 404, r.status);
r = await call('GET', '/families/me/consent', undefined, momToken);
check('el hijo propio de esa lista sigue pendiente', r.json.children.find((c) => c.studentUserId === fresh.userId).status === 'pending');
r = await call('POST', '/families/me/consent', { version: 'otra-version', studentUserIds: [fresh.userId] }, momToken);
check('un texto que cambió mientras lo leía → 409 con indicación de recargar', r.status === 409 && /Recarga/.test(r.json?.detail), r);
r = await call('POST', '/families/me/consent', { studentUserIds: [fresh.userId] }, momToken);
check('sin versión → 409', r.status === 409, r.status);
r = await call('POST', '/families/me/consent', { version, studentUserIds: [] }, momToken);
check('sin hijos elegidos → 400', r.status === 400, r.status);
r = await call('POST', '/families/me/consent', { version, studentUserIds: Array.from({ length: 21 }, () => crypto.randomUUID()) }, momToken);
check('demasiados hijos a la vez → 400', r.status === 400, r.status);
r = await call('POST', '/families/me/consent', { version, studentUserIds: [ana.userId] }, strangerToken);
check('un padre que no es de Ana no puede aceptar por ella → 404', r.status === 404, r.status);
const soloLogin = await login('student', { username: solo.loginIdentifier, password: solo.initialPassword });
r = await call('POST', '/families/me/consent', { version, studentUserIds: [solo.userId] }, soloLogin.token);
check('un alumno no puede aceptar por sí mismo → 404', r.status === 404, r.status);
r = await call('POST', '/families/me/consent', { version, studentUserIds: [ana.userId] }, null);
check('aceptar sin token → 401', r.status === 401, r.status);

// ─── Solo cuenta mientras ese padre siga vinculado ───────────────────
await call('DELETE', `/admin/guardians/${mom.userId}/students/${beto.userId}`);
check('si se desvincula a quien aceptó, el alumno queda «sin padre»', (await detail(beto)).consent?.status === 'no_guardian');
await call('PUT', `/admin/guardians/${dad.userId}/students/${beto.userId}`);
d = await detail(beto);
check('con otro padre vinculado, el consentimiento de la madre no vale: pendiente', d.consent.status === 'pending' && d.consent.acceptedAt === null, d.consent);
await call('PUT', `/admin/guardians/${mom.userId}/students/${beto.userId}`);
check('al volver a vincular a la madre, su aceptación vuelve a contar', (await detail(beto)).consent?.status === 'accepted');
check('el administrador no puede aceptar por una familia (no tiene hijos vinculados)', (await call('POST', '/families/me/consent', { version, studentUserIds: [ana.userId] }, admin)).status === 404);

finish();
