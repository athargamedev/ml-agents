import http from "node:http";
import { Readable } from "node:stream";

const listenPort = Number(process.env.PORT ?? "8080");
const targetBaseUrl = process.env.TARGET_BASE_URL ?? "http://host.docker.internal:7002";

function rewriteChatCompletionsPayload(payload) {
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
    return payload;
  }

  // Fix 1: LM Studio doesn't support structured tool_choice objects — rewrite to "required".
  const toolChoice = payload.tool_choice;
  if (toolChoice && typeof toolChoice === "object" && !Array.isArray(toolChoice)) {
    const forcedToolName = toolChoice.function?.name;
    if (forcedToolName && Array.isArray(payload.tools)) {
      const matchingTools = payload.tools.filter((tool) => tool?.type === "function" && tool.function?.name === forcedToolName);
      if (matchingTools.length > 0) {
        payload.tools = matchingTools;
      }
    }
    payload.tool_choice = "required";
  }

  // Fix 2: Qwen3 thinking mode returns content="" with all output in reasoning_content.
  // The Vercel AI SDK (openai-compatible provider) only reads content, so the agent sees
  // empty responses and terminates with no tool calls. Inject /no_think to disable CoT.
  const model = typeof payload.model === "string" ? payload.model : "";
  if (model.toLowerCase().includes("qwen3") && Array.isArray(payload.messages)) {
    const firstUserIdx = payload.messages.findIndex((m) => m.role === "user");
    if (firstUserIdx >= 0) {
      const msg = payload.messages[firstUserIdx];
      if (typeof msg.content === "string" && !msg.content.startsWith("/no_think")) {
        msg.content = "/no_think\n" + msg.content;
      }
    }
  }

  return payload;
}

function readRequestBody(request) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    request.on("data", (chunk) => chunks.push(chunk));
    request.on("end", () => resolve(Buffer.concat(chunks)));
    request.on("error", reject);
  });
}

function copyRequestHeaders(request) {
  const headers = new Headers();
  for (const [key, value] of Object.entries(request.headers)) {
    if (value === undefined) {
      continue;
    }

    if (Array.isArray(value)) {
      for (const entry of value) {
        headers.append(key, entry);
      }
      continue;
    }

    headers.set(key, value);
  }

  headers.delete("host");
  headers.delete("content-length");
  return headers;
}

function writeResponse(response, upstreamResponse) {
  response.statusCode = upstreamResponse.status;
  response.statusMessage = upstreamResponse.statusText;

  for (const [key, value] of upstreamResponse.headers.entries()) {
    if (key === "content-length") {
      continue;
    }

    response.setHeader(key, value);
  }

  if (!upstreamResponse.body) {
    response.end();
    return;
  }

  Readable.fromWeb(upstreamResponse.body).pipe(response);
}

const server = http.createServer(async (request, response) => {
  try {
    const incomingUrl = new URL(request.url ?? "/", "http://lmstudio-proxy.local");
    const upstreamUrl = new URL(incomingUrl.pathname + incomingUrl.search, targetBaseUrl);
    const headers = copyRequestHeaders(request);

    let requestBody;
    if (request.method !== "GET" && request.method !== "HEAD") {
      const rawBody = await readRequestBody(request);
      const contentType = request.headers["content-type"] ?? "";

      if (rawBody.length > 0 && typeof contentType === "string" && contentType.includes("application/json")) {
        const payload = JSON.parse(rawBody.toString("utf8"));
        if (incomingUrl.pathname.endsWith("/chat/completions")) {
          rewriteChatCompletionsPayload(payload);
        }

        requestBody = JSON.stringify(payload);
      } else if (rawBody.length > 0) {
        requestBody = rawBody;
      }
    }

    const upstreamResponse = await fetch(upstreamUrl, {
      method: request.method,
      headers,
      body: requestBody,
      duplex: "half",
    });

    writeResponse(response, upstreamResponse);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    response.statusCode = 502;
    response.setHeader("content-type", "application/json");
    response.end(JSON.stringify({ error: message }));
  }
});

server.listen(listenPort, "0.0.0.0", () => {
  console.log(`LM Studio compatibility proxy listening on ${listenPort}, forwarding to ${targetBaseUrl}`);
});
