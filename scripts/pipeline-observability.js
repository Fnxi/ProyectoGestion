"use strict";

function firstDefined(...values) {
  return values.find((value) => value !== undefined && value !== null && value !== "");
}

function asNumber(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function calculatePipelineResult(results) {
  const values = Object.values(results).filter(Boolean);
  if (values.includes("failure")) return "failure";
  if (values.includes("cancelled")) return "cancelled";
  if (values.includes("success")) return "success";
  return "skipped";
}

function durationSeconds(startedAt, completedAt) {
  const start = Date.parse(startedAt);
  const end = Date.parse(completedAt);
  return Number.isFinite(start) && Number.isFinite(end) && end >= start
    ? Math.round((end - start) / 1000)
    : 0;
}

function buildEvent(env, timing = {}, now = new Date()) {
  const stageResults = {
    test: env.PIPELINE_TEST_RESULT,
    publish: env.PIPELINE_PUBLISH_RESULT,
    deploy: env.PIPELINE_DEPLOY_RESULT
  };
  const status = calculatePipelineResult(stageResults);
  const startedAt = firstDefined(timing.started_at, env.GITHUB_RUN_STARTED_AT, now.toISOString());
  const completedAt = firstDefined(timing.completed_at, now.toISOString());

  return {
    event_type: "ci_cd_pipeline",
    workflow_name: env.GITHUB_WORKFLOW || "unknown",
    workflow_run_id: asNumber(env.GITHUB_RUN_ID) ?? env.GITHUB_RUN_ID ?? "unknown",
    workflow_run_number: asNumber(env.GITHUB_RUN_NUMBER) ?? env.GITHUB_RUN_NUMBER ?? "unknown",
    job_name: "pipeline-observability",
    status,
    conclusion: status,
    repository: env.GITHUB_REPOSITORY || "unknown",
    branch: env.GITHUB_REF_NAME || "unknown",
    commit_sha: env.GITHUB_SHA || "unknown",
    actor: env.GITHUB_ACTOR || "unknown",
    trigger_event: env.GITHUB_EVENT_NAME || "unknown",
    run_url: firstDefined(env.GITHUB_SERVER_URL && env.GITHUB_REPOSITORY && env.GITHUB_RUN_ID
      ? `${env.GITHUB_SERVER_URL}/${env.GITHUB_REPOSITORY}/actions/runs/${env.GITHUB_RUN_ID}`
      : undefined, "unknown"),
    started_at: startedAt,
    completed_at: completedAt,
    duration_seconds: durationSeconds(startedAt, completedAt),
    deployment_status: env.PIPELINE_DEPLOY_RESULT || "skipped",
    environment: env.PIPELINE_DEPLOY_RESULT && env.PIPELINE_DEPLOY_RESULT !== "skipped"
      ? env.PIPELINE_ENVIRONMENT || null
      : null,
    test_status: env.PIPELINE_TEST_RESULT || "skipped",
    publish_status: env.PIPELINE_PUBLISH_RESULT || "skipped"
  };
}

async function getRunTiming(env, fetchImpl) {
  if (!env.GITHUB_TOKEN || !env.GITHUB_API_URL || !env.GITHUB_REPOSITORY || !env.GITHUB_RUN_ID) return {};

  const response = await fetchImpl(
    `${env.GITHUB_API_URL}/repos/${env.GITHUB_REPOSITORY}/actions/runs/${env.GITHUB_RUN_ID}`,
    { headers: { Accept: "application/vnd.github+json", Authorization: `Bearer ${env.GITHUB_TOKEN}` } }
  );
  if (!response.ok) throw new Error("GitHub Actions run metadata request failed");
  const run = await response.json();
  return {
    started_at: firstDefined(run.run_started_at, run.created_at),
    completed_at: firstDefined(run.updated_at, run.completed_at)
  };
}

function configured(env) {
  return [
    env.OPENOBSERVE_URL,
    env.OPENOBSERVE_ORGANIZATION,
    env.OPENOBSERVE_PIPELINE_STREAM,
    env.OPENOBSERVE_USERNAME,
    env.OPENOBSERVE_PASSWORD
  ].every(Boolean);
}

async function reportPipelineRun({ env = process.env, fetchImpl = fetch, now = new Date(), logger = console } = {}) {
  if (!configured(env)) {
    logger.log("Pipeline observability is not configured; event was not sent.");
    return { sent: false, reason: "not_configured" };
  }

  let timing = {};
  try {
    timing = await getRunTiming(env, fetchImpl);
  } catch {
    logger.warn("Pipeline observability could not retrieve run timing; using safe fallback timestamps.");
  }

  const event = buildEvent(env, timing, now);
  const authorization = `Basic ${Buffer.from(`${env.OPENOBSERVE_USERNAME}:${env.OPENOBSERVE_PASSWORD}`).toString("base64")}`;
  try {
    const response = await fetchImpl(env.OPENOBSERVE_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: authorization },
      body: JSON.stringify([event])
    });
    if (!response.ok) {
      logger.warn(`Pipeline observability delivery failed with HTTP status ${response.status}.`);
      return { sent: false, reason: "http_error", event };
    }

    logger.log("Pipeline observability event sent.");
    return { sent: true, event };
  } catch {
    logger.warn("Pipeline observability delivery failed; pipeline result is unchanged.");
    return { sent: false, reason: "network_error", event };
  }
}

if (require.main === module) {
  reportPipelineRun().catch(() => {
    // Reporting is intentionally best-effort and must never fail the workflow.
    console.warn("Pipeline observability delivery failed; pipeline result is unchanged.");
  });
}

module.exports = { buildEvent, calculatePipelineResult, reportPipelineRun };
