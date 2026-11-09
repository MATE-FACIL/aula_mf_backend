// Archivos de las lecciones (SCRUM-58): subida directa al almacenamiento, adjuntar, descargar solo con matrícula
// activa, límites de tipo y tamaño, y limpieza de archivos reemplazados o borrados.
// Necesita almacenamiento: docker compose -f docker-compose.yml -f docker-compose.storage.yml up -d --build
// Sin él, la suite se omite (la API responde 503 a las subidas).
// Pase o falle, al terminar no queda ningún archivo de la prueba en el almacenamiento.
import { check, deleteStorageObjects, finish, login, request as call, storageObjects, storageRequest, tag } from './lib.mjs';

const course = (await call('POST', '/courses', { title: `Archivos ${tag}` })).json;
const base = `/courses/${course.id}/content`;
const unit = (await call('POST', `${base}/units`, { title: 'Unidad 1' })).json;
const lesson = (await call('POST', `${base}/units/${unit.id}/lessons`, { title: 'Sumar fracciones', isPublished: true, materials: [] })).json;

const grant = (kind, contentType, size) => call('POST', `${base}/lessons/${lesson.id}/uploads`, { kind, contentType, size });
const upload = async (granted, bytes) => (await storageRequest(granted.url, { method: granted.method, headers: granted.headers, body: bytes })).status;
const saveLesson = (fields) => call('PUT', `${base}/lessons/${lesson.id}`, {
  unitId: unit.id, title: 'Sumar fracciones', summary: null, videoUrl: null, isPublished: true, materials: [], ...fields,
});

const pdf = Buffer.from(`%PDF-1.4\n% Guía de práctica ${tag}\n`);
let r = await grant('material', 'application/pdf', pdf.length);
const skipped = r.status === 503;
if (skipped) console.log('OMITIDA: este ambiente no tiene almacenamiento de archivos (usa docker-compose.storage.yml).');

if (!skipped) try {
  const guide = r.json;
  check('permiso de subida para un PDF', r.status === 201 && guide.method === 'PUT' && guide.fileId, r);
  check('la subida va directo al almacenamiento', (await upload(guide, pdf)) === 200);

  // Límites
  r = await grant('material', 'application/x-msdownload', 100);
  check('tipo no permitido → 400', r.status === 400, r.status);
  r = await grant('material', 'application/pdf', 60 * 1024 * 1024);
  check('material de más de 50 MB → 400', r.status === 400, r.status);
  r = await grant('video', 'video/mp4', 3 * 1024 * 1024 * 1024);
  check('video de más de 2 GB → 400', r.status === 400, r.status);
  r = await saveLesson({ materials: [{ title: 'Fantasma', fileId: '00000000-0000-4000-8000-000000000001' }] });
  check('un archivo que nunca se subió → 400', r.status === 400 && /vuelve a subirlo/.test(r.json?.detail), r);

  // Adjuntar el PDF y un video
  const mp4 = Buffer.alloc(2048, 7);
  const video = (await grant('video', 'video/mp4', mp4.length)).json;
  await upload(video, mp4);
  r = await saveLesson({ videoFileId: video.fileId, materials: [{ title: 'Guía de práctica', fileId: guide.fileId }, { title: 'Pizarra', url: 'https://cdn.example.com/pizarra.png' }] });
  const saved = r.json;
  check('la lección guarda video, archivo y enlace', r.status === 200 && saved.videoFileId === video.fileId &&
    saved.materials[0].size === pdf.length && saved.materials[1].url === 'https://cdn.example.com/pizarra.png', r);

  // Lo que ve el alumno
  const at = (days) => new Date(Date.now() + days * 864e5).toISOString();
  const teacher = (await call('POST', '/admin/instructors', { givenNames: 'Lucía', firstSurname: `Archivos${tag}`, email: `archivos.${tag}@example.com` })).json;
  const group = (await call('POST', `/courses/${course.id}/offerings`, {
    name: 'Grupo A', maximumCapacity: 8, basePrice: 100, currencyCode: 'PEN', plannedSessionCount: 1,
    sessions: [{ id: null, startsAt: at(3), endsAt: at(3.05), virtualLink: null }],
  })).json;
  await call('PUT', `/courses/${course.id}/offerings/${group.id}/instructors`, { instructorIds: [teacher.instructorId] });
  await call('POST', `/courses/${course.id}/offerings/${group.id}/publish`);
  const student = async (name, doc) => (await call('POST', '/admin/students', { givenNames: name, firstSurname: `Archivos${tag}`, documentType: 'DNI', documentNumber: doc })).json;
  const active = await student('Ana', `9${tag}1`);
  const pending = await student('Luis', `9${tag}2`);
  const seat = (await call('POST', '/enrollments', { studentUserId: active.userId, courseOfferingId: group.id })).json;
  await call('POST', '/enrollments', { studentUserId: pending.userId, courseOfferingId: group.id });
  await call('POST', `/enrollments/${seat.id}/activate`, {});
  const activeToken = (await login('student', { username: active.loginIdentifier, password: active.initialPassword })).token;
  const pendingToken = (await login('student', { username: pending.loginIdentifier, password: pending.initialPassword })).token;

  r = await call('GET', `/students/me/courses/${course.id}/lessons`, undefined, activeToken);
  const seen = r.json?.units?.[0]?.lessons?.[0];
  const guideUrl = seen?.materials?.[0]?.url;
  check('el alumno matriculado recibe enlaces firmados que vencen en 15 min', r.status === 200 && /X-Amz-Expires=900/.test(guideUrl ?? '') && /X-Amz-Signature=/.test(seen?.videoUrl ?? ''), seen);
  let download = await storageRequest(guideUrl);
  check('el enlace firmado descarga el archivo', download.status === 200 && download.body.equals(pdf), download.status);
  r = await call('GET', `/students/me/courses/${course.id}/lessons`, undefined, pendingToken);
  check('sin matrícula activa no hay enlaces → 404', r.status === 404, r.status);
  const unsigned = guideUrl?.split('?')[0];
  download = await storageRequest(unsigned);
  check('el archivo sin firma no se puede descargar', download.status === 403 || download.status === 400, download.status);

  // Reemplazar y borrar: los archivos que quedan sin uso se eliminan
  const newPdf = Buffer.from(`%PDF-1.4\n% Guía nueva ${tag}\n`);
  const replacement = (await grant('material', 'application/pdf', newPdf.length)).json;
  await upload(replacement, newPdf);
  r = await saveLesson({ videoFileId: video.fileId, materials: [{ title: 'Guía nueva', fileId: replacement.fileId }] });
  check('reemplazar el archivo', r.status === 200, r);
  download = await storageRequest(guideUrl);
  check('el archivo reemplazado ya no existe', download.status === 404, download.status);
  const videoUrl = (await call('GET', `/students/me/courses/${course.id}/lessons`, undefined, activeToken)).json.units[0].lessons[0].videoUrl;
  r = await call('DELETE', `${base}/lessons/${lesson.id}`);
  download = await storageRequest(videoUrl);
  check('borrar la lección elimina sus archivos', r.status === 204 && download.status === 404, [r.status, download.status]);
} finally {
  // Borrar la lección elimina sus archivos; después se revisa el almacenamiento y se borra lo que haya quedado
  // (por ejemplo, un archivo subido que nunca se adjuntó porque la prueba falló antes).
  await call('DELETE', `${base}/lessons/${lesson.id}`);
  const prefix = `courses/${course.id.replaceAll('-', '')}/`;
  const leftover = deleteStorageObjects(prefix);
  check('la API no dejó archivos de la prueba en el almacenamiento', leftover === 0, `${leftover} objetos (ya borrados)`);
  check('al terminar no queda ningún archivo de la prueba', storageObjects(prefix).length === 0);
}

if (!skipped) finish();
