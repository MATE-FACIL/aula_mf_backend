// Corre todas las pruebas contra la API real. La API debe estar levantada: docker compose up -d --build
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

let failed = 0;
for (const name of ['groups', 'enrollments', 'classes', 'family', 'lessons', 'attendance', 'levels', 'files', 'practice', 'teacher']) {
  console.log(`\n=== ${name}`);
  const { status } = spawnSync(process.execPath, [fileURLToPath(new URL(`./${name}.mjs`, import.meta.url))], { stdio: 'inherit' });
  if (status !== 0) failed++;
}
console.log(failed ? `\n${failed} suites con fallos` : '\nTodas las suites pasaron');
process.exit(failed ? 1 : 0);
