import { test, expect } from '@playwright/test'
import { ADMIN_EMAIL, ADMIN_PASSWORD, apiLogin, apiPost, unique } from './helpers'

// Kritik axın 1: admin girişi → kateqoriya yarat → siyahıda görün
test('admin girir, kateqoriya yaradır', async ({ page }) => {
  await page.goto('/admin/')

  // Girişsiz → login səhifəsinə düşür
  await expect(page.getByText('İdarəetmə panelinə giriş')).toBeVisible()

  await page.getByLabel('E-poçt').fill(ADMIN_EMAIL)
  await page.getByLabel('Parol').fill(ADMIN_PASSWORD)
  await page.getByRole('button', { name: 'Daxil ol' }).click()

  // Dashboard açılır
  await expect(page.getByRole('heading', { name: 'Panel' })).toBeVisible()

  // Kateqoriya yarat
  await page.getByRole('link', { name: 'Kateqoriyalar' }).click()
  await page.getByRole('button', { name: 'Yeni kateqoriya' }).click()

  const name = unique('E2E Şezlonq')
  await page.getByLabel('Ad').fill(name)
  await page.getByRole('button', { name: 'Saxla' }).click()

  await expect(page.getByText(name)).toBeVisible()

  // Çıxış → yenidən login səhifəsi
  await page.getByRole('button', { name: 'Çıxış' }).click()
  await expect(page.getByText('İdarəetmə panelinə giriş')).toBeVisible()
})

test.describe('mobil admin', () => {
  test.use({ viewport: { width: 390, height: 844 }, hasTouch: true, isMobile: true })

  test('məlumat kartları görünür və horizontal daşma yoxdur', async ({ page, request }) => {
    await apiLogin(request)
    const categoryName = unique('Mobil mebel')
    const productName = unique('Mobil akasiya skamyası')
    const category = await apiPost(request, '/api/admin/categories', {
      name: categoryName,
      description: null,
      codePrefix: 'MBL',
      parentId: null,
    })
    const product = await apiPost(request, '/api/admin/products', {
      name: productName,
      description: 'Mobil görünüş yoxlaması üçün məhsul',
      categoryId: category.id,
      sku: `MOB-${Date.now().toString(36).toUpperCase()}`,
    })
    await apiPost(request, '/api/admin/qrcodes', {
      targetType: 'product',
      targetId: product.id,
    })

    await page.goto('/admin/')
    await page.getByLabel('E-poçt').fill(ADMIN_EMAIL)
    await page.getByLabel('Parol').fill(ADMIN_PASSWORD)
    await page.getByRole('button', { name: 'Daxil ol' }).click()
    await expect(page.getByRole('heading', { name: 'Panel' })).toBeVisible()

    const expectNoHorizontalOverflow = async () => {
      await expect.poll(() =>
        page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1),
      ).toBe(true)
    }

    await expectNoHorizontalOverflow()
    await page.getByRole('button', { name: 'Menyunu aç' }).click()
    await expect(page.getByRole('link', { name: 'Məhsullar' })).toBeVisible()
    await page.getByRole('link', { name: 'Məhsullar' }).click()

    const productCell = page.locator('td[data-label="Məhsul"]', { hasText: productName })
    await expect(productCell).toBeVisible()
    await expect(productCell).toHaveCSS('display', 'grid')
    await expectNoHorizontalOverflow()

    await page.getByRole('button', { name: 'Menyunu aç' }).click()
    await page.getByRole('link', { name: 'QR kodlar' }).click()
    await expect(page.locator('td[data-label="Hədəf"]', { hasText: productName })).toBeVisible()
    await expect(page.locator('td[data-label="Kod"]').first()).toHaveCSS('display', 'grid')
    await expectNoHorizontalOverflow()

    await page.goto('/admin/kateqoriyalar')
    await expect(page.getByText(categoryName)).toBeVisible()
    await expectNoHorizontalOverflow()

    await page.goto(`/admin/mehsullar/${product.id}`)
    await expect(page.getByLabel('Ad')).toBeVisible()
    await expectNoHorizontalOverflow()
  })
})
