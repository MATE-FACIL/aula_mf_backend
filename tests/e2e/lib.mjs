// Utilidades de las pruebas contra la API real en Docker. Ver docs/pruebas/README.md.
import { execFileSync } from 'node:child_process';
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

export const login = async (kind, credentials) => {
  const res = await fetch(`${api}/auth/${kind}/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(credentials) });
  return { status: res.status, token: (await res.json()).accessToken, cookies: res.headers.getSetCookie() };
};

let failures = 0;
export const check = (name, ok, detail) => {
  console.log(`${ok ? 'OK  ' : 'FAIL'} ${name}${ok ? '' : ` → ${JSON.stringify(detail)}`}`);
  if (!ok) failures++;
};
export const finish = () => {
  console.log(failures ? `\n${failures} fallos` : '\nTodo OK');
  process.exit(failures ? 1 : 0);
};
