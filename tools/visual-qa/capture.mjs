import { chromium } from 'playwright';
import { readFile, mkdir } from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '../..');
const routes = JSON.parse(await readFile(path.join(import.meta.dirname, 'routes.json'), 'utf8'));
const output = path.join(root, 'artifacts/visual-qa/screenshots');
const baseURL = (process.env.VISUAL_QA_BASE_URL || 'http://localhost:5000').replace(/\/$/, '');
const storageState = process.env.VISUAL_QA_STORAGE_STATE;
const headless = process.env.VISUAL_QA_HEADLESS !== 'false';
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless });
const context = await browser.newContext({ locale: 'pt-BR', timezoneId: 'UTC', ...(storageState ? { storageState } : {}) });
const page = await context.newPage();

if (!storageState && process.env.VISUAL_QA_EMAIL && process.env.VISUAL_QA_PASSWORD) {
  await page.goto(`${baseURL}/Account/Login`, { waitUntil: 'networkidle' });
  await page.getByLabel('E-mail ou CPF').fill(process.env.VISUAL_QA_EMAIL);
  await page.getByLabel('Senha', { exact: true }).fill(process.env.VISUAL_QA_PASSWORD);
  await page.getByRole('button', { name: 'Entrar' }).click();
  await page.waitForLoadState('networkidle');
}

const sizes = {
  desktop: [{ width: 1366, height: 768, suffix: 'desktop' }, { width: 1920, height: 1080, suffix: 'desktop-1920x1080' }],
  mobile: [{ width: 390, height: 844, suffix: 'mobile' }]
};
let failures = 0;
for (const route of routes) {
  for (const viewport of sizes[route.viewport]) {
    await page.setViewportSize(viewport);
    const response = await page.goto(`${baseURL}${route.path}`, { waitUntil: 'networkidle' });
    if (!response || response.status() >= 500) { console.error(`FAIL ${route.path}: HTTP ${response?.status() ?? 'sem resposta'}`); failures++; continue; }
    if (page.url().includes('/Account/Login')) { console.error(`AUTH ${route.path}: sessão autenticada necessária`); failures++; continue; }
    await page.evaluate(() => document.fonts.ready);
    const baseName = route.name.endsWith('-mobile') ? route.name.slice(0, -7) : route.name;
    const file = path.join(output, `${baseName}-${viewport.suffix}.png`);
    await page.screenshot({ path: file, fullPage: true, animations: 'disabled' });
    console.log(`OK ${route.path} ${viewport.width}x${viewport.height} -> ${path.relative(root, file)}`);
  }
}
await browser.close();
if (failures) process.exitCode = 1;
