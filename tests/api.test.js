const { randomUUID } = require("node:crypto");
const { spawn } = require("node:child_process");
const fs = require("node:fs");
const http = require("node:http");
const path = require("node:path");

const port = 5291;
const baseUrl = `http://127.0.0.1:${port}`;
const databasePath = path.join(__dirname, "webapp.test.db");
const dataProtectionKeysPath = path.join(__dirname, "data-protection-keys");
const observabilityPort = 5292;
const observabilityBaseUrl = `http://127.0.0.1:${observabilityPort}`;
const observabilityDatabasePath = path.join(__dirname, "observability.test.db");
const observabilityKeysPath = path.join(__dirname, "observability-data-protection-keys");
const adminEmail = "admin-test@example.test";
const operatorEmail = "operator-test@example.test";
const adminPassword = `Admin!${randomUUID()}aA1`;
const operatorPassword = `Operator!${randomUUID()}aA1`;
const managedUserPassword = `Managed!${randomUUID()}aA1`;
let apiProcess;

class Session {
  constructor() {
    this.cookies = new Map();
  }

  store(response) {
    const setCookies = response.headers.getSetCookie?.() ?? [];
    for (const value of setCookies) {
      const [cookie] = value.split(";", 1);
      const separator = cookie.indexOf("=");
      if (separator < 1) continue;

      const name = cookie.slice(0, separator);
      const cookieValue = cookie.slice(separator + 1);
      if (/max-age=0/i.test(value)) this.cookies.delete(name);
      else this.cookies.set(name, cookieValue);
    }
  }

  header() {
    return [...this.cookies.entries()].map(([name, value]) => `${name}=${value}`).join("; ");
  }
}

async function request(pathname, options = {}, session) {
  const headers = { "Content-Type": "application/json", ...options.headers };
  if (session?.header()) headers.Cookie = session.header();

  const response = await fetch(`${baseUrl}${pathname}`, {
    ...options,
    headers,
    redirect: options.redirect ?? "manual"
  });
  session?.store(response);

  const contentType = response.headers.get("content-type") ?? "";
  const body = contentType.includes("application/json")
    ? await response.json()
    : await response.arrayBuffer();

  return { response, body };
}

async function antiforgeryToken(session) {
  const { response, body } = await request("/api/auth/antiforgery", {}, session);
  expect(response.status).toBe(200);
  expect(typeof body.requestToken).toBe("string");
  return body.requestToken;
}

async function login(session, email, password) {
  const token = await antiforgeryToken(session);
  return request("/api/auth/login", {
    method: "POST",
    headers: { "X-CSRF-TOKEN": token },
    body: JSON.stringify({ email, password })
  }, session);
}

async function authenticatedRequest(session, pathname, options = {}) {
  const isUnsafe = ["POST", "PUT", "PATCH", "DELETE"].includes((options.method ?? "GET").toUpperCase());
  const headers = { ...options.headers };
  if (isUnsafe) headers["X-CSRF-TOKEN"] = await antiforgeryToken(session);
  return request(pathname, { ...options, headers }, session);
}

function startApi() {
  return new Promise((resolve, reject) => {
    let output = "";
    let settled = false;
    const finish = (callback) => {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      clearInterval(readinessCheck);
      callback();
    };
    const timeout = setTimeout(() => finish(() => reject(new Error(`La API no inició a tiempo. Salida: ${output}`))), 30000);

    apiProcess = spawn("dotnet", ["run", "--no-launch-profile", "--urls", baseUrl], {
      cwd: path.resolve(__dirname, ".."),
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: "Development",
        DatabasePath: databasePath,
        BootstrapAdmin__Email: adminEmail,
        BootstrapAdmin__Password: adminPassword,
        BootstrapOperator__Email: operatorEmail,
        BootstrapOperator__Password: operatorPassword,
        OTEL_EXPORTER_OTLP_ENDPOINT: ""
      },
      stdio: ["ignore", "pipe", "pipe"]
    });

    const onOutput = (data) => {
      output += data.toString();
      if (output.includes("Now listening on")) {
        finish(resolve);
      }
    };

    const readinessCheck = setInterval(async () => {
      try {
        const response = await fetch(`${baseUrl}/api/health`);
        if (response.ok) finish(resolve);
      } catch {
        // El proceso todavía no acepta conexiones.
      }
    }, 250);

    apiProcess.stdout.on("data", onOutput);
    apiProcess.stderr.on("data", onOutput);
    apiProcess.on("error", (error) => {
      finish(() => reject(error));
    });
    apiProcess.on("exit", (code) => {
      if (code !== 0 && !output.includes("Now listening on")) {
        finish(() => reject(new Error(`La API terminó con código ${code}. Salida: ${output}`)));
      }
    });
  });
}

function removeTestDatabase() {
  for (const suffix of ["", "-shm", "-wal"]) {
    fs.rmSync(`${databasePath}${suffix}`, { force: true });
  }
}

function removeDatabase(targetDatabasePath) {
  for (const suffix of ["", "-shm", "-wal"]) {
    fs.rmSync(`${targetDatabasePath}${suffix}`, { force: true });
  }
}

async function startOtlpCollector() {
  const requests = [];
  const server = http.createServer((request, response) => {
    request.resume();
    request.on("end", () => {
      requests.push(request.url);
      response.writeHead(200);
      response.end();
    });
  });
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port: collectorPort } = server.address();
  return { server, requests, endpoint: `http://127.0.0.1:${collectorPort}` };
}

function startObservableProbe(endpoint) {
  return new Promise((resolve, reject) => {
    let output = "";
    let settled = false;
    let probeProcess;
    const finish = (callback) => {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      clearInterval(readinessCheck);
      callback();
    };
    const timeout = setTimeout(() => finish(() => reject(new Error(`La sonda de observabilidad no inició. Salida: ${output}`))), 30000);
    probeProcess = spawn("dotnet", ["run", "--no-launch-profile", "--urls", observabilityBaseUrl], {
      cwd: path.resolve(__dirname, ".."),
      env: {
        ...process.env,
        ASPNETCORE_ENVIRONMENT: "Development",
        DatabasePath: observabilityDatabasePath,
        DataProtectionKeysPath: observabilityKeysPath,
        OTEL_SERVICE_NAME: "WebApp.ObservabilityTests",
        OTEL_EXPORTER_OTLP_ENDPOINT: "",
        OTEL_EXPORTER_OTLP_TRACES_ENDPOINT: `${endpoint}/signals/traces`,
        OTEL_EXPORTER_OTLP_METRICS_ENDPOINT: `${endpoint}/signals/metrics`,
        OTEL_EXPORTER_OTLP_LOGS_ENDPOINT: `${endpoint}/signals/logs`,
        OTEL_EXPORTER_OTLP_PROTOCOL: "http/protobuf",
        OTEL_METRIC_EXPORT_INTERVAL: "1000",
        OTEL_BSP_SCHEDULE_DELAY: "100"
      },
      stdio: ["ignore", "pipe", "pipe"]
    });
    const onOutput = (data) => {
      output += data.toString();
      if (output.includes("Now listening on")) finish(() => resolve(probeProcess));
    };
    const readinessCheck = setInterval(async () => {
      try {
        if ((await fetch(`${observabilityBaseUrl}/api/health`)).ok) finish(() => resolve(probeProcess));
      } catch {
        // La sonda todavía no acepta conexiones.
      }
    }, 250);
    probeProcess.stdout.on("data", onOutput);
    probeProcess.stderr.on("data", onOutput);
    probeProcess.on("error", (error) => finish(() => reject(error)));
    probeProcess.on("exit", (code) => {
      if (code !== 0 && !output.includes("Now listening on")) finish(() => reject(new Error(`La sonda terminó con código ${code}. Salida: ${output}`)));
    });
  });
}

async function stopProcess(process) {
  if (process && !process.killed) {
    await new Promise((resolve) => {
      process.once("exit", resolve);
      process.kill();
    });
  }
}

function bodyText(body) {
  return Buffer.from(body).toString("utf8");
}

function pageAntiforgeryToken(html) {
  const match = html.match(/name="__RequestVerificationToken" type="hidden" value="([^"]+)"/);
  if (!match) throw new Error("No se encontró el token antiforgery de Razor Pages.");
  return match[1];
}

beforeAll(async () => {
  removeTestDatabase();
  fs.rmSync(dataProtectionKeysPath, { recursive: true, force: true });
  await startApi();
});

afterAll(async () => {
  if (apiProcess && !apiProcess.killed) {
    await new Promise((resolve) => {
      apiProcess.once("exit", resolve);
      apiProcess.kill();
    });
  }
  removeTestDatabase();
  fs.rmSync(dataProtectionKeysPath, { recursive: true, force: true });
});

describe("Autenticación, autorización y API protegida", () => {
  const adminSession = new Session();
  const operatorSession = new Session();
  const managedUserSession = new Session();
  let categoryId;
  let taskId;
  let productId;
  let productCreatedAt;
  let adminUserId;
  let managedUserId;

  test("la aplicación arranca con exportación de observabilidad desactivada y GET /api/health permanece público", async () => {
    const { response, body } = await request("/api/health");
    expect(response.status).toBe(200);
    expect(body).toEqual({ statusCode: 200, data: { status: "healthy revisando" } });
  });

  test("el arranque de una SQLite vacía aplica migraciones y crea roles y usuarios bootstrap", async () => {
    const bootstrapAdminSession = new Session();
    const adminLogin = await login(bootstrapAdminSession, adminEmail, adminPassword);
    const operatorLogin = await login(new Session(), operatorEmail, operatorPassword);
    const { response, body } = await authenticatedRequest(bootstrapAdminSession, "/api/admin/users");

    expect(adminLogin.response.status).toBe(302);
    expect(operatorLogin.response.status).toBe(302);
    expect(response.status).toBe(200);
    expect(body.data).toEqual(expect.arrayContaining([
      expect.objectContaining({ email: adminEmail, roles: ["Admin"], isActive: true }),
      expect.objectContaining({ email: operatorEmail, roles: ["Operator"], isActive: true })
    ]));
  });

  test("la observabilidad es opcional y exporta trazas, métricas y logs a un colector OTLP local", async () => {
    const collector = await startOtlpCollector();
    let probeProcess;
    try {
      removeDatabase(observabilityDatabasePath);
      fs.rmSync(observabilityKeysPath, { recursive: true, force: true });
      probeProcess = await startObservableProbe(collector.endpoint);

      expect((await fetch(`${observabilityBaseUrl}/api/health`)).status).toBe(200);
      expect((await fetch(`${observabilityBaseUrl}/route-that-does-not-exist`)).status).toBe(404);
      const antiforgery = await fetch(`${observabilityBaseUrl}/api/auth/antiforgery`);
      const antiforgeryBody = await antiforgery.json();
      const antiforgeryCookie = antiforgery.headers.getSetCookie()[0].split(";", 1)[0];
      expect((await fetch(`${observabilityBaseUrl}/api/auth/login`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-CSRF-TOKEN": antiforgeryBody.requestToken,
          Cookie: antiforgeryCookie
        },
        body: JSON.stringify({ email: "not-an-account@example.test", password: "invalid" })
      })).status).toBe(401);
      // El procesador batch de logs usa hasta cinco segundos por defecto.
      await new Promise((resolve) => setTimeout(resolve, 6_000));
    } finally {
      await stopProcess(probeProcess);
      await new Promise((resolve) => setTimeout(resolve, 100));
      await new Promise((resolve) => collector.server.close(resolve));
      removeDatabase(observabilityDatabasePath);
      fs.rmSync(observabilityKeysPath, { recursive: true, force: true });
    }

    expect(collector.requests).toContain("/signals/traces");
    expect(collector.requests).toContain("/signals/metrics");
    expect(collector.requests).toContain("/signals/logs");
  });

  test("la página de login es visible para usuarios anónimos", async () => {
    const session = new Session();
    const { response, body } = await request("/account/login", {}, session);
    const html = bodyText(body);

    expect(response.status).toBe(200);
    expect(html).toContain("Iniciar sesión");
    expect(html).toContain('name="Input.Email"');
    expect(html).toContain('name="Input.Password"');
  });

  test("dashboard redirige a login cuando no existe autenticación", async () => {
    const { response } = await request("/dashboard");
    expect(response.status).toBe(302);
    expect(response.headers.get("location")).toContain("/account/login");
  });

  test("las rutas administrativas rechazan acceso anónimo", async () => {
    const { response } = await request("/api/tasks");
    expect(response.status).toBe(401);
  });

  test("los productos rechazan consultas y modificaciones anónimas", async () => {
    const list = await request("/api/products");
    const create = await request("/api/products", {
      method: "POST",
      body: JSON.stringify({ name: "Anónimo", price: 1, stock: 1 })
    });

    expect(list.response.status).toBe(401);
    expect(create.response.status).toBe(401);
  });

  test("credenciales inválidas y usuario inexistente devuelven el mismo resultado", async () => {
    const invalidPassword = await login(new Session(), adminEmail, "ContraseñaIncorrecta!1");
    const unknownUser = await login(new Session(), "desconocido@example.test", "ContraseñaIncorrecta!1");

    expect(invalidPassword.response.status).toBe(401);
    expect(unknownUser.response.status).toBe(401);
    expect(invalidPassword.response.headers.get("location")).toBe(unknownUser.response.headers.get("location"));
  });

  test("login válido crea cookie HttpOnly y redirige al dashboard", async () => {
    const { response } = await login(adminSession, adminEmail, adminPassword);
    expect(response.status).toBe(302);
    expect(response.headers.get("location")).toBe("/dashboard");
    expect(response.headers.getSetCookie().join(";")).toContain("httponly");
  });

  test("el formulario Razor inicia sesión usando antiforgery", async () => {
    const session = new Session();
    const loginPage = await request("/account/login", {}, session);
    const form = new URLSearchParams({
      "Input.Email": adminEmail,
      "Input.Password": adminPassword,
      __RequestVerificationToken: pageAntiforgeryToken(bodyText(loginPage.body))
    });
    const loginPagePost = await request("/account/login", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: form.toString()
    }, session);

    expect(loginPagePost.response.status).toBe(302);
    expect(loginPagePost.response.headers.get("location")).toBe("/dashboard");
  });

  test("Admin puede abrir dashboard y ver navegación administrativa", async () => {
    const dashboard = await authenticatedRequest(adminSession, "/dashboard");
    const html = bodyText(dashboard.body);

    expect(dashboard.response.status).toBe(200);
    expect(html).toContain("Panel administrativo");
    expect(html).toContain("Productos");
    expect(html).toContain("Usuarios");
  });

  test("la protección CSRF rechaza operaciones con estado sin token", async () => {
    const { response } = await request("/api/categories", {
      method: "POST",
      body: JSON.stringify({ name: "Sin token" })
    }, adminSession);
    expect(response.status).toBe(400);
  });

  test("Admin puede acceder a operaciones administrativas y backup", async () => {
    const access = await authenticatedRequest(adminSession, "/api/admin/users/authorization");
    const backup = await authenticatedRequest(adminSession, "/api/database/backup");

    expect(access.response.status).toBe(204);
    expect(backup.response.status).toBe(200);
    expect(backup.response.headers.get("content-disposition")).toContain("webapp-backup.db");
  });

  test("Operator puede acceder a productos pero no a administración de usuarios ni backup", async () => {
    const loginResult = await login(operatorSession, operatorEmail, operatorPassword);
    expect(loginResult.response.status).toBe(302);

    const products = await authenticatedRequest(operatorSession, "/api/products/authorization");
    const users = await authenticatedRequest(operatorSession, "/api/admin/users/authorization");
    const backup = await authenticatedRequest(operatorSession, "/api/database/backup");

    expect(products.response.status).toBe(204);
    expect(users.response.status).toBe(403);
    expect(backup.response.status).toBe(403);

    const dashboard = await authenticatedRequest(operatorSession, "/dashboard");
    const dashboardHtml = bodyText(dashboard.body);
    expect(dashboard.response.status).toBe(200);
    expect(dashboardHtml).toContain("Productos");
    expect(dashboardHtml).not.toContain('href="/users"');

    const deniedPage = await authenticatedRequest(operatorSession, "/users");
    expect(deniedPage.response.status).toBe(302);
    expect(deniedPage.response.headers.get("location")).toContain("/account/access-denied");
  });

  test("la administración de usuarios rechaza anónimos y Operators", async () => {
    const anonymous = await request("/api/admin/users");
    const operatorList = await authenticatedRequest(operatorSession, "/api/admin/users");
    const operatorCreate = await authenticatedRequest(operatorSession, "/api/admin/users", {
      method: "POST",
      body: JSON.stringify({ email: "blocked@example.test", userName: "blocked", password: managedUserPassword, role: "Operator" })
    });

    expect(anonymous.response.status).toBe(401);
    expect(operatorList.response.status).toBe(403);
    expect(operatorCreate.response.status).toBe(403);
  });

  test("Admin puede listar usuarios sin exponer información interna de Identity", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/admin/users");
    const admin = body.data.find((user) => user.email === adminEmail);

    expect(response.status).toBe(200);
    expect(admin).toMatchObject({ email: adminEmail, roles: ["Admin"], isActive: true });
    expect(admin).not.toHaveProperty("passwordHash");
    expect(admin).not.toHaveProperty("securityStamp");
    expect(admin).not.toHaveProperty("concurrencyStamp");
    adminUserId = admin.id;
  });

  test("Operator no puede cambiar roles ni desactivar cuentas", async () => {
    const roles = await authenticatedRequest(operatorSession, `/api/admin/users/${adminUserId}/roles`, {
      method: "PUT",
      body: JSON.stringify({ roles: ["Operator"] })
    });
    const status = await authenticatedRequest(operatorSession, `/api/admin/users/${adminUserId}/status`, {
      method: "PUT",
      body: JSON.stringify({ isActive: false })
    });

    expect(roles.response.status).toBe(403);
    expect(status.response.status).toBe(403);
  });

  test("las modificaciones de usuarios exigen CSRF", async () => {
    const { response } = await request("/api/admin/users", {
      method: "POST",
      body: JSON.stringify({ email: "without-csrf@example.test", userName: "without-csrf", password: managedUserPassword, role: "Operator" })
    }, adminSession);
    expect(response.status).toBe(400);
  });

  test("Admin crea un usuario con rol Operator", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/admin/users", {
      method: "POST",
      body: JSON.stringify({
        email: "managed@example.test",
        userName: "managed-user",
        firstName: "Managed",
        lastName: "User",
        password: managedUserPassword,
        role: "Operator"
      })
    });

    expect(response.status).toBe(201);
    expect(body.data).toMatchObject({ email: "managed@example.test", userName: "managed-user", roles: ["Operator"], isActive: true });
    expect(body.data).not.toHaveProperty("password");
    managedUserId = body.data.id;
  });

  test("la creación de usuarios valida correo, duplicados, contraseña y rol", async () => {
    const invalidRequests = [
      { email: "correo-invalido", userName: "invalid-email", password: managedUserPassword, role: "Operator" },
      { email: "managed@example.test", userName: "other-user", password: managedUserPassword, role: "Operator" },
      { email: "other@example.test", userName: "managed-user", password: managedUserPassword, role: "Operator" },
      { email: "password@example.test", userName: "password-user", password: "corta", role: "Operator" },
      { email: "role@example.test", userName: "role-user", password: managedUserPassword, role: "Unsupported" }
    ];

    for (const requestBody of invalidRequests) {
      const { response } = await authenticatedRequest(adminSession, "/api/admin/users", {
        method: "POST",
        body: JSON.stringify(requestBody)
      });
      expect(response.status).toBe(400);
    }
  });

  test("Admin puede consultar y actualizar información de usuario", async () => {
    const details = await authenticatedRequest(adminSession, `/api/admin/users/${managedUserId}`);
    const update = await authenticatedRequest(adminSession, `/api/admin/users/${managedUserId}`, {
      method: "PUT",
      body: JSON.stringify({ email: "managed.updated@example.test", userName: "managed-updated", firstName: "Updated", lastName: "Account" })
    });
    const missing = await authenticatedRequest(adminSession, "/api/admin/users/missing-user", {
      method: "PUT",
      body: JSON.stringify({ email: "missing@example.test", userName: "missing", firstName: "Missing", lastName: "User" })
    });
    const invalid = await authenticatedRequest(adminSession, `/api/admin/users/${managedUserId}`, {
      method: "PUT",
      body: JSON.stringify({ email: "invalid", userName: "", firstName: "", lastName: "" })
    });

    expect(details.response.status).toBe(200);
    expect(update.response.status).toBe(200);
    expect(update.body.data).toMatchObject({ email: "managed.updated@example.test", userName: "managed-updated", firstName: "Updated" });
    expect(missing.response.status).toBe(404);
    expect(invalid.response.status).toBe(400);
  });

  test("Admin puede asignar Admin y Operator, y rechaza roles inválidos", async () => {
    const adminRole = await authenticatedRequest(adminSession, `/api/admin/users/${managedUserId}/roles`, {
      method: "PUT",
      body: JSON.stringify({ roles: ["Admin"] })
    });
    const operatorRole = await authenticatedRequest(adminSession, `/api/admin/users/${managedUserId}/roles`, {
      method: "PUT",
      body: JSON.stringify({ roles: ["Operator"] })
    });
    const invalidRole = await authenticatedRequest(adminSession, `/api/admin/users/${managedUserId}/roles`, {
      method: "PUT",
      body: JSON.stringify({ roles: ["Other"] })
    });

    expect(adminRole.response.status).toBe(200);
    expect(adminRole.body.data.roles).toEqual(["Admin"]);
    expect(operatorRole.response.status).toBe(200);
    expect(operatorRole.body.data.roles).toEqual(["Operator"]);
    expect(invalidRole.response.status).toBe(400);
  });

  test("el último Admin activo no puede perder su rol", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/admin/users/${adminUserId}/roles`, {
      method: "PUT",
      body: JSON.stringify({ roles: ["Operator"] })
    });
    expect(response.status).toBe(400);
    expect(body.data.join(" ")).toContain("último rol Admin activo");
  });

  test("la desactivación invalida sesión, bloquea login y permite reactivación", async () => {
    const initialLogin = await login(managedUserSession, "managed.updated@example.test", managedUserPassword);
    expect(initialLogin.response.status).toBe(302);

    const deactivate = await authenticatedRequest(adminSession, `/api/admin/users/${managedUserId}/status`, {
      method: "PUT",
      body: JSON.stringify({ isActive: false })
    });
    expect(deactivate.response.status).toBe(200);
    expect(deactivate.body.data.isActive).toBe(false);
    expect((await authenticatedRequest(managedUserSession, "/api/products")).response.status).toBe(401);
    expect((await login(new Session(), "managed.updated@example.test", managedUserPassword)).response.status).toBe(401);

    const reactivate = await authenticatedRequest(adminSession, `/api/admin/users/${managedUserId}/status`, {
      method: "PUT",
      body: JSON.stringify({ isActive: true })
    });
    expect(reactivate.response.status).toBe(200);
    expect(reactivate.body.data.isActive).toBe(true);
    expect((await login(new Session(), "managed.updated@example.test", managedUserPassword)).response.status).toBe(302);
  });

  test("un Admin no puede desactivarse a sí mismo ni desactivar al último Admin", async () => {
    const self = await authenticatedRequest(adminSession, `/api/admin/users/${adminUserId}/status`, {
      method: "PUT",
      body: JSON.stringify({ isActive: false })
    });
    expect(self.response.status).toBe(400);
    expect(self.body.data.join(" ")).toContain("propia cuenta");
  });

  test("las páginas Razor de usuarios permiten listar, crear, editar y consultar", async () => {
    const list = await request("/users", {}, adminSession);
    expect(list.response.status).toBe(200);
    expect(bodyText(list.body)).toContain("managed.updated@example.test");

    const createPage = await request("/users/create", {}, adminSession);
    const createForm = new URLSearchParams({
      "Input.Email": "razor.user@example.test",
      "Input.UserName": "razor-user",
      "Input.FirstName": "Razor",
      "Input.LastName": "User",
      "Input.Password": `Razor!${randomUUID()}aA1`,
      "Input.Role": "Operator",
      __RequestVerificationToken: pageAntiforgeryToken(bodyText(createPage.body))
    });
    const created = await request("/users/create", { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: createForm.toString() }, adminSession);
    expect(created.response.status).toBe(302);

    const users = await authenticatedRequest(adminSession, "/api/admin/users");
    const razorUser = users.body.data.find((user) => user.email === "razor.user@example.test");
    const editPage = await request(`/users/${razorUser.id}/edit`, {}, adminSession);
    const editForm = new URLSearchParams({
      "Input.Email": "razor.updated@example.test",
      "Input.UserName": "razor-user-updated",
      "Input.FirstName": "Razor Updated",
      "Input.LastName": "User",
      "Input.Roles": "Operator",
      __RequestVerificationToken: pageAntiforgeryToken(bodyText(editPage.body))
    });
    const updated = await request(`/users/${razorUser.id}/edit`, { method: "POST", headers: { "Content-Type": "application/x-www-form-urlencoded" }, body: editForm.toString() }, adminSession);
    expect(updated.response.status).toBe(302);

    const details = await request(`/users/${razorUser.id}`, {}, adminSession);
    expect(bodyText(details.body)).toContain("razor.updated@example.test");
  });

  test("Admin puede crear un producto válido", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/products", {
      method: "POST",
      body: JSON.stringify({
        name: "Teclado mecanico",
        description: "Teclado para pruebas de inventario.",
        price: 89.95,
        stock: 12,
        createdAt: "2000-01-01T00:00:00Z"
      })
    });

    expect(response.status).toBe(201);
    expect(response.headers.get("location")).toMatch(/^\/api\/products\/\d+$/);
    expect(body.data).toMatchObject({ name: "Teclado mecanico", description: "Teclado para pruebas de inventario.", price: 89.95, stock: 12 });
    expect(body.data.createdAt).not.toBe("2000-01-01T00:00:00Z");
    expect(body.data.updatedAt).toBe(body.data.createdAt);
    productId = body.data.id;
    productCreatedAt = body.data.createdAt;
  });

  test("la API valida nombre, longitud, precio y stock de productos", async () => {
    const invalidProducts = [
      { name: "", price: 1, stock: 1 },
      { name: "x".repeat(151), price: 1, stock: 1 },
      { name: "Precio negativo", price: -1, stock: 1 },
      { name: "Stock negativo", price: 1, stock: -1 }
    ];

    for (const product of invalidProducts) {
      const { response, body } = await authenticatedRequest(adminSession, "/api/products", {
        method: "POST",
        body: JSON.stringify(product)
      });
      expect(response.status).toBe(400);
      expect(body.statusCode).toBe(400);
    }
  });

  test("la API de productos exige CSRF para crear", async () => {
    const { response } = await request("/api/products", {
      method: "POST",
      body: JSON.stringify({ name: "Sin CSRF", price: 1, stock: 1 })
    }, adminSession);
    expect(response.status).toBe(400);
  });

  test("Admin y Operator pueden consultar el listado y un producto", async () => {
    const adminList = await authenticatedRequest(adminSession, "/api/products");
    const operatorList = await authenticatedRequest(operatorSession, "/api/products");
    const details = await authenticatedRequest(adminSession, `/api/products/${productId}`);

    expect(adminList.response.status).toBe(200);
    expect(adminList.body.data).toEqual([expect.objectContaining({ id: productId, name: "Teclado mecanico" })]);
    expect(operatorList.response.status).toBe(200);
    expect(details.response.status).toBe(200);
    expect(details.body.data.createdAt).toBe(productCreatedAt);
  });

  test("la API responde 404 para un producto inexistente", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/products/99999");
    expect(response.status).toBe(404);
    expect(body.data).toBe("Producto no encontrado");
  });

  test("la página Razor lista productos para usuarios autorizados", async () => {
    const page = await request("/products", {}, operatorSession);
    const html = bodyText(page.body);
    expect(page.response.status).toBe(200);
    expect(html).toContain("Teclado mecanico");
    expect(html).toContain("Nuevo producto");
  });

  test("Admin puede actualizar un producto sin modificar su fecha de creación", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/products/${productId}`, {
      method: "PUT",
      body: JSON.stringify({ name: "Teclado inalámbrico", description: "Actualizado", price: 99.5, stock: 8 })
    });

    expect(response.status).toBe(200);
    expect(body.data).toMatchObject({ id: productId, name: "Teclado inalámbrico", description: "Actualizado", price: 99.5, stock: 8, createdAt: productCreatedAt });
    expect(new Date(body.data.updatedAt).getTime()).toBeGreaterThanOrEqual(new Date(productCreatedAt).getTime());
  });

  test("la actualización de producto valida datos y producto existente", async () => {
    const invalid = await authenticatedRequest(adminSession, `/api/products/${productId}`, {
      method: "PUT",
      body: JSON.stringify({ name: "", price: -1, stock: -1 })
    });
    const missing = await authenticatedRequest(adminSession, "/api/products/99999", {
      method: "PUT",
      body: JSON.stringify({ name: "No existe", price: 1, stock: 1 })
    });

    expect(invalid.response.status).toBe(400);
    expect(missing.response.status).toBe(404);
    expect(missing.body.data).toBe("Producto no encontrado");
  });

  test("Operator puede actualizar productos mediante el backend", async () => {
    const { response, body } = await authenticatedRequest(operatorSession, `/api/products/${productId}`, {
      method: "PUT",
      body: JSON.stringify({ name: "Teclado Operator", description: null, price: 100, stock: 7 })
    });
    expect(response.status).toBe(200);
    expect(body.data.name).toBe("Teclado Operator");
  });

  test("la eliminación de producto exige CSRF y permite eliminar un producto existente", async () => {
    const noCsrf = await request(`/api/products/${productId}`, { method: "DELETE" }, adminSession);
    expect(noCsrf.response.status).toBe(400);

    const deleted = await authenticatedRequest(adminSession, `/api/products/${productId}`, { method: "DELETE" });
    const missing = await authenticatedRequest(adminSession, `/api/products/${productId}`, { method: "DELETE" });
    expect(deleted.response.status).toBe(200);
    expect(deleted.body.data).toBe("Producto eliminado correctamente");
    expect(missing.response.status).toBe(404);
  });

  test("las páginas Razor permiten crear, editar, ver y eliminar productos", async () => {
    const createPage = await request("/products/create", {}, adminSession);
    const createForm = new URLSearchParams({
      "Input.Name": "Producto Razor",
      "Input.Description": "Creado desde el formulario web.",
      "Input.Price": "15.50",
      "Input.Stock": "4",
      __RequestVerificationToken: pageAntiforgeryToken(bodyText(createPage.body))
    });
    const created = await request("/products/create", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: createForm.toString()
    }, adminSession);
    expect(created.response.status).toBe(302);

    const list = await authenticatedRequest(adminSession, "/api/products");
    const razorProduct = list.body.data.find((product) => product.name === "Producto Razor");
    expect(razorProduct).toBeDefined();

    const editPage = await request(`/products/${razorProduct.id}/edit`, {}, adminSession);
    const editForm = new URLSearchParams({
      "Input.Name": "Producto Razor actualizado",
      "Input.Description": "Actualizado desde el formulario web.",
      "Input.Price": "20.00",
      "Input.Stock": "5",
      __RequestVerificationToken: pageAntiforgeryToken(bodyText(editPage.body))
    });
    const updated = await request(`/products/${razorProduct.id}/edit`, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: editForm.toString()
    }, adminSession);
    expect(updated.response.status).toBe(302);

    const details = await request(`/products/${razorProduct.id}`, {}, adminSession);
    expect(bodyText(details.body)).toContain("Producto Razor actualizado");

    const deletePage = await request(`/products/${razorProduct.id}/delete`, {}, adminSession);
    const deleteForm = new URLSearchParams({
      __RequestVerificationToken: pageAntiforgeryToken(bodyText(deletePage.body))
    });
    const deleted = await request(`/products/${razorProduct.id}/delete`, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: deleteForm.toString()
    }, adminSession);
    expect(deleted.response.status).toBe(302);
    expect((await authenticatedRequest(adminSession, `/api/products/${razorProduct.id}`)).response.status).toBe(404);
  });

  test("GET /api/tasks devuelve una lista vacía al iniciar", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/tasks");
    expect(response.status).toBe(200);
    expect(body).toEqual({ statusCode: 200, data: [] });
  });

  test("POST /api/categories crea una categoría", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/categories", {
      method: "POST",
      body: JSON.stringify({ name: "Trabajo" })
    });
    expect(response.status).toBe(200);
    expect(body.data.message).toBe("Categoría creada correctamente");
    categoryId = body.data.id;
  });

  test("GET /api/categories devuelve la categoría creada", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/categories");
    expect(response.status).toBe(200);
    expect(body.data).toEqual([{ id: categoryId, name: "Trabajo" }]);
  });

  test("POST /api/tasks rechaza una categoría inexistente", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/tasks", {
      method: "POST",
      body: JSON.stringify({ title: "Inválida", completed: false, categoryId: 99999 })
    });
    expect(response.status).toBe(400);
    expect(body.data).toBe("La categoría no existe");
  });

  test("POST /api/tasks crea una tarea válida", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/tasks", {
      method: "POST",
      body: JSON.stringify({ title: "Preparar pruebas", completed: false, categoryId })
    });
    expect(response.status).toBe(200);
    expect(body.data.message).toBe("Tarea creada correctamente");
    taskId = body.data.id;
  });

  test("GET /api/tasks/{id} devuelve la tarea solicitada", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/tasks/${taskId}`);
    expect(response.status).toBe(200);
    expect(body.data).toEqual({ id: taskId, title: "Preparar pruebas", completed: false, categoryId });
  });

  test("GET /api/tasks/{id} responde 404 para una tarea inexistente", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/tasks/99999");
    expect(response.status).toBe(404);
    expect(body.data).toBe("Tarea no encontrada");
  });

  test("PUT /api/tasks/{id} actualiza una tarea existente", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/tasks/${taskId}`, {
      method: "PUT",
      body: JSON.stringify({ title: "Pruebas terminadas", completed: true, categoryId })
    });
    expect(response.status).toBe(200);
    expect(body.data).toBe("Tarea actualizada correctamente");
  });

  test("PUT /api/tasks/{id} responde 404 al actualizar una tarea inexistente", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/tasks/99999", {
      method: "PUT",
      body: JSON.stringify({ title: "No existe", completed: true, categoryId })
    });
    expect(response.status).toBe(404);
    expect(body.data).toBe("Tarea no encontrada");
  });

  test("PUT /api/tasks/{id} rechaza una categoría inexistente", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/tasks/${taskId}`, {
      method: "PUT",
      body: JSON.stringify({ title: "Categoría inválida", completed: false, categoryId: 99999 })
    });
    expect(response.status).toBe(400);
    expect(body.data).toBe("La categoría no existe");
  });

  test("GET /api/categories/{id}/tasks devuelve las tareas de la categoría", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/categories/${categoryId}/tasks`);
    expect(response.status).toBe(200);
    expect(body.data).toEqual([{ id: taskId, title: "Pruebas terminadas", completed: true }]);
  });

  test("GET /api/categories/{id}/tasks responde 404 para una categoría inexistente", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/categories/99999/tasks");
    expect(response.status).toBe(404);
    expect(body.data).toBe("Categoría no encontrada");
  });

  test("GET /api/database/backup descarga la base de datos", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/database/backup");
    expect(response.status).toBe(200);
    expect(response.headers.get("content-disposition")).toContain("webapp-backup.db");
    expect(body.byteLength).toBeGreaterThan(0);
  });

  test("DELETE /api/tasks/{id} elimina una tarea", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/tasks/${taskId}`, { method: "DELETE" });
    expect(response.status).toBe(200);
    expect(body.data).toBe("Tarea eliminada correctamente");
  });

  test("DELETE /api/tasks/{id} responde 404 para una tarea ya eliminada", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/tasks/${taskId}`, { method: "DELETE" });
    expect(response.status).toBe(404);
    expect(body.data).toBe("Tarea no encontrada");
  });

  test("DELETE /api/categories/{id} elimina una categoría", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/categories/${categoryId}`, { method: "DELETE" });
    expect(response.status).toBe(200);
    expect(body.data).toBe("Categoría eliminada correctamente");
  });

  test("DELETE /api/categories/{id} responde 404 para una categoría inexistente", async () => {
    const { response, body } = await authenticatedRequest(adminSession, `/api/categories/${categoryId}`, { method: "DELETE" });
    expect(response.status).toBe(404);
    expect(body.data).toBe("Categoría no encontrada");
  });

  test("DELETE /api/database vacía la base de datos", async () => {
    const { response, body } = await authenticatedRequest(adminSession, "/api/database", { method: "DELETE" });
    expect(response.status).toBe(200);
    expect(body.data).toBe("Base de datos vaciada correctamente");
  });

  test("logout invalida la sesión", async () => {
    const token = await antiforgeryToken(adminSession);
    const logout = await request("/api/auth/logout", { method: "POST", headers: { "X-CSRF-TOKEN": token } }, adminSession);
    expect(logout.response.status).toBe(302);

    const protectedRequest = await request("/api/tasks", {}, adminSession);
    expect(protectedRequest.response.status).toBe(401);
  });

  test("el formulario web permite cerrar sesión", async () => {
    const session = new Session();
    const loginPage = await request("/account/login", {}, session);
    const loginForm = new URLSearchParams({
      "Input.Email": operatorEmail,
      "Input.Password": operatorPassword,
      __RequestVerificationToken: pageAntiforgeryToken(bodyText(loginPage.body))
    });
    await request("/account/login", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: loginForm.toString()
    }, session);

    const dashboard = await request("/dashboard", {}, session);
    const logoutForm = new URLSearchParams({
      __RequestVerificationToken: pageAntiforgeryToken(bodyText(dashboard.body))
    });
    const logout = await request("/account/logout", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: logoutForm.toString()
    }, session);

    expect(logout.response.status).toBe(302);
    expect(logout.response.headers.get("location")).toBe("/account/login");
    expect((await request("/dashboard", {}, session)).response.status).toBe(302);
  });
});
