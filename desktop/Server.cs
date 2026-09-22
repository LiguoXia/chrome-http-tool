using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HttpTool;

/// <summary>
/// 内嵌本地 HTTP 服务：完整复刻 server.js 的全部 API 与数据语义。
/// 用户数据保存在 exe 同级 data 目录。
/// </summary>
internal sealed class AppServer
{
    private readonly int _port;
    private readonly string _baseDir;
    private readonly string _dataDir;
    private readonly string _publicDir;
    private readonly object _lock = new();
    private IHost _host;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions IndentedOpts = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    private static readonly string[] EnvColors =
        { "#0071E3", "#FF9500", "#34C759", "#AF52DE", "#FF2D55", "#5AC8FA" };

    private static readonly string[] Methods = { "GET", "POST", "PUT", "DELETE", "PATCH" };
    private static readonly string[] ParamTypes = { "JSON", "FORM", "QUERY", "RAW" };
    private static readonly string[] Colors = { "primary", "success", "ghost", "danger" };

    public AppServer(int port, string baseDir)
    {
        _port = port;
        _baseDir = baseDir;
        _dataDir = Path.Combine(baseDir, "data");
        _publicDir = Path.Combine(baseDir, "public");
    }

    public void Start()
    {
        EnsureData();
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Error);
            })
            .ConfigureWebHostDefaults(builder =>
            {
                builder.UseUrls("http://127.0.0.1:" + _port);
                builder.Configure(app => app.Run(HandleAsync));
            })
            .Build();
        _host.Start();
        // 轻量周期回收：低频触发代际 GC，保持长期运行内存水位稳定
        _gcTimer = new System.Threading.Timer(_ =>
        {
            try { GC.Collect(1, GCCollectionMode.Optimized, false); } catch { /* 忽略 */ }
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));
    }

    private System.Threading.Timer _gcTimer;

    public void Stop()
    {
        try { _gcTimer?.Dispose(); } catch { /* 忽略 */ }
        try { _host?.StopAsync(TimeSpan.FromSeconds(3)).Wait(); }
        catch { /* 忽略 */ }
    }

    /* ==================== 文件读写 ==================== */

    private string IndexFile => Path.Combine(_dataDir, "index.json");

    private string SceneFile(string id) => Path.Combine(_dataDir, "scenes", id + ".json");

    private JsonObject ReadJsonFile(string fp)
    {
        try { return JsonNode.Parse(File.ReadAllText(fp, Encoding.UTF8)) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    private void WriteJsonFile(string fp, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fp)!);
        File.WriteAllText(fp, node.ToJsonString(IndentedOpts), new UTF8Encoding(false));
    }

    private void EnsureData()
    {
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(Path.Combine(_dataDir, "scenes"));
        if (!File.Exists(IndexFile))
        {
            var (id, scene) = NewDemoScene();
            WriteJsonFile(SceneFile(id), scene);
            WriteJsonFile(IndexFile, BuildIndex(id, new JsonArray
            {
                SceneMeta(id, "演示场景", 0)
            }));
        }
    }

    /* ==================== 数据模型 ==================== */

    private static string Uid(string prefix)
    {
        string ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x");
        string rnd = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("/", "").Replace("+", "").Replace("=", "").Substring(0, 6);
        return prefix + ts + "-" + rnd;
    }

    private static JsonObject SceneMeta(string id, string name, int order)
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        return new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["createdAt"] = now,
            ["updatedAt"] = now,
            ["groupId"] = null,
            ["order"] = order
        };
    }

    private static JsonObject BuildIndex(string currentSceneId, JsonArray scenes)
    {
        return new JsonObject
        {
            ["currentSceneId"] = currentSceneId,
            ["groups"] = new JsonArray(),
            ["scenes"] = scenes,
            ["ui"] = new JsonObject()
        };
    }

    private (string id, JsonObject scene) NewDemoScene()
    {
        return ("sc-" + Uid("sc"), TemplateScene());
    }

    /** 空白场景（清空配置时使用） */
    private static JsonObject EmptySettings()
    {
        return new JsonObject { ["defaultUseProxy"] = true, ["timeoutMs"] = 30000, ["insecure"] = false };
    }

    private static JsonObject EmptyEnvConfig()
    {
        return new JsonObject
        {
            ["headers"] = new JsonArray(),
            ["globals"] = new JsonObject(),
            ["settings"] = EmptySettings()
        };
    }

    private static JsonArray DefaultEnvs()
    {
        return new JsonArray
        {
            Env("env-dev", "开发环境", "#0071E3"),
            Env("env-test", "测试环境", "#FF9500"),
            Env("env-prod", "生产环境", "#34C759")
        };
    }

    private static JsonObject Env(string id, string name, string color)
    {
        return new JsonObject { ["id"] = id, ["name"] = name, ["color"] = color };
    }

    private static JsonArray NormalizeHeaders(JsonNode list)
    {
        var outArr = new JsonArray();
        if (list is not JsonArray arr) return outArr;
        foreach (var h in arr)
        {
            if (h is not JsonObject o) continue;
            outArr.Add(new JsonObject
            {
                ["key"] = Str(o["key"]),
                ["value"] = Str(o["value"])
            });
        }
        return outArr;
    }

    private static JsonObject NormalizeAction(JsonObject a)
    {
        string id = Str(a["id"]);
        if (string.IsNullOrEmpty(id)) id = Uid("act");
        return new JsonObject
        {
            ["id"] = id,
            ["name"] = Str(a["name"]).Length > 0 ? Str(a["name"]) : "未命名接口",
            ["method"] = Methods.Contains(Str(a["method"]).ToUpperInvariant()) ? Str(a["method"]).ToUpperInvariant() : "POST",
            ["url"] = Str(a["url"]),
            ["paramType"] = ParamTypes.Contains(Str(a["paramType"])) ? Str(a["paramType"]) : "JSON",
            ["params"] = Str(a["params"]),
            ["desc"] = Str(a["desc"]),
            ["color"] = Colors.Contains(Str(a["color"])) ? Str(a["color"]) : "primary",
            ["useProxy"] = a["useProxy"] == null || Bool(a["useProxy"], true),
            ["autoClose"] = Bool(a["autoClose"], false)
        };
    }

    /** 以空白场景为基底合并外部数据；headers/globals/settings 始终输出为「当前环境」的配置 */
    private static JsonObject MergeScene(JsonObject d)
    {
        d ??= new JsonObject();

        // 环境列表
        JsonArray environments;
        if (d["environments"] is JsonArray envArr && envArr.Count > 0)
        {
            environments = new JsonArray();
            for (int i = 0; i < envArr.Count; i++)
            {
                var e = envArr[i] as JsonObject ?? new JsonObject();
                string id = Str(e["id"]);
                if (string.IsNullOrEmpty(id)) id = "env-" + i;
                string name = Str(e["name"]);
                if (string.IsNullOrEmpty(name)) name = "环境" + (i + 1);
                if (name.Length > 30) name = name.Substring(0, 30);
                string color = Str(e["color"]);
                if (!System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))
                    color = EnvColors[i % EnvColors.Length];
                environments.Add(new JsonObject { ["id"] = id, ["name"] = name, ["color"] = color });
            }
        }
        else
        {
            environments = DefaultEnvs();
        }

        // 每个环境的独立配置
        var envConfigs = new JsonObject();
        var rawConfigs = d["envConfigs"] as JsonObject;
        bool hasEnvConfigs = rawConfigs != null;
        foreach (var envNode in environments)
        {
            var env = (JsonObject)envNode;
            string eid = Str(env["id"]);
            JsonObject c = hasEnvConfigs ? rawConfigs[eid] as JsonObject : null;
            JsonObject settings = EmptySettings();
            if (c?["settings"] is JsonObject so)
            {
                settings["defaultUseProxy"] = Bool(so["defaultUseProxy"], true);
                settings["timeoutMs"] = Int(so["timeoutMs"], 30000);
                settings["insecure"] = Bool(so["insecure"], false);
            }
            envConfigs[eid] = new JsonObject
            {
                ["headers"] = NormalizeHeaders(c?["headers"]),
                ["globals"] = (c?["globals"] as JsonObject)?.DeepClone() ?? new JsonObject(),
                ["settings"] = settings
            };
        }
        // 旧数据迁移：无环境结构时，原 headers/globals/settings 归入默认环境
        if (!hasEnvConfigs)
        {
            JsonObject oldSettings = EmptySettings();
            if (d["settings"] is JsonObject so)
            {
                oldSettings["defaultUseProxy"] = Bool(so["defaultUseProxy"], true);
                oldSettings["timeoutMs"] = Int(so["timeoutMs"], 30000);
                oldSettings["insecure"] = Bool(so["insecure"], false);
            }
            envConfigs["env-dev"] = new JsonObject
            {
                ["headers"] = NormalizeHeaders(d["headers"]),
                ["globals"] = (d["globals"] as JsonObject)?.DeepClone() ?? new JsonObject(),
                ["settings"] = oldSettings
            };
        }

        string currentEnvId = Str(d["currentEnvId"]);
        bool envExists = false;
        foreach (var envNode in environments)
            if (Str(((JsonObject)envNode)["id"]) == currentEnvId) { envExists = true; break; }
        if (!envExists) currentEnvId = Str(((JsonObject)environments[0])["id"]);
        var cur = (JsonObject)envConfigs[currentEnvId];
        // actions
        var actions = new JsonArray();
        if (d["actions"] is JsonArray actArr)
            foreach (var a in actArr)
                if (a is JsonObject ao) actions.Add(NormalizeAction(ao));

        // poll
        var poll = new JsonObject
        {
            ["url"] = Str(d["poll"]?["url"]),
            ["method"] = Methods.Contains(Str(d["poll"]?["method"]).ToUpperInvariant()) ? Str(d["poll"]?["method"]).ToUpperInvariant() : "POST",
            ["paramType"] = ParamTypes.Contains(Str(d["poll"]?["paramType"])) ? Str(d["poll"]?["paramType"]) : "JSON",
            ["interval"] = Math.Max(1, Int(d["poll"]?["interval"], 3)),
            ["body"] = Str(d["poll"]?["body"]),
            ["useProxy"] = d["poll"]?["useProxy"] == null || Bool(d["poll"]?["useProxy"], true)
        };

        // logs（最多保留 30 条）
        // 日志历史不再随场景文件持久化（完整记录按天写入 data/logs），减小保存体积
        var logs = new JsonArray();

        return new JsonObject
        {
            ["environments"] = environments,
            ["envConfigs"] = envConfigs,
            ["currentEnvId"] = currentEnvId,
            ["globals"] = cur["globals"].DeepClone(),
            ["headers"] = cur["headers"].DeepClone(),
            ["actions"] = actions,
            ["poll"] = poll,
            ["settings"] = cur["settings"].DeepClone(),
            ["logs"] = logs,
            ["lastRequest"] = Str(d["lastRequest"]),
            ["lastResponse"] = Str(d["lastResponse"]),
            ["lastRequestName"] = Str(d["lastRequestName"]),
            ["lastRequestTime"] = Str(d["lastRequestTime"]),
            ["lastResponseName"] = Str(d["lastResponseName"]),
            ["lastResponseTime"] = Str(d["lastResponseTime"])
        };
    }

    /** 场景模板：抽象 REST 演示 */
    private static JsonObject TemplateScene()
    {
        var envConfigs = new JsonObject
        {
            ["env-dev"] = new JsonObject
            {
                ["headers"] = new JsonArray
                {
                    new JsonObject { ["key"] = "Content-Type", ["value"] = "application/json" },
                    new JsonObject { ["key"] = "Authorization", ["value"] = "Bearer demo-token" }
                },
                ["globals"] = new JsonObject
                {
                    ["baseUrl"] = "https://api.example.com/v1",
                    ["userId"] = "u-1001",
                    ["resourceId"] = "res-1001"
                },
                ["settings"] = EmptySettings()
            },
            ["env-test"] = EmptyEnvConfig(),
            ["env-prod"] = EmptyEnvConfig()
        };
        var globals = new JsonObject
        {
            ["baseUrl"] = "https://api.example.com/v1",
            ["userId"] = "u-1001",
            ["resourceId"] = "res-1001"
        };
        var headers = new JsonArray
        {
            new JsonObject { ["key"] = "Content-Type", ["value"] = "application/json" },
            new JsonObject { ["key"] = "Authorization", ["value"] = "Bearer demo-token" }
        };

        JsonObject Act(string name, string method, string url, string paramType, string ps, string desc, string color, bool autoClose)
        {
            return new JsonObject
            {
                ["id"] = Uid("act"),
                ["name"] = name,
                ["method"] = method,
                ["url"] = url,
                ["paramType"] = paramType,
                ["params"] = ps,
                ["desc"] = desc,
                ["color"] = color,
                ["useProxy"] = true,
                ["autoClose"] = autoClose
            };
        }

        var actions = new JsonArray
        {
            Act("分页查询列表", "GET", "{{baseUrl}}/resources", "QUERY", "{\n  \"page\": 1,\n  \"size\": 10\n}", "演示：GET + 查询参数拼接", "primary", false),
            Act("创建一条数据", "POST", "{{baseUrl}}/resources", "JSON", "{\n  \"name\": \"示例数据\",\n  \"ownerId\": \"{{userId}}\"\n}", "演示：POST + JSON 请求体", "success", true),
            Act("查询数据详情", "GET", "{{baseUrl}}/resources/{{resourceId}}", "QUERY", "", "演示：URL 路径中使用变量", "ghost", false),
            Act("更新数据状态", "PUT", "{{baseUrl}}/resources/{{resourceId}}/status", "JSON", "{\n  \"status\": \"active\"\n}", "演示：PUT 请求", "primary", false),
            Act("删除数据", "DELETE", "{{baseUrl}}/resources/{{resourceId}}", "QUERY", "", "演示：DELETE 请求", "danger", false)
        };

        return new JsonObject
        {
            ["environments"] = DefaultEnvs(),
            ["envConfigs"] = envConfigs,
            ["currentEnvId"] = "env-dev",
            ["globals"] = globals.DeepClone(),
            ["headers"] = headers.DeepClone(),
            ["actions"] = actions,
            ["poll"] = new JsonObject
            {
                ["url"] = "{{baseUrl}}/resources/{{resourceId}}/status",
                ["method"] = "GET",
                ["paramType"] = "QUERY",
                ["interval"] = 3,
                ["body"] = "",
                ["useProxy"] = true
            },
            ["settings"] = EmptySettings(),
            ["logs"] = new JsonArray(),
            ["lastRequest"] = "",
            ["lastResponse"] = "",
            ["lastRequestName"] = "",
            ["lastRequestTime"] = "",
            ["lastResponseName"] = "",
            ["lastResponseTime"] = ""
        };
    }

    /* ==================== index 读写 ==================== */

    private JsonObject ReadIndex()
    {
        lock (_lock)
        {
            var idx = ReadJsonFile(IndexFile);
            if (idx["scenes"] is not JsonArray scenes || scenes.Count == 0)
            {
                // 重建
                var (id, scene) = NewDemoScene();
                WriteJsonFile(SceneFile(id), scene);
                idx = BuildIndex(id, new JsonArray { SceneMeta(id, "演示场景", 0) });
                WriteJsonFile(IndexFile, idx);
                return idx;
            }
            if (idx["groups"] is not JsonArray) idx["groups"] = new JsonArray();
            if (idx["ui"] is not JsonObject) idx["ui"] = new JsonObject();
            var groups = (JsonArray)idx["groups"];
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] is not JsonObject g) { groups[i] = new JsonObject(); g = (JsonObject)groups[i]; }
                if (g["order"] == null || Int(g["order"], -1) < 0) g["order"] = i;
                if (g["collapsed"] == null) g["collapsed"] = false;
                if (string.IsNullOrEmpty(Str(g["id"]))) g["id"] = Uid("gr");
            }
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i] is not JsonObject s) continue;
                if (s["groupId"] == null) s["groupId"] = null;
                if (s["order"] == null) s["order"] = i;
            }
            return idx;
        }
    }

    private void WriteIndex(JsonObject idx)
    {
        lock (_lock) { WriteJsonFile(IndexFile, idx); }
    }

    private JsonObject ReadSceneMeta(string id)
    {
        var idx = ReadIndex();
        foreach (var s in (JsonArray)idx["scenes"])
            if (Str(((JsonObject)s)["id"]) == id) return (JsonObject)s;
        return null;
    }

    private JsonObject ReadSceneData(string id)
    {
        string fp = SceneFile(id);
        if (!File.Exists(fp)) return null;
        return MergeScene(ReadJsonFile(fp));
    }

    /* ==================== 场景 / 分组操作 ==================== */

    private JsonObject CreateScene(JsonObject opts)
    {
        opts ??= new JsonObject();
        var idx = ReadIndex();
        JsonObject data;
        if (!string.IsNullOrEmpty(Str(opts["copyFrom"])))
        {
            data = ReadSceneData(Str(opts["copyFrom"]));
            if (data == null) throw new Exception("被复制的场景不存在");
        }
        else if (opts["data"] is JsonObject dd)
        {
            data = MergeScene(dd);
        }
        else
        {
            data = TemplateScene();
        }
        string id = Uid("sc");
        string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        string name = Str(opts["name"]).Trim();
        if (name.Length > 60) name = name.Substring(0, 60);
        if (string.IsNullOrEmpty(name)) name = "新场景";
        int maxOrder = -1;
        foreach (var s in (JsonArray)idx["scenes"])
            if (((JsonObject)s)["groupId"] == null) maxOrder = Math.Max(maxOrder, Int(((JsonObject)s)["order"], 0));
        var meta = new JsonObject
        {
            ["id"] = id, ["name"] = name, ["createdAt"] = now, ["updatedAt"] = now,
            ["groupId"] = null, ["order"] = maxOrder + 1
        };
        ((JsonArray)idx["scenes"]).Add(meta);
        WriteJsonFile(SceneFile(id), data);
        WriteIndex(idx);
        return new JsonObject { ["meta"] = meta.DeepClone(), ["data"] = data };
    }

    private JsonObject RemoveScene(string id)
    {
        var idx = ReadIndex();
        var scenes = (JsonArray)idx["scenes"];
        bool found = false;
        foreach (var s in scenes) if (Str(((JsonObject)s)["id"]) == id) { found = true; break; }
        if (!found) return new JsonObject { ["error"] = "场景不存在" };
        try { File.Delete(SceneFile(id)); } catch { /* 忽略 */ }
        var kept = new List<JsonNode>();
        foreach (var s in scenes) if (Str(((JsonObject)s)["id"]) != id) kept.Add(s);
        if (kept.Count == 0)
        {
            var created = CreateScene(new JsonObject { ["name"] = "演示场景" });
            idx = ReadIndex();
            idx["currentSceneId"] = Str(created["meta"]["id"]);
            WriteIndex(idx);
            return new JsonObject
            {
                ["currentSceneId"] = Str(created["meta"]["id"]),
                ["scenes"] = idx["scenes"].DeepClone(),
                ["groups"] = idx["groups"].DeepClone()
            };
        }
        if (Str(idx["currentSceneId"]) == id) idx["currentSceneId"] = Str(((JsonObject)kept[0])["id"]);
        scenes.Clear();
        foreach (var n in kept) scenes.Add(n);
        WriteIndex(idx);
        return new JsonObject
        {
            ["currentSceneId"] = Str(idx["currentSceneId"]),
            ["scenes"] = scenes.DeepClone(),
            ["groups"] = idx["groups"].DeepClone()
        };
    }

    private JsonObject CreateGroup(string name)
    {
        var idx = ReadIndex();
        string trimmed = name?.Trim() ?? "";
        if (trimmed.Length > 40) trimmed = trimmed.Substring(0, 40);
        if (string.IsNullOrEmpty(trimmed)) trimmed = "新分组";
        var g = new JsonObject
        {
            ["id"] = Uid("gr"),
            ["name"] = trimmed,
            ["order"] = ((JsonArray)idx["groups"]).Count,
            ["collapsed"] = false
        };
        ((JsonArray)idx["groups"]).Add(g);
        WriteIndex(idx);
        return (JsonObject)g.DeepClone();
    }

    private JsonObject UpdateGroup(string id, JsonObject patch)
    {
        var idx = ReadIndex();
        JsonObject g = null;
        foreach (var x in (JsonArray)idx["groups"])
            if (Str(((JsonObject)x)["id"]) == id) { g = (JsonObject)x; break; }
        if (g == null) return null;
        if (patch.ContainsKey("name"))
        {
            string name = Str(patch["name"]).Trim();
            if (name.Length > 40) name = name.Substring(0, 40);
            if (!string.IsNullOrEmpty(name)) g["name"] = name;
        }
        if (patch["collapsed"] is JsonValue cv && cv.TryGetValue<bool>(out var collapsed)) g["collapsed"] = collapsed;
        WriteIndex(idx);
        return (JsonObject)g.DeepClone();
    }

    private JsonObject RemoveGroup(string id)
    {
        var idx = ReadIndex();
        var groups = (JsonArray)idx["groups"];
        bool found = false;
        foreach (var g in groups) if (Str(((JsonObject)g)["id"]) == id) { found = true; break; }
        if (!found) return new JsonObject { ["error"] = "分组不存在" };
        var kept = new List<JsonNode>();
        foreach (var g in groups) if (Str(((JsonObject)g)["id"]) != id) kept.Add(g);
        groups.Clear();
        for (int i = 0; i < kept.Count; i++)
        {
            groups.Add(kept[i]);
            ((JsonObject)kept[i])["order"] = i;
        }
        int maxOrder = -1;
        foreach (var s in (JsonArray)idx["scenes"])
            if (((JsonObject)s)["groupId"] == null) maxOrder = Math.Max(maxOrder, Int(((JsonObject)s)["order"], 0));
        foreach (var s in (JsonArray)idx["scenes"])
            if (Str(((JsonObject)s)["groupId"]) == id)
            {
                ((JsonObject)s)["groupId"] = null;
                ((JsonObject)s)["order"] = ++maxOrder;
            }
        WriteIndex(idx);
        return new JsonObject { ["groups"] = groups.DeepClone(), ["scenes"] = idx["scenes"].DeepClone() };
    }

    private JsonObject ApplyLayout(JsonObject payload)
    {
        var idx = ReadIndex();
        var scenes = (JsonArray)idx["scenes"];
        var groups = (JsonArray)idx["groups"];
        var gSet = new HashSet<string>();
        foreach (var g in groups) gSet.Add(Str(((JsonObject)g)["id"]));

        if (payload["scenes"] is JsonArray pScenes && pScenes.Count > 0)
        {
            var idSet = new HashSet<string>();
            foreach (var s in scenes) idSet.Add(Str(((JsonObject)s)["id"]));
            var seen = new HashSet<string>();
            var orderMap = new Dictionary<string, int>();
            var ordered = new List<JsonNode>();
            foreach (var item in pScenes)
            {
                if (item is not JsonObject o) continue;
                string id = Str(o["id"]);
                if (!idSet.Contains(id) || seen.Contains(id)) continue;
                seen.Add(id);
                JsonObject s = null;
                foreach (var x in scenes) if (Str(((JsonObject)x)["id"]) == id) { s = (JsonObject)x; break; }
                if (s == null) continue;
                string gid = gSet.Contains(Str(o["groupId"])) ? Str(o["groupId"]) : null;
                s["groupId"] = gid;
                string key = gid ?? "__ungrouped__";
                orderMap.TryGetValue(key, out int c);
                s["order"] = c;
                orderMap[key] = c + 1;
                ordered.Add(s);
            }
            foreach (var s in scenes)
                if (!seen.Contains(Str(((JsonObject)s)["id"]))) ordered.Add(s);
            // 先清空旧数组解除父级，再按新顺序重填（保持节点引用）
            scenes.Clear();
            foreach (var n in ordered) scenes.Add(n);
        }

        if (payload["groups"] is JsonArray pGroups && pGroups.Count > 0)
        {
            var gIdSet = new HashSet<string>();
            foreach (var g in groups) gIdSet.Add(Str(((JsonObject)g)["id"]));
            var seen = new HashSet<string>();
            var ordered = new List<JsonNode>();
            foreach (var item in pGroups)
            {
                if (item is not JsonObject o) continue;
                string id = Str(o["id"]);
                if (!gIdSet.Contains(id) || seen.Contains(id)) continue;
                seen.Add(id);
                JsonObject g = null;
                foreach (var x in groups) if (Str(((JsonObject)x)["id"]) == id) { g = (JsonObject)x; break; }
                if (g == null) continue;
                g["order"] = ordered.Count;
                ordered.Add(g);
            }
            foreach (var g in groups)
                if (!seen.Contains(Str(((JsonObject)g)["id"])))
                {
                    ((JsonObject)g)["order"] = ordered.Count;
                    ordered.Add(g);
                }
            groups.Clear();
            foreach (var n in ordered) groups.Add(n);
        }

        WriteIndex(idx);
        return idx;
    }

    /* ==================== 代理请求 ==================== */

    private static async Task<JsonObject> DoProxy(JsonObject p)
    {
        string urlStr = Str(p["url"]).Trim();
        var started = DateTime.UtcNow;
        if (!urlStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !urlStr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return ProxyError("代理模式仅支持 http/https 绝对地址", started);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (p["headers"] is JsonObject hdrObj)
        {
            foreach (var kv in hdrObj)
            {
                string k = kv.Key.Trim();
                if (!string.IsNullOrEmpty(k)) headers[k] = Str(kv.Value);
            }
        }
        headers["Accept-Encoding"] = "identity";

        string method = Str(p["method"]).ToUpperInvariant();
        if (!Methods.Contains(method) && method != "HEAD") method = "GET";
        string body = Str(p["body"]);
        if (string.IsNullOrEmpty(body)) body = null;
        int timeout = Math.Clamp(Int(p["timeout"], 30000), 1000, 300000);
        bool insecure = Bool(p["insecure"], false);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(30)
        };
        if (insecure)
        {
            handler.SslOptions.RemoteCertificateValidationCallback =
                (object s, X509Certificate c, X509Chain ch, SslPolicyErrors e) => true;
        }
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeout) };

        string curUrl = urlStr;
        string curMethod = method;
        string curBody = body;
        for (int depth = 0; depth < 6; depth++)
        {
            HttpResponseMessage resp;
            try
            {
                using var req = new HttpRequestMessage(new HttpMethod(curMethod), curUrl);
                foreach (var kv in headers)
                    try { req.Headers.TryAddWithoutValidation(kv.Key, kv.Value); } catch { }
                if (curBody != null && curMethod != "GET" && curMethod != "HEAD")
                    req.Content = new StringContent(curBody, Encoding.UTF8, headers.ContainsKey("Content-Type") ? headers["Content-Type"] : "application/json");
                resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            }
            catch (TaskCanceledException)
            {
                return ProxyError("请求超时（" + timeout + "ms）", started);
            }
            catch (Exception ex)
            {
                return ProxyError(ex.Message, started);
            }

            int status = (int)resp.StatusCode;
            bool redirect = (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                           && resp.Headers.Location != null && depth < 5;
            if (redirect)
            {
                string next = new Uri(new Uri(curUrl), resp.Headers.Location).ToString();
                resp.Dispose();
                if (status == 303) { curMethod = "GET"; curBody = null; }
                curUrl = next;
                continue;
            }

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
            string contentType = resp.Content.Headers.ContentType?.ToString() ?? "";
            bool isText = System.Text.RegularExpressions.Regex.IsMatch(contentType,
                               "text/|json|xml|javascript|form-urlencoded|html", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                          || contentType == "";
            double duration = (DateTime.UtcNow - started).TotalMilliseconds;
            return new JsonObject
            {
                ["ok"] = status >= 200 && status < 300,
                ["status"] = status,
                ["statusText"] = resp.ReasonPhrase ?? "",
                ["contentType"] = contentType,
                ["duration"] = Math.Round(duration),
                ["binary"] = !isText,
                ["size"] = bytes.Length,
                ["body"] = isText ? Encoding.UTF8.GetString(bytes) : Convert.ToBase64String(bytes)
            };
        }
        return ProxyError("重定向次数过多", started);
    }

    private static JsonObject ProxyError(string msg, DateTime started)
    {
        return new JsonObject
        {
            ["ok"] = false,
            ["error"] = msg,
            ["duration"] = Math.Round((DateTime.UtcNow - started).TotalMilliseconds)
        };
    }

    /* ==================== 工具 ==================== */

    private static string Str(JsonNode n)
    {
        if (n == null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
            if (v.TryGetValue<long>(out var l)) return l.ToString();
            if (v.TryGetValue<double>(out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return n.ToJsonString(JsonOpts);
    }

    private static bool Bool(JsonNode n, bool def)
    {
        if (n is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        return def;
    }

    private static int Int(JsonNode n, int def)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<long>(out var l)) return (int)l;
            if (v.TryGetValue<double>(out var d)) return (int)d;
        }
        return def;
    }

    private static JsonObject Parse(string text)
    {
        try { return JsonNode.Parse(string.IsNullOrEmpty(text) ? "{}" : text) as JsonObject ?? new JsonObject(); }
        catch { return new JsonObject(); }
    }

    /* ==================== HTTP 路由 ==================== */

    private async Task WriteJson(HttpContext ctx, int code, JsonNode obj)
    {
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        // JsonNode 是树结构，响应对象可能引用 index 子树——统一深拷贝后再序列化
        var safe = obj == null ? new JsonObject() : obj.DeepClone();
        await ctx.Response.WriteAsync(safe.ToJsonString(JsonOpts), Encoding.UTF8);
    }

    private async Task ServeFile(HttpContext ctx, string fp)
    {
        byte[] buf = null;
        try { buf = await File.ReadAllBytesAsync(fp); }
        catch { /* 外部文件缺失，走嵌入资源 */ }
        if (buf == null || buf.Length == 0)
        {
            // 嵌入资源兜底：exe 被单独拷贝（无 public 目录）时依然能打开界面
            try
            {
                using var s = typeof(AppServer).Assembly.GetManifestResourceStream("HttpTool.public.index.html");
                if (s != null)
                {
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    buf = ms.ToArray();
                }
            }
            catch { buf = null; }
        }
        if (buf == null)
        {
            await WriteJson(ctx, 404, new JsonObject { ["error"] = "Not Found" });
            return;
        }
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-store";
        await ctx.Response.Body.WriteAsync(buf);
    }

    private async Task HandleAsync(HttpContext ctx)
    {
        var req = ctx.Request;
        string path = req.Path.Value ?? "/";
        try
        {
            if (req.Method == "GET" && (path == "/" || path == "/index.html"))
            {
                await ServeFile(ctx, Path.Combine(_publicDir, "index.html"));
                return;
            }
            if (req.Method == "GET" && path.StartsWith("/assets/"))
            {
                await ServeFile(ctx, Path.Combine(_publicDir, path.Substring(1)));
                return;
            }
            if (path == "/api/state" && req.Method == "GET")
            {
                var idx = ReadIndex();
                await WriteJson(ctx, 200, new JsonObject
                {
                    ["currentSceneId"] = Str(idx["currentSceneId"]),
                    ["scenes"] = idx["scenes"].DeepClone(),
                    ["groups"] = idx["groups"].DeepClone(),
                    ["ui"] = idx["ui"].DeepClone(),
                    ["dataDir"] = _dataDir
                });
                return;
            }
            if (path == "/api/ui" && req.Method == "PUT")
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                var u = Parse(body);
                var idx = ReadIndex();
                var ui = idx["ui"] as JsonObject ?? new JsonObject();
                idx["ui"] = ui;
                int aw = Int(u["actionWidth"], 0);
                if (aw >= 200) ui["actionWidth"] = aw;
                int pw = Int(u["pollWidth"], 0);
                if (pw >= 200) ui["pollWidth"] = pw;
                int sw = Int(u["sidebarWidth"], 0);
                if (sw >= 160) ui["sidebarWidth"] = sw;
                if (u["collapsedPoll"] is JsonValue cv && cv.TryGetValue<bool>(out var collapsed)) ui["collapsedPoll"] = collapsed;
                WriteIndex(idx);
                await WriteJson(ctx, 200, ui.DeepClone());
                return;
            }
            if (path == "/api/groups" && req.Method == "POST")
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                await WriteJson(ctx, 200, CreateGroup(Str(Parse(body)["name"])));
                return;
            }
            if (path == "/api/layout" && req.Method == "PUT")
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                var idx = ApplyLayout(Parse(body));
                await WriteJson(ctx, 200, new JsonObject { ["scenes"] = idx["scenes"].DeepClone(), ["groups"] = idx["groups"].DeepClone() });
                return;
            }
            if (path.StartsWith("/api/groups/"))
            {
                string gid = path.Substring("/api/groups/".Length);
                if (req.Method == "PATCH")
                {
                    var body = await new StreamReader(req.Body).ReadToEndAsync();
                    var g = UpdateGroup(gid, Parse(body));
                    if (g == null) { await WriteJson(ctx, 404, new JsonObject { ["error"] = "分组不存在" }); return; }
                    await WriteJson(ctx, 200, g);
                    return;
                }
                if (req.Method == "DELETE")
                {
                    await WriteJson(ctx, 200, RemoveGroup(gid));
                    return;
                }
            }
            if (path == "/api/template" && req.Method == "GET")
            {
                await WriteJson(ctx, 200, TemplateScene());
                return;
            }
            if (path == "/api/scenes" && req.Method == "POST")
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                await WriteJson(ctx, 200, CreateScene(Parse(body)));
                return;
            }
            if (path == "/api/current" && req.Method == "PUT")
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                var idx = ReadIndex();
                string id = Str(Parse(body)["sceneId"]);
                bool exists = false;
                foreach (var s in (JsonArray)idx["scenes"]) if (Str(((JsonObject)s)["id"]) == id) { exists = true; break; }
                if (!exists) { await WriteJson(ctx, 404, new JsonObject { ["error"] = "场景不存在" }); return; }
                idx["currentSceneId"] = id;
                WriteIndex(idx);
                await WriteJson(ctx, 200, new JsonObject { ["currentSceneId"] = id });
                return;
            }
            if (path.StartsWith("/api/scenes/"))
            {
                string id = path.Substring("/api/scenes/".Length);
                if (req.Method == "GET")
                {
                    var meta = ReadSceneMeta(id);
                    var data = ReadSceneData(id);
                    if (meta == null || data == null) { await WriteJson(ctx, 404, new JsonObject { ["error"] = "场景不存在" }); return; }
                    await WriteJson(ctx, 200, new JsonObject { ["meta"] = meta.DeepClone(), ["data"] = data });
                    return;
                }
                if (req.Method == "PUT")
                {
                    var body = await new StreamReader(req.Body).ReadToEndAsync();
                    if (ReadSceneMeta(id) == null) { await WriteJson(ctx, 404, new JsonObject { ["error"] = "场景不存在" }); return; }
                    WriteJsonFile(SceneFile(id), MergeScene(Parse(body)["data"] as JsonObject));
                    var idx = ReadIndex();
                    JsonObject it = null;
                    foreach (var s in (JsonArray)idx["scenes"]) if (Str(((JsonObject)s)["id"]) == id) { it = (JsonObject)s; break; }
                    it["updatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
                    WriteIndex(idx);
                    await WriteJson(ctx, 200, new JsonObject { ["meta"] = it.DeepClone() });
                    return;
                }
                if (req.Method == "PATCH")
                {
                    var body = await new StreamReader(req.Body).ReadToEndAsync();
                    if (ReadSceneMeta(id) == null) { await WriteJson(ctx, 404, new JsonObject { ["error"] = "场景不存在" }); return; }
                    string name = Str(Parse(body)["name"]).Trim();
                    if (name.Length > 60) name = name.Substring(0, 60);
                    if (string.IsNullOrEmpty(name)) { await WriteJson(ctx, 400, new JsonObject { ["error"] = "名称不能为空" }); return; }
                    var idx = ReadIndex();
                    JsonObject it = null;
                    foreach (var s in (JsonArray)idx["scenes"]) if (Str(((JsonObject)s)["id"]) == id) { it = (JsonObject)s; break; }
                    it["name"] = name;
                    it["updatedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
                    WriteIndex(idx);
                    await WriteJson(ctx, 200, new JsonObject { ["meta"] = it.DeepClone() });
                    return;
                }
                if (req.Method == "DELETE")
                {
                    await WriteJson(ctx, 200, RemoveScene(id));
                    return;
                }
            }
            if (path == "/api/proxy" && req.Method == "POST")
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                await WriteJson(ctx, 200, await DoProxy(Parse(body)));
                return;
            }
            if (path == "/api/logs" && req.Method == "POST")
            {
                var body = await new StreamReader(req.Body).ReadToEndAsync();
                var payload = Parse(body);
                string sceneId = System.Text.RegularExpressions.Regex.Replace(Str(payload["sceneId"]) != "" ? Str(payload["sceneId"]) : "default", @"[^\w\-]", "");
                string envId = System.Text.RegularExpressions.Regex.Replace(Str(payload["envId"]) != "" ? Str(payload["envId"]) : "default", @"[^\w\-]", "");
                var entry = payload["entry"] as JsonObject;
                if (entry == null)
                {
                    await WriteJson(ctx, 400, new JsonObject { ["error"] = "缺少日志内容" });
                    return;
                }
                var now = DateTime.Now;
                string ym = now.ToString("yyyy-MM");
                string dir = Path.Combine(_dataDir, "logs", sceneId, envId, ym);
                Directory.CreateDirectory(dir);
                var line = (JsonObject)entry.DeepClone();
                line["sceneId"] = sceneId;
                line["envId"] = envId;
                File.AppendAllText(
                    Path.Combine(dir, now.ToString("yyyy-MM-dd") + ".jsonl"),
                    line.ToJsonString(JsonOpts) + "\n", new UTF8Encoding(false));
                await WriteJson(ctx, 200, new JsonObject { ["ok"] = true });
                return;
            }
            if (path == "/api/logs" && req.Method == "DELETE")
            {
                string sceneId = System.Text.RegularExpressions.Regex.Replace(ctx.Request.Query["sceneId"].ToString(), @"[^\w\-]", "");
                string envId = System.Text.RegularExpressions.Regex.Replace(ctx.Request.Query["envId"].ToString(), @"[^\w\-]", "");
                string date = ctx.Request.Query["date"].ToString();
                if (string.IsNullOrEmpty(sceneId) || string.IsNullOrEmpty(envId))
                {
                    await WriteJson(ctx, 400, new JsonObject { ["error"] = "缺少 sceneId 或 envId" });
                    return;
                }
                if (!System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
                {
                    await WriteJson(ctx, 400, new JsonObject { ["error"] = "日期格式应为 YYYY-MM-DD" });
                    return;
                }
                string ym = date.Substring(0, 7);
                string fp = Path.Combine(_dataDir, "logs", sceneId, envId, ym, date + ".jsonl");
                bool removed = false;
                if (File.Exists(fp))
                {
                    File.Delete(fp);
                    removed = true;
                }
                await WriteJson(ctx, 200, new JsonObject { ["ok"] = true, ["removed"] = removed });
                return;
            }
            if (path == "/api/logs" && req.Method == "GET")
            {
                string sceneId = System.Text.RegularExpressions.Regex.Replace(ctx.Request.Query["sceneId"].ToString(), @"[^\w\-]", "");
                string envId = System.Text.RegularExpressions.Regex.Replace(ctx.Request.Query["envId"].ToString(), @"[^\w\-]", "");
                string date = ctx.Request.Query["date"].ToString();
                if (string.IsNullOrEmpty(sceneId) || string.IsNullOrEmpty(envId))
                {
                    await WriteJson(ctx, 400, new JsonObject { ["error"] = "缺少 sceneId 或 envId" });
                    return;
                }
                if (!System.Text.RegularExpressions.Regex.IsMatch(date, @"^\d{4}-\d{2}-\d{2}$"))
                {
                    await WriteJson(ctx, 400, new JsonObject { ["error"] = "日期格式应为 YYYY-MM-DD" });
                    return;
                }
                string ym = date.Substring(0, 7);
                string fp = Path.Combine(_dataDir, "logs", sceneId, envId, ym, date + ".jsonl");
                if (!File.Exists(fp))
                {
                    await WriteJson(ctx, 200, new JsonArray());
                    return;
                }
                var items = new JsonArray();
                foreach (var lineRaw in File.ReadAllLines(fp, Encoding.UTF8))
                {
                    var t = lineRaw.Trim();
                    if (t.Length == 0) continue;
                    try { items.Add(JsonNode.Parse(t)); } catch { /* 跳过坏行 */ }
                }
                await WriteJson(ctx, 200, items);
                return;
            }
            await WriteJson(ctx, 404, new JsonObject { ["error"] = "Not Found" });
        }
        catch (Exception e)
        {
            try { await WriteJson(ctx, 500, new JsonObject { ["error"] = e.Message }); }
            catch { /* 忽略 */ }
        }
    }
}
