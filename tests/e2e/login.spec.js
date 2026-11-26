const { test, expect } = require("@playwright/test");

test("un administrador inicia sesión y accede a los CRUD", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveURL(/\/account\/login/);
  await expect(page.getByRole("heading", { name: "Bienvenido" })).toBeVisible();

  await page.getByLabel("Correo electrónico").fill("admin-e2e@example.test");
  await page.getByLabel("Contraseña").fill("Admin!Playwright2026");
  await page.getByRole("button", { name: "Iniciar sesión" }).click();

  await expect(page).toHaveURL(/\/dashboard/);
  await expect(page.getByText("Panel administrativo")).toBeVisible();

  await page.getByRole("link", { name: "Usuarios", exact: true }).click();
  await expect(page).toHaveURL(/\/users/);
  await expect(page.getByRole("heading", { name: "Usuarios" })).toBeVisible();
  await expect(page.getByRole("cell", { name: "admin-e2e@example.test" }).first()).toBeVisible();
});
