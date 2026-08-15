import { test, expect } from '@playwright/test'
import { apiLogin, apiPost, unique } from './helpers'

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
