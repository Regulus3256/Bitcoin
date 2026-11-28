import { createServer } from 'node:http';
import { readFile, mkdtemp, rm } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { tmpdir } from 'node:os';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const jsRoot = resolve(here, '..', '..', 'BtcApi', 'wwwroot', 'js');
const candidates = process.platform === 'win32'
    ? [join(process.env['ProgramFiles(x86)'] ?? '', 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
        join(process.env.ProgramFiles ?? '', 'Google', 'Chrome', 'Application', 'chrome.exe')]
    : ['/usr/bin/chromium', '/usr/bin/chromium-browser', '/usr/bin/google-chrome'];
const browser = process.env.BROWSER_PATH ?? candidates.find(existsSync);
if (!browser) throw new Error('Chromium browser not found. Set BROWSER_PATH to the browser executable.');

const server = createServer(async (request, response) => {
    try {
        const pathname = new URL(request.url, 'http://localhost').pathname;
        const file = pathname === '/' ? join(here, 'index.html') : resolve(jsRoot, `.${pathname.slice(3)}`);
        if (pathname !== '/' && (!pathname.startsWith('/js/') || !file.startsWith(jsRoot + sep))) {
            response.writeHead(404).end();
            return;
        }
        const body = await readFile(file);
        response.writeHead(200, { 'Content-Type': pathname === '/' ? 'text/html; charset=utf-8' : 'text/javascript; charset=utf-8' });
        response.end(body);
    } catch (error) {
        console.error(error);
        response.writeHead(500).end();
    }
});

const profile = await mkdtemp(join(tmpdir(), 'btc-browser-tests-'));
try {
    await new Promise(resolveListen => server.listen(0, '127.0.0.1', resolveListen));
    const url = `http://127.0.0.1:${server.address().port}/`;
    const { stdout } = await promisify(execFile)(browser, [
        '--headless', '--disable-gpu', '--no-first-run', `--user-data-dir=${profile}`,
        '--virtual-time-budget=10000', '--dump-dom', url
    ], { timeout: 30000, maxBuffer: 1024 * 1024 });
    const result = stdout.match(/<pre id="result" data-status="(pass|fail)">([\s\S]*?)<\/pre>/);
    if (!result || result[1] !== 'pass') throw new Error(`Browser tests failed or did not finish:\n${stdout}`);
    console.log(result[2]);
} finally {
    await new Promise(resolveClose => server.close(resolveClose));
    await rm(profile, { recursive: true, force: true });
}
