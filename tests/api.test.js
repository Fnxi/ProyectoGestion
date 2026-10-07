const { spawn } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const port = 5291;
const baseUrl = `http://127.0.0.1:${port}`;
const databasePath = path.join(__dirname, "webapp.test.db");
let apiProcess;

async function request(pathname, options = {}) {
  const response = await fetch(`${baseUrl}${pathname}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...options.headers
    }
  });

  const contentType = response.headers.get("content-type") ?? "";
  const body = contentType.includes("application/json")
    ? await response.json()
    : await response.arrayBuffer();

  return { response, body };
}

function startApi() {
  return new Promise((resolve, reject) => {
    let output = "";
    const timeout = setTimeout(() => {
      reject(new Error(`La API no inició a tiempo. Salida: ${output}`));
    }, 30000);

    apiProcess = spawn(
      "dotnet",
      ["run", "--no-launch-profile", "--urls", baseUrl],
      {
        cwd: path.resolve(__dirname, ".."),
        env: { ...process.env, DatabasePath: databasePath },
        stdio: ["ignore", "pipe", "pipe"]
      }
    );

    const onOutput = (data) => {
      output += data.toString();
      if (output.includes("Now listening on")) {
        clearTimeout(timeout);
        resolve();
      }
    };

    apiProcess.stdout.on("data", onOutput);
    apiProcess.stderr.on("data", onOutput);
    apiProcess.on("error", (error) => {
      clearTimeout(timeout);
      reject(error);
    });
    apiProcess.on("exit", (code) => {
      if (code !== 0 && !output.includes("Now listening on")) {
        clearTimeout(timeout);
        reject(new Error(`La API terminó con código ${code}. Salida: ${output}`));
      }
    });
  });
}

beforeAll(async () => {
  fs.rmSync(databasePath, { force: true });
  await startApi();
});

afterAll(async () => {
  if (apiProcess && !apiProcess.killed) {
    await new Promise((resolve) => {
      apiProcess.once("exit", resolve);
      apiProcess.kill();
    });
  }
  fs.rmSync(databasePath, { force: true });
});

describe("API de tareas y categorías", () => {
  let categoryId;
  let taskId;

  test("GET /api/health confirma que el servicio está disponible", async () => {
    const { response, body } = await request("/api/health");

    expect(response.status).toBe(200);
    expect(body).toEqual({ statusCode: 200, data: { status: "healthy" } });
  });

  test("GET /api/tasks devuelve una lista vacía al iniciar", async () => {
    const { response, body } = await request("/api/tasks");

    expect(response.status).toBe(200);
    expect(body).toEqual({ statusCode: 200, data: [] });
  });

  test("POST /api/categories crea una categoría", async () => {
    const { response, body } = await request("/api/categories", {
      method: "POST",
      body: JSON.stringify({ name: "Trabajo" })
    });

    expect(response.status).toBe(200);
    expect(body.data.message).toBe("Categoría creada correctamente");
    categoryId = body.data.id;
  });

  test("GET /api/categories devuelve la categoría creada", async () => {
    const { response, body } = await request("/api/categories");

    expect(response.status).toBe(200);
    expect(body.data).toEqual([{ id: categoryId, name: "Trabajo" }]);
  });

  test("POST /api/tasks rechaza una categoría inexistente", async () => {
    const { response, body } = await request("/api/tasks", {
      method: "POST",
      body: JSON.stringify({ title: "Inválida", completed: false, categoryId: 99999 })
    });

    expect(response.status).toBe(400);
    expect(body.data).toBe("La categoría no existe");
  });

  test("POST /api/tasks crea una tarea válida", async () => {
    const { response, body } = await request("/api/tasks", {
      method: "POST",
      body: JSON.stringify({ title: "Preparar pruebas", completed: false, categoryId })
    });

    expect(response.status).toBe(200);
    expect(body.data.message).toBe("Tarea creada correctamente");
    taskId = body.data.id;
  });

  test("GET /api/tasks/{id} devuelve la tarea solicitada", async () => {
    const { response, body } = await request(`/api/tasks/${taskId}`);

    expect(response.status).toBe(200);
    expect(body.data).toEqual({
      id: taskId,
      title: "Preparar pruebas",
      completed: false,
      categoryId
    });
  });

  test("GET /api/tasks/{id} responde 404 para una tarea inexistente", async () => {
    const { response, body } = await request("/api/tasks/99999");

    expect(response.status).toBe(404);
    expect(body.data).toBe("Tarea no encontrada");
  });

  test("PUT /api/tasks/{id} actualiza una tarea existente", async () => {
    const { response, body } = await request(`/api/tasks/${taskId}`, {
      method: "PUT",
      body: JSON.stringify({ title: "Pruebas terminadas", completed: true, categoryId })
    });

    expect(response.status).toBe(200);
    expect(body.data).toBe("Tarea actualizada correctamente");
  });

  test("PUT /api/tasks/{id} responde 404 al actualizar una tarea inexistente", async () => {
    const { response, body } = await request("/api/tasks/99999", {
      method: "PUT",
      body: JSON.stringify({ title: "No existe", completed: true, categoryId })
    });

    expect(response.status).toBe(404);
    expect(body.data).toBe("Tarea no encontrada");
  });

  test("PUT /api/tasks/{id} rechaza una categoría inexistente", async () => {
    const { response, body } = await request(`/api/tasks/${taskId}`, {
      method: "PUT",
      body: JSON.stringify({ title: "Categoría inválida", completed: false, categoryId: 99999 })
    });

    expect(response.status).toBe(400);
    expect(body.data).toBe("La categoría no existe");
  });

  test("GET /api/categories/{id}/tasks devuelve las tareas de la categoría", async () => {
    const { response, body } = await request(`/api/categories/${categoryId}/tasks`);

    expect(response.status).toBe(200);
    expect(body.data).toEqual([{ id: taskId, title: "Pruebas terminadas", completed: true }]);
  });

  test("GET /api/categories/{id}/tasks responde 404 para una categoría inexistente", async () => {
    const { response, body } = await request("/api/categories/99999/tasks");

    expect(response.status).toBe(404);
    expect(body.data).toBe("Categoría no encontrada");
  });

  test("GET /api/database/backup descarga la base de datos", async () => {
    const { response, body } = await request("/api/database/backup");

    expect(response.status).toBe(200);
    expect(response.headers.get("content-disposition")).toContain("webapp-backup.db");
    expect(body.byteLength).toBeGreaterThan(0);
  });

  test("DELETE /api/tasks/{id} elimina una tarea", async () => {
    const { response, body } = await request(`/api/tasks/${taskId}`, { method: "DELETE" });

    expect(response.status).toBe(200);
    expect(body.data).toBe("Tarea eliminada correctamente");
  });

  test("DELETE /api/tasks/{id} responde 404 para una tarea ya eliminada", async () => {
    const { response, body } = await request(`/api/tasks/${taskId}`, { method: "DELETE" });

    expect(response.status).toBe(404);
    expect(body.data).toBe("Tarea no encontrada");
  });

  test("DELETE /api/categories/{id} elimina una categoría", async () => {
    const { response, body } = await request(`/api/categories/${categoryId}`, { method: "DELETE" });

    expect(response.status).toBe(200);
    expect(body.data).toBe("Categoría eliminada correctamente");
  });

  test("DELETE /api/categories/{id} responde 404 para una categoría inexistente", async () => {
    const { response, body } = await request(`/api/categories/${categoryId}`, { method: "DELETE" });

    expect(response.status).toBe(404);
    expect(body.data).toBe("Categoría no encontrada");
  });

  test("DELETE /api/database vacía la base de datos", async () => {
    const { response, body } = await request("/api/database", { method: "DELETE" });

    expect(response.status).toBe(200);
    expect(body.data).toBe("Base de datos vaciada correctamente");
  });
});
