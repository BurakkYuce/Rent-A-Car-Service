// Production derlemesini /app/ altında servis eden küçük statik sunucu (yalnız yerel e2e için).
// Web uygulamasının CSP'sini birebir uygular: inline script ya da harici host varsa test kırılır.
import { readFile, stat } from 'node:fs/promises';
import { createServer } from 'node:http';
import { extname, join, resolve, sep } from 'node:path';

const PORT = Number(process.argv[2] ?? 4321);
const KOK = resolve(import.meta.dirname, '../dist/rentacar-frontend/browser');
const ONEK = '/app/';

const CSP =
  "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
  "font-src 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'self'; " +
  "base-uri 'self'; object-src 'none'";

const TURLER = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.ico': 'image/x-icon',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.json': 'application/json',
  '.woff2': 'font/woff2',
  '.txt': 'text/plain; charset=utf-8',
};

async function dosyaMi(yol) {
  try {
    return (await stat(yol)).isFile();
  } catch {
    return false;
  }
}

createServer(async (istek, yanit) => {
  const yol = new URL(istek.url ?? '/', 'http://yerel').pathname;
  if (yol === '/app') {
    yanit.writeHead(301, { Location: ONEK }).end();
    return;
  }
  if (!yol.startsWith(ONEK)) {
    yanit.writeHead(404).end();
    return;
  }

  const goreli = decodeURIComponent(yol.slice(ONEK.length));
  let hedef = resolve(join(KOK, goreli));
  if (hedef !== KOK && !hedef.startsWith(KOK + sep)) {
    yanit.writeHead(400).end();
    return;
  }
  if (!(await dosyaMi(hedef))) {
    // SPA geri dönüşü: uzantısız yollar kabuğa düşer, eksik varlıklar 404 kalır.
    if (extname(goreli) !== '') {
      yanit.writeHead(404).end();
      return;
    }
    hedef = join(KOK, 'index.html');
  }

  const govde = await readFile(hedef);
  yanit
    .writeHead(200, {
      'Content-Type': TURLER[extname(hedef)] ?? 'application/octet-stream',
      'Content-Security-Policy': CSP,
      'X-Content-Type-Options': 'nosniff',
    })
    .end(govde);
}).listen(PORT, '127.0.0.1', () => console.log(`e2e sunucusu: http://127.0.0.1:${PORT}${ONEK}`));
