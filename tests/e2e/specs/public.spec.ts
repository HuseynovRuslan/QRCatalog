import { test, expect } from '@playwright/test'
import {
  ADMIN_EMAIL, ADMIN_PASSWORD, apiGet, apiLogin, apiPost, apiPut, unique,
} from './helpers'

// Kritik axın 2: QR skan → məhsul səhifəsi → sorğu göndər
test('QR səhifəsi açılır, sorğu göndərilir', async ({ page, request }) => {
  // Məzmunu API ilə hazırla (UI-dan sürətli və stabil)
  await apiLogin(request)

  const category = await apiPost(request, '/api/admin/categories', {
    name: unique('E2E Kateqoriya'),
    codePrefix: 'EE',
  })
  const productName = unique('E2E Bahama')
  const product = await apiPost(request, '/api/admin/products', {
    name: productName,
    description: 'E2E test məhsulu',
    categoryId: category.id,
  })
  await apiPost(request, `/api/admin/products/${product.id}/publish`, {})
  const qr = await apiPost<{ id: string; token: string; humanCode: string }>(
    request, '/api/admin/qrcodes', { targetType: 'product', targetId: product.id })

  // QR skanını simulyasiya et
  await page.goto(`/q/${qr.token}`)
  await expect(page.getByRole('heading', { name: productName })).toBeVisible()
  await expect(page.getByText(qr.humanCode)).toBeVisible()

  // Sorğu forması
  await page.getByText('Sorğu göndərin').click()
  await page.getByLabel('Adınız').fill('E2E Müştəri')
  await page.getByLabel('Telefon').fill('+994501234567')
  await page.getByRole('button', { name: 'Göndər' }).click()

  await expect(page.getByText('Sorğunuz qəbul olundu')).toBeVisible()

  // Tanınmayan token izahlı səhifə göstərir (404 status, amma boş səhifə yox)
  const missing = await page.goto('/q/olmayantoken')
  expect(missing!.status()).toBe(404)
  await expect(page.getByText('Kod tapılmadı')).toBeVisible()
})

// Kritik axın 3: İŞÇİ telefonda skan edir → giriş → admin ekranı.
// Bu test bir səhvdən doğdu: «İşçi girişi» linki səhifədə var idi, amma sabit
// əlaqə paneli (.contact-dock, position:fixed) ekranın son ~83px-ni tutduğu üçün
// telefonda ONU HEÇ VAXT GÖRMƏK MÜMKÜN DEYİLDİ — nə qədər aşağı sürüşdürsən də
// panelin altında qalırdı. Ona görə burada linkin mövcudluğu YOX, panelin
// üstündə qalması yoxlanılır: Playwright üçün örtülmüş element də «visible»-dır.
test.describe('Mobil — işçi girişi', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true })

  test('İşçi girişi linki panelin altında qalmır və admin ekranına aparır', async ({ page, request }) => {
    await apiLogin(request)

    // Panel yalnız telefon/WhatsApp təyin olunanda görünür. Təmiz bazada boşdur —
    // ona görə onu QƏSDƏN qururuq, yoxsa test heç nə yoxlamadan yaşıl olardı
    // (ilk dəfə CI belə sındı: dock tapılmadı, boundingBox 30 saniyə gözlədi).
    const original = await apiGet<{ name: string; phone: string | null; whatsappNumber: string | null }>(
      request, '/api/admin/settings')
    await apiPut(request, '/api/admin/settings', {
      name: original.name, phone: '+994501112233', whatsappNumber: '+994501112233',
    })

    try {
      const category = await apiPost(request, '/api/admin/categories', {
        name: unique('E2E Mobil'), codePrefix: 'EM',
      })
      const productName = unique('E2E Skamya')
      const product = await apiPost(request, '/api/admin/products', {
        name: productName, categoryId: category.id,
      })
      await apiPost(request, `/api/admin/products/${product.id}/publish`, {})
      const qr = await apiPost<{ token: string }>(request, '/api/admin/qrcodes',
        { targetType: 'product', targetId: product.id })

      // Qonaq kimi skan — brauzerin öz konteksti, API cookie-si burada yoxdur
      await page.goto(`/q/${qr.token}`)
      const staffLink = page.getByRole('link', { name: 'İşçi girişi' })
      await staffLink.scrollIntoViewIfNeeded()
      await expect(staffLink).toBeVisible()

      const dock = page.locator('.contact-dock')
      await expect(dock, 'əlaqə paneli qurulmadı — örtülmə yoxlanıla bilməz').toBeVisible()

      // Əsl yoxlama: linkin alt kənarı panelin üst kənarından yuxarıda olmalıdır
      const lb = (await staffLink.boundingBox())!
      const db = (await dock.boundingBox())!
      expect(lb.y + lb.height,
        'İşçi girişi linki əlaqə panelinin altında qalır — telefonda görünmür',
      ).toBeLessThanOrEqual(db.y)
      expect(lb.height, 'Barmaq üçün toxunma hədəfi çox kiçikdir').toBeGreaterThanOrEqual(40)

      // Toxunuş → giriş səhifəsi, qayıdış ünvanı qorunur
      await staffLink.click()
      // qayit dekodlanmış halda tam üst-üstə düşməlidir — regex ilə yoxlamaq
      // gizli səhvlərə yol verir (bir dəfə `\?` şablon sətrində sadəcə `?`-a
      // çevrildi və nümunə «logi(n?)qayit» kimi oxundu)
      await page.waitForURL(url =>
        url.pathname === '/admin/login' && url.searchParams.get('qayit') === `/q/${qr.token}`)

      await page.getByLabel('E-poçt').fill(ADMIN_EMAIL)
      await page.getByLabel('Parol').fill(ADMIN_PASSWORD)
      await page.getByRole('button', { name: 'Daxil ol' }).click()

      // Giriş bitəndə HƏMİN səhifəyə qayıdır, girişli olduğu üçün admin ekranı açılır
      await expect(page).toHaveURL(new RegExp(`/admin/mehsullar/${product.id}`))
      await expect(page.getByLabel('Ad')).toHaveValue(productName)
    } finally {
      // Parametrləri geri qaytar — təkrar cəhddə (retry) ilk test masaüstü ölçüdə
      // işləyir və panel oradaki «Göndər» düyməsini örtə bilər
      await apiPut(request, '/api/admin/settings', {
        name: original.name,
        phone: original.phone,
        whatsappNumber: original.whatsappNumber,
      })
    }
  })
})
