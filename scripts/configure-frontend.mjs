import { execFileSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const configPath = resolve(dirname(fileURLToPath(import.meta.url)), '../src/assets/runtime-config.js');

if (process.argv[2] === '--reset') {
  writeFileSync(configPath, "window.employeeDocumentAssistantConfig = { apiBaseUrl: '' };\n");
} else {
  const apiUrl = execFileSync('azd', ['env', 'get-value', 'API_URL'], { encoding: 'utf8' }).trim();
  const endpoint = new URL(apiUrl);

  if (endpoint.protocol !== 'https:' || endpoint.pathname !== '/' || endpoint.search || endpoint.hash) {
    throw new Error('API_URL must be an HTTPS origin without a path, query, or fragment.');
  }

  writeFileSync(
    configPath,
    `window.employeeDocumentAssistantConfig = ${JSON.stringify({ apiBaseUrl: endpoint.origin })};\n`
  );
}
