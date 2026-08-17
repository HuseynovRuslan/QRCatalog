// Canlı saytdan ekran şəkli — dizaynı gözlə yoxlamaq üçün.
// Konsol xətaları və bloklanmış sorğular da yazılır (CSP pozuntusu səssiz olur).
import { chromium } from '@playwright/test'

const OUT = process.argv[2]
const BASE = 'https://katalog.qrlog.az'

const pages = [
  ['ana', '/', 1280, 900],
  ['kataloq', '/katalog', 1280, 1100],
  ['mehsul', '/p/akasiya-sezlonq-klassik', 1280, 1100],
  ['mobil-mehsul', '/p/akasiya-sezlonq-klassik', 390, 844],
]

const browser = await chromium.launch()
for (const [name, path, width, height] of pages) {
  const context = await browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: 1,
    isMobile: width < 500,
  })
  const page = await context.newPage()
  const problems = []
  page.on('console', message => {
    if (message.type() === 'error') problems.push(`konsol: ${message.text().slice(0, 160)}`)
  })
  page.on('requestfailed', request =>
    problems.push(`sorğu alınmadı: ${request.url().slice(0, 110)} — ${request.failure()?.errorText}`))

  const response = await page.goto(BASE + path, { waitUntil: 'networkidle', timeout: 45000 })
  // reveal animasiyaları scroll ilə işə düşür — hamısını göstər
  await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight))
  await page.waitForTimeout(900)
  await page.evaluate(() => window.scrollTo(0, 0))
  await page.waitForTimeout(600)

  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: false })
  console.log(`${name.padEnd(14)} HTTP ${response.status()}  ${problems.length ? problems.join(' | ') : 'problem yoxdur'}`)
  await context.close()
}
await browser.close()
