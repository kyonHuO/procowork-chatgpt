import { spawn } from "node:child_process";
import { existsSync, mkdirSync } from "node:fs";

import { join } from "node:path";
import { createInterface } from "node:readline";


import { createChatGPT } from "./sdk/dist/index.js";
const emit = (type, data = {}) => console.log(JSON.stringify({ type, ...data }));

function dpapi(method, bytes) {
  const exe = join(process.env.SystemRoot, "System32/WindowsPowerShell/v1.0/powershell.exe");
  const script = "$ErrorActionPreference='Stop'; Add-Type -AssemblyName System.Security; " +
    "$data=[Convert]::FromBase64String([Console]::In.ReadToEnd()); " +
    "$result=[Security.Cryptography.ProtectedData]::" + method +
    "($data,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser); " +
    "[Console]::Write([Convert]::ToBase64String($result))";
  return new Promise((resolve, reject) => {
    const child = spawn(exe, ["-NoProfile", "-NonInteractive", "-Command", script],
      { windowsHide: true, stdio: ["pipe", "pipe", "ignore"] });
    let output = "";
    child.stdout.setEncoding("ascii").on("data", (chunk) => { output += chunk; });
    child.on("error", reject);
    child.stdin.on("error", reject);
    child.on("close", (code) => {
      if (code !== 0 || !output) reject(new Error("Windows credential encryption failed."));
      else resolve(Buffer.from(output, "base64"));
    });
    child.stdin.end(Buffer.from(bytes).toString("base64"));
  });
}

const storageDir = join(process.env.LOCALAPPDATA, "ProCoworkChatGPT");
const client = createChatGPT({
  appName: "ProCowork",
  appId: "procowork-arcgis",
  redirectPort: 0,
  sendHostId: true,
  storageDir,
  openBrowser: (url) => emit("browser", { url }),
  credentialEncryption: {
    id: "windows-dpapi-currentuser-v1",
    isAvailable: () => process.platform === "win32",
    encrypt: (value) => dpapi("Protect", Buffer.from(value, "utf8")),
    decrypt: async (value) => (await dpapi("Unprotect", value)).toString("utf8"),
  },
});

function codexExecutable() {
  const path = join(process.env.APPDATA, "npm/node_modules/@openai/codex/node_modules/@openai",
    "codex-win32-x64/vendor/x86_64-pc-windows-msvc/bin/codex.exe");
  const exe = [path, ...process.env.PATH.split(";").filter(Boolean)
    .map((dir) => join(dir, "codex.exe"))].find(existsSync);
  if (!exe) throw new Error("Install Codex CLI to use ProCowork.");
  return exe;
}

async function runTurn(request, accessToken, signal) {
  const { bridgePort, bridgeToken } = request;
  if (!Number.isInteger(bridgePort) || bridgePort < 1 || bridgePort > 65535 ||
      typeof bridgeToken !== "string" || !/^[A-Fa-f0-9]{64}$/.test(bridgeToken))
    throw new Error("Invalid ArcGIS bridge session.");
  const bridgeUrl = "http://127.0.0.1:" + bridgePort + "/mcp";
  const provider = "model_providers.openai_chatgpt_plan";
  const config = [
    'model_provider="openai_chatgpt_plan"',
    provider + '.name="ChatGPT plan"',
    provider + '.base_url="https://api.openai.com/v1"',
    provider + '.env_key="ACCESS_TOKEN"',
    provider + '.wire_api="responses"',
    provider + '.requires_openai_auth=false',
    provider + '.supports_websockets=false',
    'features.tool_search=false',
    'features.shell_tool=false',
    'features.apps=false',
    'web_search="disabled"',
    'shell_environment_policy.inherit="none"',
    'mcp_servers.arcgis_bridge.url=' + JSON.stringify(bridgeUrl),
    'mcp_servers.arcgis_bridge.required=true',
    'mcp_servers.arcgis_bridge.default_tools_approval_mode="approve"',
    'mcp_servers.arcgis_bridge.tool_timeout_sec=620',
    'mcp_servers.arcgis_bridge.env_http_headers={Authorization="PROCOWORK_BRIDGE_AUTH"}',
  ];
  const env = { ...process.env, CODEX_HOME: join(storageDir, "codex"),
    ACCESS_TOKEN: accessToken, PROCOWORK_BRIDGE_AUTH: "Bearer " + bridgeToken };
  mkdirSync(env.CODEX_HOME, { recursive: true });
  for (const name of ["OPENAI_API_KEY", "OPENAI_BASE_URL", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "RUST_LOG", "RUST_LOG_SPAN_EVENTS"])
    delete env[name];
  const child = spawn(codexExecutable(), ["app-server", "--listen", "stdio://",
    ...config.flatMap((entry) => ["-c", entry])],
    { windowsHide: true, stdio: ["pipe", "pipe", "ignore"], env, signal });
  const pending = new Map();
  let nextId = 1;
  const turnDone = Promise.withResolvers();
  turnDone.promise.catch(() => {});
  const fail = (error) => {
    for (const operation of pending.values()) operation.reject(error);
    pending.clear();
    turnDone.reject(error);
  };
  child.once("error", fail);
  child.once("exit", () => fail(new Error("Codex app-server exited during the request.")));
  child.stdin.on("error", fail);
  const send = (message) => child.stdin.write(JSON.stringify(message) + "\n");
  const rpc = (method, params) => new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    send({ id, method, params });
  });

  let threadId;
  const output = createInterface({ input: child.stdout });
  output.on("line", (line) => {
    try {
      const { id, method, params, result, error } = JSON.parse(line);
      if (id !== undefined) {
        if (method) {
          send({ id, error: { code: -32601, message: "Unsupported request." } });
          return;
        }
        const operation = pending.get(id);
        pending.delete(id);
        if (error) operation?.reject(new Error(error.message));
        else operation?.resolve(result);
        return;
      }
      if (!threadId || params?.threadId !== threadId) return;
      const item = params.item;
      if (item?.type === "mcpToolCall" && method === "item/started")
        emit("tool", { id: item.id, name: item.tool, input: item.arguments });
      if (item?.type === "mcpToolCall" && method === "item/completed")
        emit("toolResult", { id: item.id, result: item.result?.content ?? item.error?.message, failed: item.status === "failed" });
      if (item?.type === "agentMessage" && method === "item/completed")
        emit("message", { text: item.text });
      if (method === "turn/completed") turnDone.resolve(params.turn);
    } catch (error) { fail(error); }
  });

  try {
    await rpc("initialize", { clientInfo: { name: "ProCowork", title: "ProCowork", version: "0.1.15" } });
    send({ method: "initialized" });
    const params = { cwd: process.cwd(), model: request.model, sandbox: "read-only", approvalPolicy: "never" };
    const session = request.threadId
      ? await rpc("thread/resume", { ...params, threadId: request.threadId })
      : await rpc("thread/start", params);
    threadId = session.thread.id;
    await rpc("turn/start", { threadId, input: [{ type: "text", text: request.text, text_elements: [] }] });
    const turn = await turnDone.promise;
    if (turn.status !== "completed") throw new Error(turn.error?.message ?? ("Codex turn " + turn.status));
    emit("success", { threadId, model: session.model ?? request.model });
  } finally {
    output.close();
    child.kill();
  }
}

try {
  const chunks = [];
  for await (const chunk of process.stdin) chunks.push(chunk);
  const request = JSON.parse(Buffer.concat(chunks).toString("utf8"));
  if (request.action === "signIn") {
    emit("session", { session: await client.signIn({ reconsent: true }) });
  } else if (request.action === "disconnect") {
    await client.disconnect();
    emit("session", { session: await client.getSession() });
  } else if (request.action === "status") {
    emit("session", { session: await client.getSession() });
  } else if (request.action === "turn") {
    if (!(await client.getSession()).sharing) throw new Error("Select Continue with ChatGPT and allow ChatGPT plan usage.");
    if (!request.model) {
      const models = await client.listModels();
      request.model = (models.find((model) => model.slug === "gpt-6.2-sol") ?? models[0])?.slug;
    }
    if (!request.model) throw new Error("ChatGPT did not return an available model.");
    await client.withPlanAccess((token, signal) => runTurn(request, token, signal));
  } else {
    throw new Error("Unknown ProCowork operation.");
  }
} catch (error) {
  emit("error", { message: error.message });
  process.exitCode = 1;
}
