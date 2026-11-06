import { createServer } from "node:http";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const PORT = Number(process.env.PORT ?? 3001);
const DATA_FILE = resolve(ROOT, "data/profiles.json");
const CAPTURE_FILE = resolve(ROOT, "data/capture.json");

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET,POST,PUT,OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
};

const telemetryClients = new Set();
let liveTelemetry = null;

function sendJson(response, statusCode, body) {
  response.writeHead(statusCode, {
    ...corsHeaders,
    "Content-Type": "application/json; charset=utf-8",
  });
  response.end(JSON.stringify(body));
}

function isObject(value) {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isTelemetryValue(value) {
  return value === null || ["number", "boolean", "string"].includes(typeof value);
}

function validateTelemetry(value) {
  if (!isObject(value)) return "Telemetry body must be a flat JSON object";
  if (!Object.values(value).every(isTelemetryValue)) return "Telemetry values must be primitive values";
  return null;
}

function validateProfile(profile) {
  if (!isObject(profile)) return "Profile body must be a JSON object";
  if (!Number.isInteger(profile.schemaVersion)) return "schemaVersion must be an integer";
  if (typeof profile.id !== "string" || profile.id.trim() === "") return "id is required";
  if (typeof profile.name !== "string" || profile.name.trim() === "") return "name is required";
  if (!isObject(profile.canvas)) return "canvas must be an object";
  if (!Number.isFinite(profile.canvas.w) || profile.canvas.w <= 0) return "canvas.w must be positive";
  if (!Number.isFinite(profile.canvas.h) || profile.canvas.h <= 0) return "canvas.h must be positive";
  if (!Array.isArray(profile.widgets)) return "widgets must be an array";

  for (const [index, widget] of profile.widgets.entries()) {
    if (!isObject(widget)) return `widgets[${index}] must be an object`;
    if (typeof widget.id !== "string" || widget.id.trim() === "") return `widgets[${index}].id is required`;
    if (typeof widget.type !== "string" || widget.type.trim() === "") return `widgets[${index}].type is required`;
    for (const field of ["x", "y", "w", "h"]) {
      if (!Number.isFinite(widget[field])) return `widgets[${index}].${field} must be a number`;
    }
    if (widget.w <= 0 || widget.h <= 0) return `widgets[${index}].w and h must be positive`;
  }

  return null;
}

async function readJson(file) {
  return JSON.parse(await readFile(file, "utf8"));
}

async function readProfiles() {
  const profiles = await readJson(DATA_FILE);
  if (!Array.isArray(profiles)) throw new Error("Profile storage must contain an array");
  return profiles;
}

async function writeProfiles(profiles) {
  await mkdir(dirname(DATA_FILE), { recursive: true });
  await writeFile(DATA_FILE, `${JSON.stringify(profiles, null, 2)}\n`, "utf8");
}

async function readBaselineTelemetry() {
  const telemetry = await readJson(CAPTURE_FILE);
  const validationError = validateTelemetry(telemetry);
  if (validationError) throw new Error(validationError);
  return telemetry;
}

function broadcastTelemetry(telemetry) {
  const event = `data: ${JSON.stringify(telemetry)}\n\n`;
  for (const client of telemetryClients) {
    try {
      client.write(event);
    } catch {
      telemetryClients.delete(client);
    }
  }
}

async function readBody(request) {
  let body = "";
  request.setEncoding("utf8");
  for await (const chunk of request) {
    body += chunk;
    if (body.length > 1_000_000) throw new Error("Request body is too large");
  }
  try {
    return JSON.parse(body);
  } catch {
    throw new Error("Request body must be valid JSON");
  }
}

async function handleTelemetry(request, response, parts) {
  if (request.method === "GET" && parts[1] === "events" && parts.length === 2) {
    if (!liveTelemetry) liveTelemetry = await readBaselineTelemetry();
    response.writeHead(200, {
      ...corsHeaders,
      "Content-Type": "text/event-stream; charset=utf-8",
      "Cache-Control": "no-cache, no-transform",
      Connection: "keep-alive",
      "X-Accel-Buffering": "no",
    });
    response.write(`retry: 1000\ndata: ${JSON.stringify(liveTelemetry)}\n\n`);
    telemetryClients.add(response);
    response.on("close", () => telemetryClients.delete(response));
    return;
  }

  if (request.method === "GET" && parts.length === 1) {
    if (!liveTelemetry) liveTelemetry = await readBaselineTelemetry();
    sendJson(response, 200, liveTelemetry);
    return;
  }

  if ((request.method === "PUT" || request.method === "POST") && (parts.length === 1 || parts[1] === "ingest")) {
    const telemetry = await readBody(request);
    const validationError = validateTelemetry(telemetry);
    if (validationError) {
      sendJson(response, 400, { error: validationError });
      return;
    }
    liveTelemetry = telemetry;
    broadcastTelemetry(liveTelemetry);
    sendJson(response, 200, liveTelemetry);
    return;
  }

  sendJson(response, 405, { error: "Method not allowed" });
}

const server = createServer(async (request, response) => {
  if (request.method === "OPTIONS") {
    response.writeHead(204, corsHeaders);
    response.end();
    return;
  }

  const url = new URL(request.url ?? "/", `http://${request.headers.host ?? "localhost"}`);
  const parts = url.pathname.split("/").filter(Boolean);

  try {
    if (parts[0] === "health" && request.method === "GET") {
      sendJson(response, 200, { ok: true, service: "ac-hud-backend", telemetryClients: telemetryClients.size });
      return;
    }

    if (parts[0] === "telemetry") {
      await handleTelemetry(request, response, parts);
      return;
    }

    if (parts[0] !== "configs" || parts.length > 2) {
      sendJson(response, 404, { error: "Not found" });
      return;
    }

    const profiles = await readProfiles();
    if (request.method === "GET" && parts.length === 1) {
      sendJson(response, 200, profiles.map(({ id, name }) => ({ id, name })));
      return;
    }

    if (request.method === "GET" && parts.length === 2) {
      const profile = profiles.find((item) => item.id === decodeURIComponent(parts[1]));
      if (!profile) {
        sendJson(response, 404, { error: "Profile not found" });
        return;
      }
      sendJson(response, 200, profile);
      return;
    }

    if (request.method === "POST" && parts.length === 1) {
      const profile = await readBody(request);
      const validationError = validateProfile(profile);
      if (validationError) {
        sendJson(response, 400, { error: validationError });
        return;
      }
      if (profiles.some((item) => item.id === profile.id)) {
        sendJson(response, 409, { error: "Profile id already exists" });
        return;
      }
      profiles.push(profile);
      await writeProfiles(profiles);
      sendJson(response, 201, profile);
      return;
    }

    if (request.method === "PUT" && parts.length === 2) {
      const profileId = decodeURIComponent(parts[1]);
      const profile = await readBody(request);
      const validationError = validateProfile(profile);
      if (validationError) {
        sendJson(response, 400, { error: validationError });
        return;
      }
      if (profile.id !== profileId) {
        sendJson(response, 400, { error: "Body id must match URL id" });
        return;
      }
      const index = profiles.findIndex((item) => item.id === profileId);
      if (index === -1) {
        sendJson(response, 404, { error: "Profile not found" });
        return;
      }
      profiles[index] = profile;
      await writeProfiles(profiles);
      sendJson(response, 200, profile);
      return;
    }

    sendJson(response, 405, { error: "Method not allowed" });
  } catch (error) {
    console.error(error);
    sendJson(response, 500, { error: error instanceof Error ? error.message : "Internal server error" });
  }
});

server.listen(PORT, "0.0.0.0", () => {
  console.log(`AC HUD backend listening on http://0.0.0.0:${PORT}`);
});
