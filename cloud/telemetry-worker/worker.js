/**
 * ZiloreMiniMap Opt-In Diagnostic Telemetry Worker (report.zilore.app)
 * Compatible with ErrorReportService.cs (System.Text.Json ErrorReportPayload).
 *
 * Security & Privacy Guarantees:
 * - Strict opt-in only; verifies X-Zilore-Token in constant time.
 * - Enforces <= 64 KB JSON body limit and per-IP rate limiting.
 * - Redacts Windows usernames, POI license keys, and tokens server-side (defense-in-depth).
 * - Persists reports to Cloudflare R2 (TELEMETRY_BUCKET) with daily partitioning.
 */

const MAX_BODY_BYTES = 65536;
const RATE_LIMIT_WINDOW_MS = 60_000;
const RATE_LIMIT_MAX_REQ = 6;
const ipBuckets = new Map();

function constantTimeEquals(a, b) {
  if (typeof a !== "string" || typeof b !== "string") return false;
  const enc = new TextEncoder();
  const ba = enc.encode(a);
  const bb = enc.encode(b);
  if (ba.length !== bb.length) return false;
  let diff = 0;
  for (let i = 0; i < ba.length; i++) {
    diff |= ba[i] ^ bb[i];
  }
  return diff === 0;
}

function sanitizeText(value, maxLen) {
  if (typeof value !== "string") return "";
  let s = value.slice(0, maxLen);
  // Redact Windows profile paths: C:\Users\<name>\ -> C:\Users\<redacted>\
  s = s.replace(/([A-Za-z]:\\Users\\)([^\\\r\n]+)/gi, "$1<redacted>");
  // Redact POI access codes and report tokens
  s = s.replace(/ZLR-POI-[A-Za-z0-9_-]{8,}/gi, "ZLR-POI-<redacted>");
  s = s.replace(/(ReportToken\s*=\s*)([^\r\n\s]+)/gi, "$1<redacted>");
  return s;
}

function checkRateLimit(ip) {
  const now = Date.now();
  if (ipBuckets.size > 4096) ipBuckets.clear();
  const entry = ipBuckets.get(ip);
  if (!entry || now - entry.windowStart >= RATE_LIMIT_WINDOW_MS) {
    ipBuckets.set(ip, { windowStart: now, count: 1 });
    return true;
  }
  if (entry.count >= RATE_LIMIT_MAX_REQ) return false;
  entry.count++;
  return true;
}

function jsonResponse(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
      "x-content-type-options": "nosniff"
    }
  });
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/health") {
      return jsonResponse({ ok: true, service: "zilore-telemetry", schema: 1 });
    }

    if (request.method !== "POST") {
      return jsonResponse({ error: "method_not_allowed" }, 405);
    }

    const expectedToken = env.ZILORE_REPORT_TOKEN || "";
    const providedToken = request.headers.get("X-Zilore-Token") || "";
    if (!expectedToken || !constantTimeEquals(providedToken, expectedToken)) {
      return jsonResponse({ error: "unauthorized" }, 401);
    }

    const clientIp = request.headers.get("CF-Connecting-IP") || "unknown";
    if (!checkRateLimit(clientIp)) {
      return jsonResponse({ error: "rate_limited" }, 429);
    }

    const contentType = request.headers.get("content-type") || "";
    if (!contentType.toLowerCase().includes("application/json")) {
      return jsonResponse({ error: "unsupported_media_type" }, 415);
    }

    const contentLength = Number(request.headers.get("content-length") || "0");
    if (contentLength > MAX_BODY_BYTES) {
      return jsonResponse({ error: "payload_too_large" }, 413);
    }

    const rawBody = await request.text();
    if (rawBody.length === 0 || new TextEncoder().encode(rawBody).length > MAX_BODY_BYTES) {
      return jsonResponse({ error: "payload_too_large" }, 413);
    }

    let parsed;
    try {
      parsed = JSON.parse(rawBody);
    } catch {
      return jsonResponse({ error: "invalid_json" }, 400);
    }

    if (!parsed || typeof parsed !== "object") {
      return jsonResponse({ error: "invalid_schema" }, 400);
    }

    const level = sanitizeText(String(parsed.level || "ERROR"), 16).toUpperCase();
    const record = {
      id: crypto.randomUUID(),
      receivedUtc: new Date().toISOString(),
      version: sanitizeText(String(parsed.version || ""), 128),
      os: sanitizeText(String(parsed.os || ""), 160),
      level: level === "WARNING" ? "WARNING" : "ERROR",
      source: sanitizeText(String(parsed.source || "App"), 64),
      message: sanitizeText(String(parsed.message || ""), 1024),
      logs: sanitizeText(String(parsed.logs || ""), 24000)
    };

    const now = new Date();
    const yyyy = now.getUTCFullYear();
    const mm = String(now.getUTCMonth() + 1).padStart(2, "0");
    const dd = String(now.getUTCDate()).padStart(2, "0");
    const objectKey = `reports/${yyyy}/${mm}/${dd}/${now.toISOString().replace(/[:.]/g, "-")}-${record.id}.json`;

    if (env.TELEMETRY_BUCKET) {
      await env.TELEMETRY_BUCKET.put(objectKey, JSON.stringify(record, null, 2), {
        httpMetadata: { contentType: "application/json" }
      });
    }

    if (record.level === "ERROR" && env.ALERT_WEBHOOK_URL) {
      const alertText = `[ZiloreMiniMap ${record.level}] ${record.version} | ${record.source}: ${record.message.slice(0, 260)}`;
      ctx.waitUntil(
        fetch(env.ALERT_WEBHOOK_URL, {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: JSON.stringify({ content: alertText })
        }).catch(() => {})
      );
    }

    return jsonResponse({ ok: true, id: record.id }, 202);
  }
};
