"use strict";

/**
 * HTTP 接口调用与触发工具 — 零依赖本地服务
 * 运行：node server.js [端口]  （默认 8787）
 * 用户数据统一保存于项目 data/ 目录（不使用浏览器 localStorage）
 */
const http = require("http");
const https = require("https");
const fs = require("fs");
const path = require("path");
const { URL } = require("url");

const ROOT = __dirname;
const PUBLIC_DIR = path.join(ROOT, "public");
const DATA_DIR = path.join(ROOT, "data");
const SCENES_DIR = path.join(DATA_DIR, "scenes");
const INDEX_FILE = path.join(DATA_DIR, "index.json");
const PORT = Number(process.env.PORT) || Number(process.argv[2]) || 8787;

/* ---------------- 数据模型 ---------------- */

function uid() {
  return "id-" + Math.random().toString(36).slice(2, 10) + Date.now().toString(36);
}

function newSceneId() {
  return "sc-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 8);
}

function sceneFile(id) {
  return path.join(SCENES_DIR, id + ".json");
}

/** 场景模板：新建场景时基于此模板生成完整功能 */
function templateScene() {
  const envConfigs = {};
  DEFAULT_ENVS.forEach(e => { envConfigs[e.id] = emptyEnvConfig(); });
  envConfigs["env-dev"] = {
    headers: [
      { key: "Content-Type", value: "application/json" },
      { key: "Authorization", value: "Bearer demo-token" }
    ],
    globals: {
      baseUrl: "https://api.example.com/v1",
      userId: "u-1001",
      resourceId: "res-1001"
    },
    settings: { ...DEFAULT_SETTINGS }
  };
  return {
    environments: DEFAULT_ENVS.map(e => ({ ...e })),
    envConfigs,
    currentEnvId: "env-dev",
    globals: {
      baseUrl: "https://api.example.com/v1",
      userId: "u-1001",
      resourceId: "res-1001"
    },
    headers: [
      { key: "Content-Type", value: "application/json" },
      { key: "Authorization", value: "Bearer demo-token" }
    ],
    actions: [
      {
        id: uid(),
        name: "分页查询列表",
        method: "GET",
        url: "{{baseUrl}}/resources",
        paramType: "QUERY",
        params: "{\n  \"page\": 1,\n  \"size\": 10\n}",
        desc: "演示：GET + 查询参数拼接",
        color: "primary",
        useProxy: true,
        autoClose: false
      },
      {
        id: uid(),
        name: "创建一条数据",
        method: "POST",
        url: "{{baseUrl}}/resources",
        paramType: "JSON",
        params: "{\n  \"name\": \"示例数据\",\n  \"ownerId\": \"{{userId}}\"\n}",
        desc: "演示：POST + JSON 请求体",
        color: "success",
        useProxy: true,
        autoClose: true
      },
      {
        id: uid(),
        name: "查询数据详情",
        method: "GET",
        url: "{{baseUrl}}/resources/{{resourceId}}",
        paramType: "QUERY",
        params: "",
        desc: "演示：URL 路径中使用变量",
        color: "ghost",
        useProxy: true,
        autoClose: false
      },
      {
        id: uid(),
        name: "更新数据状态",
        method: "PUT",
        url: "{{baseUrl}}/resources/{{resourceId}}/status",
        paramType: "JSON",
        params: "{\n  \"status\": \"active\"\n}",
        desc: "演示：PUT 请求",
        color: "primary",
        useProxy: true,
        autoClose: false
      },
      {
        id: uid(),
        name: "删除数据",
        method: "DELETE",
        url: "{{baseUrl}}/resources/{{resourceId}}",
        paramType: "QUERY",
        params: "",
        desc: "演示：DELETE 请求",
        color: "danger",
        useProxy: true,
        autoClose: false
      }
    ],
    poll: {
      url: "{{baseUrl}}/resources/{{resourceId}}/status",
      method: "GET",
      paramType: "QUERY",
      interval: 3,
      body: "",
      useProxy: true
    },
    settings: { defaultUseProxy: true, timeoutMs: 30000, insecure: false },
    logs: [],
    lastRequest: "",
    lastResponse: "",
    lastRequestName: "",
    lastRequestTime: "",
    lastResponseName: "",
    lastResponseTime: ""
  };
}

/** 空白场景（清空配置时使用） */
const ENV_COLORS = ["#0071E3", "#FF9500", "#34C759", "#AF52DE", "#FF2D55", "#5AC8FA"];
const DEFAULT_ENVS = [
  { id: "env-dev", name: "开发环境", color: "#0071E3" },
  { id: "env-test", name: "测试环境", color: "#FF9500" },
  { id: "env-prod", name: "生产环境", color: "#34C759" }
];
const DEFAULT_SETTINGS = { defaultUseProxy: true, timeoutMs: 30000, insecure: false };
function emptyEnvConfig() {
  return { headers: [], globals: {}, settings: { ...DEFAULT_SETTINGS } };
}
function normalizeHeaders(list) {
  return Array.isArray(list)
    ? list.filter(h => h && typeof h === "object").map(h => ({ key: String(h.key ?? ""), value: String(h.value ?? "") }))
    : [];
}
function emptyScene() {
  const envConfigs = {};
  DEFAULT_ENVS.forEach(e => { envConfigs[e.id] = emptyEnvConfig(); });
  return {
    environments: DEFAULT_ENVS.map(e => ({ ...e })),
    envConfigs,
    currentEnvId: DEFAULT_ENVS[0].id,
    globals: {},
    headers: [],
    actions: [],
    poll: { url: "", method: "POST", paramType: "JSON", interval: 3, body: "", useProxy: true },
    settings: { ...DEFAULT_SETTINGS },
    logs: [],
    lastRequest: "",
    lastResponse: "",
    lastRequestName: "",
    lastRequestTime: "",
    lastResponseName: "",
    lastResponseTime: ""
  };
}

function normalizeAction(a) {
  return {
    id: typeof a.id === "string" && a.id ? a.id : uid(),
    name: String(a.name ?? "未命名接口"),
    method: ["GET", "POST", "PUT", "DELETE", "PATCH"].includes(a.method) ? a.method : "POST",
    url: String(a.url ?? ""),
    paramType: ["JSON", "FORM", "QUERY", "RAW"].includes(a.paramType) ? a.paramType : "JSON",
    params: String(a.params ?? ""),
    desc: String(a.desc ?? ""),
    color: ["primary", "success", "ghost", "danger"].includes(a.color) ? a.color : "primary",
    useProxy: a.useProxy !== false,
    autoClose: !!a.autoClose
  };
}

/** 以空白场景为基底合并外部数据（导入 / 保存）；headers/globals/settings 始终输出为「当前环境」的配置 */
function mergeScene(data) {
  const base = emptyScene();
  const d = data && typeof data === "object" ? data : {};

  // 环境列表
  let environments;
  if (Array.isArray(d.environments) && d.environments.length) {
    environments = d.environments.map((e, i) => ({
      id: String(e && e.id ? e.id : "env-" + i),
      name: String(e && e.name ? e.name : "环境" + (i + 1)).slice(0, 30),
      color: e && typeof e === "object" && /^#[0-9a-fA-F]{6}$/.test(e.color) ? e.color : ENV_COLORS[i % ENV_COLORS.length]
    }));
  } else {
    environments = DEFAULT_ENVS.map(x => ({ ...x }));
  }
  // 每个环境的独立配置
  const envConfigs = {};
  const hasEnvConfigs = d.envConfigs && typeof d.envConfigs === "object";
  environments.forEach(e => {
    const c = hasEnvConfigs ? d.envConfigs[e.id] : null;
    envConfigs[e.id] = {
      headers: normalizeHeaders(c && c.headers),
      globals: c && c.globals && typeof c.globals === "object" ? c.globals : {},
      settings: { ...DEFAULT_SETTINGS, ...((c && c.settings && typeof c.settings === "object") ? c.settings : {}) }
    };
  });
  // 旧数据迁移：无环境结构时，原 headers/globals/settings 归入默认环境
  if (!hasEnvConfigs) {
    envConfigs[DEFAULT_ENVS[0].id] = {
      headers: normalizeHeaders(d.headers),
      globals: d.globals && typeof d.globals === "object" ? d.globals : {},
      settings: { ...DEFAULT_SETTINGS, ...(d.settings && typeof d.settings === "object" ? d.settings : {}) }
    };
  }
  const currentEnvId = environments.some(e => e.id === d.currentEnvId) ? d.currentEnvId : environments[0].id;
  const cur = envConfigs[currentEnvId];

  return {
    environments,
    envConfigs,
    currentEnvId,
    globals: cur.globals,
    headers: cur.headers,
    actions: Array.isArray(d.actions) ? d.actions.filter(a => a && typeof a === "object").map(normalizeAction) : [],
    poll: {
      ...base.poll,
      ...(d.poll && typeof d.poll === "object" ? d.poll : {}),
      paramType: ["JSON", "FORM", "QUERY", "RAW"].includes(d.poll && d.poll.paramType) ? d.poll.paramType : "JSON"
    },
    settings: cur.settings,
    logs: Array.isArray(d.logs) ? d.logs.slice(-30) : [],
    lastRequest: String(d.lastRequest ?? ""),
    lastResponse: String(d.lastResponse ?? ""),
    lastRequestName: String(d.lastRequestName ?? ""),
    lastRequestTime: String(d.lastRequestTime ?? ""),
    lastResponseName: String(d.lastResponseName ?? ""),
    lastResponseTime: String(d.lastResponseTime ?? "")
  };
}

/* ---------------- 文件读写 ---------------- */

function writeJson(fp, obj) {
  fs.mkdirSync(path.dirname(fp), { recursive: true });
  fs.writeFileSync(fp, JSON.stringify(obj, null, 2), "utf8");
}

function readJson(fp) {
  return JSON.parse(fs.readFileSync(fp, "utf8"));
}

function ensureData() {
  fs.mkdirSync(SCENES_DIR, { recursive: true });
  if (!fs.existsSync(INDEX_FILE)) {
    const id = newSceneId();
    const now = new Date().toISOString();
    writeJson(INDEX_FILE, {
      currentSceneId: id,
      scenes: [{ id, name: "演示场景", createdAt: now, updatedAt: now }]
    });
    writeJson(sceneFile(id), templateScene());
  }
}

function readIndex() {
  let idx;
  try {
    idx = readJson(INDEX_FILE);
  } catch (e) {
    console.error("index.json 读取失败，重建:", e.message);
    idx = null;
  }
  if (!idx || !Array.isArray(idx.scenes) || idx.scenes.length === 0) {
    const id = newSceneId();
    const now = new Date().toISOString();
    idx = { currentSceneId: id, groups: [], scenes: [{ id, name: "演示场景", createdAt: now, updatedAt: now }] };
    writeJson(INDEX_FILE, idx);
    writeJson(sceneFile(id), templateScene());
  }
  // 归一化：分组 / 排序 / 折叠字段（兼容旧数据）
  if (!Array.isArray(idx.groups)) idx.groups = [];
  if (!idx.ui || typeof idx.ui !== "object") idx.ui = {};
  idx.groups.forEach((g, i) => {
    if (typeof g.order !== "number") g.order = i;
    if (typeof g.collapsed !== "boolean") g.collapsed = false;
  });
  idx.scenes.forEach((s, i) => {
    if (s.groupId === undefined) s.groupId = null;
    if (typeof s.order !== "number") s.order = i;
  });
  return idx;
}

function writeIndex(idx) {
  writeJson(INDEX_FILE, idx);
}

function readSceneMeta(id) {
  const idx = readIndex();
  return idx.scenes.find(s => s.id === id) || null;
}

function readSceneData(id) {
  const fp = sceneFile(id);
  if (!fs.existsSync(fp)) return null;
  return mergeScene(readJson(fp));
}

function createScene(opts = {}) {
  const idx = readIndex();
  let data;
  if (opts.copyFrom) {
    const src = readSceneData(opts.copyFrom);
    if (!src) throw new Error("被复制的场景不存在");
    data = mergeScene(src);
  } else if (opts.data) {
    data = mergeScene(opts.data);
  } else {
    data = templateScene();
  }
  const id = newSceneId();
  const now = new Date().toISOString();
  const name = String(opts.name || "").trim().slice(0, 60) || "新场景";
  const maxOrder = Math.max(-1, ...idx.scenes.filter(s => !s.groupId).map(s => s.order || 0));
  idx.scenes.push({ id, name, createdAt: now, updatedAt: now, groupId: null, order: maxOrder + 1 });
  writeJson(sceneFile(id), data);
  writeIndex(idx);
  return { meta: { id, name, createdAt: now, updatedAt: now, groupId: null, order: maxOrder + 1 }, data };
}

function removeScene(id) {
  const idx = readIndex();
  if (!idx.scenes.some(s => s.id === id)) return { error: "场景不存在" };
  try { fs.unlinkSync(sceneFile(id)); } catch (e) { /* 忽略 */ }
  idx.scenes = idx.scenes.filter(s => s.id !== id);
  if (idx.scenes.length === 0) {
    const created = createScene({ name: "演示场景" });
    const fresh = readIndex();
    fresh.currentSceneId = created.meta.id;
    writeIndex(fresh);
    return { currentSceneId: created.meta.id, scenes: fresh.scenes, groups: fresh.groups };
  }
  if (idx.currentSceneId === id) idx.currentSceneId = idx.scenes[0].id;
  writeIndex(idx);
  return { currentSceneId: idx.currentSceneId, scenes: idx.scenes, groups: idx.groups };
}

/* ---------------- 分组管理 ---------------- */

function createGroup(name) {
  const idx = readIndex();
  const g = {
    id: "gr-" + Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 6),
    name: String(name || "").trim().slice(0, 40) || "新分组",
    order: idx.groups.length,
    collapsed: false
  };
  idx.groups.push(g);
  writeIndex(idx);
  return g;
}

function updateGroup(id, patch) {
  const idx = readIndex();
  const g = idx.groups.find(x => x.id === id);
  if (!g) return null;
  if (patch.name !== undefined) {
    const name = String(patch.name || "").trim().slice(0, 40);
    if (name) g.name = name;
  }
  if (typeof patch.collapsed === "boolean") g.collapsed = patch.collapsed;
  writeIndex(idx);
  return g;
}

function removeGroup(id) {
  const idx = readIndex();
  if (!idx.groups.some(g => g.id === id)) return { error: "分组不存在" };
  idx.groups = idx.groups.filter(g => g.id !== id).map((g, i) => ({ ...g, order: i }));
  let maxOrder = Math.max(-1, ...idx.scenes.filter(s => !s.groupId).map(s => s.order || 0));
  idx.scenes.forEach(s => {
    if (s.groupId === id) { s.groupId = null; s.order = ++maxOrder; }
  });
  writeIndex(idx);
  return { groups: idx.groups, scenes: idx.scenes };
}

/** 应用前端拖拽后的布局：scenes 按渲染顺序提交，groups 按组间顺序提交；未提及项保持原位 */
function applyLayout(payload) {
  const idx = readIndex();
  const gSet = new Set(idx.groups.map(g => g.id));
  if (Array.isArray(payload.scenes) && payload.scenes.length) {
    const idSet = new Set(idx.scenes.map(s => s.id));
    const seen = new Set();
    const orderMap = {};
    const ordered = [];
    payload.scenes.forEach(item => {
      if (!item || !idSet.has(item.id) || seen.has(item.id)) return;
      seen.add(item.id);
      const s = idx.scenes.find(x => x.id === item.id);
      s.groupId = gSet.has(item.groupId) ? item.groupId : null;
      const key = s.groupId || "__ungrouped__";
      orderMap[key] = orderMap[key] || 0;
      s.order = orderMap[key]++;
      ordered.push(s);
    });
    // 未提及的场景保持原相对顺序追加
    idx.scenes.forEach(s => {
      if (!seen.has(s.id)) ordered.push(s);
    });
    idx.scenes = ordered;
  }
  if (Array.isArray(payload.groups) && payload.groups.length) {
    const gIdSet = new Set(idx.groups.map(g => g.id));
    const seen = new Set();
    const ordered = [];
    payload.groups.forEach(item => {
      if (!item || !gIdSet.has(item.id) || seen.has(item.id)) return;
      seen.add(item.id);
      const g = idx.groups.find(x => x.id === item.id);
      g.order = ordered.length;
      ordered.push(g);
    });
    idx.groups.forEach(g => {
      if (!seen.has(g.id)) { g.order = ordered.length; ordered.push(g); }
    });
    idx.groups = ordered;
  }
  writeIndex(idx);
  return idx;
}

/* ---------------- 代理请求（绕过 CORS） ---------------- */

function doProxy(payload, cb) {
  const urlStr = String(payload.url || "").trim();
  if (!/^https?:\/\//i.test(urlStr)) {
    return cb({ ok: false, error: "代理模式仅支持 http/https 绝对地址" });
  }
  const headers = {};
  if (payload.headers && typeof payload.headers === "object") {
    for (const [k, v] of Object.entries(payload.headers)) {
      const key = String(k).trim();
      if (key) headers[key] = String(v);
    }
  }
  headers["Accept-Encoding"] = "identity";
  const method = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD"].includes(String(payload.method || "GET").toUpperCase())
    ? String(payload.method).toUpperCase() : "GET";
  const body = payload.body == null ? null : Buffer.from(String(payload.body), "utf8");
  const timeout = Math.min(Math.max(Number(payload.timeout) || 30000, 1000), 300000);
  const insecure = !!payload.insecure;
  const started = Date.now();
  sendWithRedirects(urlStr, method, headers, body, timeout, insecure, 0, started, cb);
}

function sendWithRedirects(urlStr, method, headers, body, timeout, insecure, depth, started, cb) {
  let u;
  try { u = new URL(urlStr); } catch (e) { return cb({ ok: false, error: "URL 无效：" + urlStr }); }
  const lib = u.protocol === "https:" ? https : http;
  const req = lib.request({
    protocol: u.protocol,
    hostname: u.hostname,
    port: u.port || (u.protocol === "https:" ? 443 : 80),
    path: u.pathname + u.search,
    method,
    headers,
    rejectUnauthorized: !insecure,
    timeout
  }, (res) => {
    const status = res.statusCode || 0;
    if ([301, 302, 303, 307, 308].includes(status) && res.headers.location && depth < 5) {
      res.resume();
      const next = new URL(res.headers.location, u).toString();
      const nextMethod = status === 303 ? "GET" : method;
      const nextBody = status === 303 ? null : body;
      return sendWithRedirects(next, nextMethod, headers, nextBody, timeout, insecure, depth + 1, started, cb);
    }
    const chunks = [];
    res.on("data", c => chunks.push(c));
    res.on("end", () => {
      const buf = Buffer.concat(chunks);
      const contentType = String(res.headers["content-type"] || "");
      const isText = /text\/|json|xml|javascript|form-urlencoded|html/i.test(contentType) || contentType === "";
      cb({
        ok: status >= 200 && status < 300,
        status,
        statusText: res.statusMessage || "",
        contentType,
        duration: Date.now() - started,
        binary: !isText,
        size: buf.length,
        body: isText ? buf.toString("utf8") : buf.toString("base64")
      });
    });
  });
  req.on("timeout", () => req.destroy(new Error("请求超时（" + timeout + "ms）")));
  req.on("error", (err) => cb({ ok: false, error: err.message, duration: Date.now() - started }));
  if (body && method !== "GET" && method !== "HEAD") req.write(body);
  req.end();
}

/* ---------------- HTTP 服务 ---------------- */

const MIME = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
  ".png": "image/png",
  ".ico": "image/x-icon",
  ".map": "application/json"
};

function json(res, code, obj) {
  const body = JSON.stringify(obj);
  res.writeHead(code, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" });
  res.end(body);
}

function serveFile(fp, res) {
  const abs = path.resolve(fp);
  if (!abs.startsWith(path.resolve(PUBLIC_DIR) + path.sep) && abs !== path.resolve(path.join(PUBLIC_DIR, "index.html"))) {
    return json(res, 403, { error: "Forbidden" });
  }
  fs.readFile(abs, (err, buf) => {
    if (err) return json(res, 404, { error: "Not Found" });
    res.writeHead(200, {
      "Content-Type": MIME[path.extname(abs).toLowerCase()] || "application/octet-stream",
      "Cache-Control": "no-store"
    });
    res.end(buf);
  });
}

function readBody(req, limit = 10 * 1024 * 1024) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    req.on("data", c => {
      size += c.length;
      if (size > limit) {
        req.destroy();
        reject(new Error("请求体过大"));
        return;
      }
      chunks.push(c);
    });
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", reject);
  });
}

function parse(text) {
  try { return JSON.parse(text || "{}"); } catch (e) { return {}; }
}

const server = http.createServer((req, res) => {
  const u = new URL(req.url, "http://127.0.0.1");
  const p = decodeURIComponent(u.pathname);
  try {
    if (req.method === "GET" && (p === "/" || p === "/index.html")) {
      return serveFile(path.join(PUBLIC_DIR, "index.html"), res);
    }
    if (req.method === "GET" && p.startsWith("/assets/")) {
      return serveFile(path.join(PUBLIC_DIR, p.slice(1)), res);
    }
    if (p === "/api/state" && req.method === "GET") {
      const idx = readIndex();
      return json(res, 200, { currentSceneId: idx.currentSceneId, scenes: idx.scenes, groups: idx.groups, ui: idx.ui, dataDir: DATA_DIR });
    }
    if (p === "/api/ui" && req.method === "PUT") {
      return readBody(req).then(b => {
        const idx = readIndex();
        const u = parse(b);
        idx.ui = idx.ui || {};
        if (typeof u.actionWidth === "number" && u.actionWidth >= 200) idx.ui.actionWidth = Math.round(u.actionWidth);
        if (typeof u.pollWidth === "number" && u.pollWidth >= 200) idx.ui.pollWidth = Math.round(u.pollWidth);
        if (typeof u.sidebarWidth === "number" && u.sidebarWidth >= 160) idx.ui.sidebarWidth = Math.round(u.sidebarWidth);
        if (typeof u.collapsedPoll === "boolean") idx.ui.collapsedPoll = u.collapsedPoll;
        writeIndex(idx);
        return json(res, 200, idx.ui);
      });
    }
    if (p === "/api/groups" && req.method === "POST") {
      return readBody(req).then(b => json(res, 200, createGroup(parse(b).name)));
    }
    if (p === "/api/layout" && req.method === "PUT") {
      return readBody(req).then(b => {
        const idx = applyLayout(parse(b));
        return json(res, 200, { scenes: idx.scenes, groups: idx.groups });
      });
    }
    const gm = p.match(/^\/api\/groups\/([^/]+)$/);
    if (gm) {
      const gid = gm[1];
      if (req.method === "PATCH") {
        return readBody(req).then(b => {
          const g = updateGroup(gid, parse(b));
          if (!g) return json(res, 404, { error: "分组不存在" });
          return json(res, 200, g);
        });
      }
      if (req.method === "DELETE") {
        return json(res, 200, removeGroup(gid));
      }
    }
    if (p === "/api/template" && req.method === "GET") {
      return json(res, 200, templateScene());
    }
    if (p === "/api/scenes" && req.method === "POST") {
      return readBody(req).then(b => json(res, 200, createScene(parse(b))));
    }
    if (p === "/api/current" && req.method === "PUT") {
      return readBody(req).then(b => {
        const idx = readIndex();
        const id = String(parse(b).sceneId || "");
        if (!idx.scenes.some(s => s.id === id)) return json(res, 404, { error: "场景不存在" });
        idx.currentSceneId = id;
        writeIndex(idx);
        return json(res, 200, { currentSceneId: id });
      });
    }
    const m = p.match(/^\/api\/scenes\/([^/]+)$/);
    if (m) {
      const id = m[1];
      if (req.method === "GET") {
        const meta = readSceneMeta(id);
        const data = readSceneData(id);
        if (!meta || !data) return json(res, 404, { error: "场景不存在" });
        return json(res, 200, { meta, data });
      }
      if (req.method === "PUT") {
        return readBody(req).then(b => {
          if (!readSceneMeta(id)) return json(res, 404, { error: "场景不存在" });
          writeJson(sceneFile(id), mergeScene(parse(b).data));
          const idx = readIndex();
          const it = idx.scenes.find(s => s.id === id);
          it.updatedAt = new Date().toISOString();
          writeIndex(idx);
          return json(res, 200, { meta: it });
        });
      }
      if (req.method === "PATCH") {
        return readBody(req).then(b => {
          if (!readSceneMeta(id)) return json(res, 404, { error: "场景不存在" });
          const name = String(parse(b).name || "").trim().slice(0, 60);
          if (!name) return json(res, 400, { error: "名称不能为空" });
          const idx = readIndex();
          const it = idx.scenes.find(s => s.id === id);
          it.name = name;
          it.updatedAt = new Date().toISOString();
          writeIndex(idx);
          return json(res, 200, { meta: it });
        });
      }
      if (req.method === "DELETE") {
        return json(res, 200, removeScene(id));
      }
    }
    if (p === "/api/proxy" && req.method === "POST") {
      return readBody(req, 5 * 1024 * 1024).then(b => {
        doProxy(parse(b), out => json(res, 200, out));
      });
    }
    json(res, 404, { error: "Not Found" });
  } catch (e) {
    json(res, 500, { error: e.message });
  }
});

ensureData();
server.listen(PORT, "127.0.0.1", () => {
  console.log("HTTP Tool server running:");
  console.log("  Local:   http://127.0.0.1:" + PORT);
  console.log("  Data dir: " + DATA_DIR);
});
server.on("error", (err) => {
  if (err.code === "EADDRINUSE") {
    console.error("[ERROR] Port " + PORT + " is already in use. Try: node server.js 8788");
  } else {
    console.error("[ERROR] " + err.message);
  }
  process.exit(1);
});
