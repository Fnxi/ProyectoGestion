const http = require("node:http");
const fs = require("node:fs");
const path = require("node:path");
const { buildEvent, reportPipelineRun } = require("../scripts/pipeline-observability");

const now = new Date("2026-11-25T12:05:00.000Z");
const baseEnvironment = {
  OPENOBSERVE_URL: "http://127.0.0.1:1/ingest",
  OPENOBSERVE_ORGANIZATION: "test-org",
  OPENOBSERVE_PIPELINE_STREAM: "ci-events",
  OPENOBSERVE_USERNAME: "test-user",
  OPENOBSERVE_PASSWORD: "test-password",
  GITHUB_API_URL: "http://127.0.0.1:1/api",
  GITHUB_TOKEN: "test-github-token",
  GITHUB_REPOSITORY: "example/webapp",
  GITHUB_RUN_ID: "123",
  GITHUB_RUN_NUMBER: "45",
  GITHUB_WORKFLOW: "CI/CD",
  GITHUB_REF_NAME: "main",
  GITHUB_SHA: "abc123",
  GITHUB_ACTOR: "octocat",
  GITHUB_EVENT_NAME: "push",
  GITHUB_SERVER_URL: "https://github.com",
  PIPELINE_TEST_RESULT: "success",
  PIPELINE_PUBLISH_RESULT: "success",
  PIPELINE_DEPLOY_RESULT: "success",
  PIPELINE_ENVIRONMENT: "production"
};

function silentLogger() {
  return { log: jest.fn(), warn: jest.fn() };
}

async function startServer(handler) {
  const server = http.createServer(handler);
  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address();
  return { server, url: `http://127.0.0.1:${port}` };
}

test("buildEvent includes required pipeline metadata and timing", () => {
  const event = buildEvent(baseEnvironment, {
    started_at: "2026-11-25T12:00:00.000Z",
    completed_at: "2026-11-25T12:05:00.000Z"
  }, now);

  expect(event).toMatchObject({
    event_type: "ci_cd_pipeline", workflow_name: "CI/CD", workflow_run_id: 123,
    workflow_run_number: 45, status: "success", conclusion: "success",
    repository: "example/webapp", branch: "main", commit_sha: "abc123", actor: "octocat",
    trigger_event: "push", deployment_status: "success", environment: "production",
    duration_seconds: 300
  });
  expect(event.run_url).toBe("https://github.com/example/webapp/actions/runs/123");
});

test.each([
  ["failure", "failure"],
  ["cancelled", "cancelled"],
  ["skipped", "skipped"]
])("buildEvent records a %s pipeline", (stageResult, expectedStatus) => {
  const event = buildEvent({ ...baseEnvironment, PIPELINE_TEST_RESULT: stageResult, PIPELINE_PUBLISH_RESULT: "skipped", PIPELINE_DEPLOY_RESULT: "skipped" }, {}, now);
  expect(event.status).toBe(expectedStatus);
  expect(event.conclusion).toBe(expectedStatus);
});

test("sends a JSON event to a local HTTP endpoint", async () => {
  let received;
  let authorization;
  const { server, url } = await startServer((request, response) => {
    if (request.method === "GET") {
      response.writeHead(200, { "Content-Type": "application/json" }).end(JSON.stringify({
        run_started_at: "2026-11-25T12:00:00.000Z",
        updated_at: "2026-11-25T12:05:00.000Z"
      }));
      return;
    }
    authorization = request.headers.authorization;
    let body = "";
    request.on("data", (chunk) => { body += chunk; });
    request.on("end", () => {
      received = JSON.parse(body);
      response.writeHead(200).end("{}");
    });
  });
  const logger = silentLogger();
  try {
    const result = await reportPipelineRun({
      env: { ...baseEnvironment, OPENOBSERVE_URL: `${url}/ingest`, GITHUB_API_URL: url }, now, logger
    });
    expect(result.sent).toBe(true);
    expect(received).toHaveLength(1);
    expect(received[0].event_type).toBe("ci_cd_pipeline");
    expect(authorization).toMatch(/^Basic /);
  } finally {
    await new Promise((resolve) => server.close(resolve));
  }
});

test("does not send when endpoint configuration is absent", async () => {
  const logger = silentLogger();
  const result = await reportPipelineRun({ env: { ...baseEnvironment, OPENOBSERVE_URL: "" }, now, logger });
  expect(result).toEqual({ sent: false, reason: "not_configured" });
  expect(logger.log).toHaveBeenCalledWith(expect.stringContaining("not configured"));
});

test("HTTP delivery errors are non-fatal and do not disclose credentials", async () => {
  const { server, url } = await startServer((request, response) => response.writeHead(503).end("unavailable"));
  const logger = silentLogger();
  try {
    const result = await reportPipelineRun({
      env: { ...baseEnvironment, OPENOBSERVE_URL: `${url}/ingest`, GITHUB_API_URL: url }, now, logger
    });
    expect(result.sent).toBe(false);
    expect(result.reason).toBe("http_error");
    const output = JSON.stringify(logger.warn.mock.calls);
    expect(output).not.toContain(baseEnvironment.OPENOBSERVE_PASSWORD);
    expect(output).not.toContain(baseEnvironment.OPENOBSERVE_USERNAME);
    expect(output).not.toContain(baseEnvironment.GITHUB_TOKEN);
  } finally {
    await new Promise((resolve) => server.close(resolve));
  }
});

test("network errors are non-fatal and return the original event status", async () => {
  const logger = silentLogger();
  const result = await reportPipelineRun({
    env: { ...baseEnvironment, OPENOBSERVE_URL: "http://127.0.0.1:1/ingest" }, now, logger
  });
  expect(result).toMatchObject({ sent: false, reason: "network_error", event: { status: "success" } });
  expect(logger.warn).toHaveBeenCalledWith(expect.stringContaining("pipeline result is unchanged"));
});

test("workflow schedules final reporting after every pipeline stage", () => {
  const workflow = fs.readFileSync(path.join(__dirname, "..", ".github", "workflows", "main.yml"), "utf8");
  expect(workflow).toContain("pipeline-observability:");
  expect(workflow).toContain("needs: [test, publish, deploy]");
  expect(workflow).toContain("if: ${{ always() }}");
  expect(workflow).toContain("continue-on-error: true");
  expect(workflow).toContain("actions: read");
});
