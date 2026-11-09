// Utilidades de las pruebas contra la API real en Docker. Ver docs/pruebas/README.md.
import { execFileSync } from 'node:child_process';
import http from 'node:http';
import { createHmac, randomUUID } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const fileEnv = existsSync(`${root}.env`) ? readFileSync(`${root}.env`, 'utf8') : '';
const setting = (name, fallback) => process.env[name] || fileEnv.match(new RegExp(`^${name}=(.*)$`, 'm'))?.[1].trim() || fallback;

const signingKey = setting('JWT_SIGNING_KEY');
if (!signingKey) throw new Error('Falta JWT_SIGNING_KEY: copia .env.example a .env.');
const api = `http://localhost:${setting('API_PORT', '5087')}/api/v1`;

/** Primer administrador de la base. El rol se lee en cada petición, así que basta con firmar su identidad. */
function findAdministrator() {
  const sql = `select a.id, a.user_id from auth.auth_accounts a
    join "authorization".user_roles ur on ur.user_id = a.user_id
    join "authorization".roles r on r.id = ur.role_id
    where r.code = 'administrator' limit 1`;
  const output = execFileSync('docker', ['compose', 'exec', '-T', 'postgres', 'psql',
    '-U', setting('POSTGRES_USER', 'aula'), '-d', setting('POSTGRES_DB', 'aula_virtual_dev'), '-At', '-F', ' ', '-c', sql],
  { cwd: root, encoding: 'utf8' }).trim();
  if (!output) throw new Error('No hay administrador: ejecuta sh tests/e2e/setup-admin.sh.');
  const [account, user] = output.split(' ');
  return { account, user };
}

const b64 = (value) => Buffer.from(JSON.stringify(value)).toString('base64url');

export function signToken(account, user) {
  const now = Math.floor(Date.now() / 1000);
  const head = b64({ alg: 'HS256', typ: 'JWT' });
  const body = b64({ sub: account, user_id: user, jti: randomUUID(), iat: now, exp: now + 900, iss: 'AulaVirtual.Api', aud: 'AulaVirtual.Web' });
  const signature = createHmac('sha256', Buffer.from(signingKey, 'utf8')).update(`${head}.${body}`).digest('base64url');
  return `${head}.${body}.${signature}`;
}

const administrator = findAdministrator();
export const admin = signToken(administrator.account, administrator.user);
export const adminUser = administrator.user;
export const tag = Date.now().toString().slice(-6);

export async function request(method, path, payload, token = admin) {
  const res = await fetch(api + path, {
    method,
    headers: {
      Accept: 'application/json',
      ...(token && { Authorization: `Bearer ${token}` }),
      ...(payload !== undefined && { 'Content-Type': 'application/json' }),
    },
    body: payload === undefined ? undefined : JSON.stringify(payload),
  });
  const text = await res.text();
  let json;
  try { json = text ? JSON.parse(text) : undefined; } catch { json = text; }
  return { status: res.status, json, headers: res.headers };
}

const LOGIN_WINDOW_MS = 61_000;

/**
 * Real login. The API allows 10 logins per minute per IP (fixed window), so on 429 this waits for the next
 * window instead of skipping the limit. Any other failure stops the suite: a missing token must never fall
 * back to the administrator's (request() uses the admin token when none is given).
 */
export async function login(kind, credentials) {
  for (let attempt = 1; ; attempt++) {
    const res = await fetch(`${api}/auth/${kind}/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(credentials) });
    if (res.status === 429 && attempt <= 2) {
      console.log('     (límite de ingresos de la API: espero la siguiente ventana)');
      await new Promise((done) => setTimeout(done, LOGIN_WINDOW_MS));
      continue;
    }
    const body = await res.json().catch(() => ({}));
    if (res.status !== 200 || !body.accessToken) throw new Error(`Login ${kind} falló con ${res.status}`);
    return { status: res.status, token: body.accessToken, cookies: res.headers.getSetCookie() };
  }
}

let failures = 0;
export const check = (name, ok, detail) => {
  console.log(`${ok ? 'OK  ' : 'FAIL'} ${name}${ok ? '' : ` → ${JSON.stringify(detail)}`}`);
  if (!ok) failures++;
};
// Sets the exit code instead of calling process.exit(): on Windows, exiting while sockets are still closing can
// crash Node (libuv assertion) and turn a passing suite into a failure.
export const finish = () => {
  console.log(failures ? `\n${failures} fallos` : '\nTodo OK');
  process.exitCode = failures ? 1 : 0;
};

/**
 * Request to a signed storage URL. The signature covers the host name, so it is kept as is; only the connection
 * goes to 127.0.0.1, where docker-compose.storage.yml publishes the local server (never to the whole network).
 */
export function storageRequest(url, { method = 'GET', headers = {}, body } = {}) {
  const target = new URL(url);
  return new Promise((resolve, reject) => {
    const req = http.request(target, {
      method,
      headers: { ...headers, ...(body && { 'Content-Length': body.length }) },
      lookup: (hostname, options, callback) =>
        options?.all ? callback(null, [{ address: '127.0.0.1', family: 4 }]) : callback(null, '127.0.0.1', 4),
    }, (res) => {
      const chunks = [];
      res.on('data', (chunk) => chunks.push(chunk));
      res.on('end', () => resolve({ status: res.statusCode, body: Buffer.concat(chunks) }));
    });
    req.on('error', reject);
    if (body) req.write(body);
    req.end();
  });
}

// ── Almacenamiento local: limpieza de lo que suben las pruebas ─────────
// Se usa el curl del propio contenedor de almacenamiento (firma SigV4), así no hace falta ningún SDK.
const storageCompose = ['compose', '-f', 'docker-compose.yml', '-f', 'docker-compose.storage.yml', 'exec', '-T', 's3', 'sh', '-c'];
const storageCredentials = `${process.env.LOCAL_S3_ACCESS_KEY || 'aula-local'}:${process.env.LOCAL_S3_SECRET_KEY || 'aula-local-secret'}`;
const signedCurl = (args) => `curl -s --aws-sigv4 "aws:amz:us-east-1:s3" --user "${storageCredentials}" ${args}`;

/** Claves de los objetos bajo `prefix` en los dos buckets locales. */
export function storageObjects(prefix) {
  const keys = [];
  for (const bucket of ['aula-private', 'aula-public']) {
    const xml = execFileSync('docker', [...storageCompose,
      signedCurl(`"http://localhost:9000/${bucket}?list-type=2&prefix=${encodeURIComponent(prefix)}"`)],
    { cwd: root, encoding: 'utf8' });
    for (const [, key] of xml.matchAll(/<Key>([^<]+)<\/Key>/g)) keys.push({ bucket, key });
  }
  return keys;
}

/** Borra todo lo que haya bajo `prefix`. Devuelve cuántos objetos encontró (0 = la API ya había limpiado). */
export function deleteStorageObjects(prefix) {
  const found = storageObjects(prefix);
  for (const { bucket, key } of found) {
    execFileSync('docker', [...storageCompose, signedCurl(`-X DELETE "http://localhost:9000/${bucket}/${key}"`)], { cwd: root });
  }
  return found.length;
}
