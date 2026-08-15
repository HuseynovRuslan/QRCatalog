import { test, expect } from '@playwright/test'
import { ADMIN_EMAIL, ADMIN_PASSWORD, unique } from './helpers'

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
