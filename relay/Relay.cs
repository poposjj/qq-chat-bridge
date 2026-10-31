using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/**
 * QQ Chat Bridge 中转网关。
 *
 * 只暴露两个端点，且都要 Bearer Token：
 *   /ws            WebSocket 事件流（转发 NapCat 的 OneBot WS）
 *   /api/<action>  一次性 API 调用（转发 NapCat 的 HTTP）
 *
 * NapCat 本身只监听 127.0.0.1，公网只能看到本网关。
 */
internal static class Relay
{
    private static string napcatWs = "ws://127.0.0.1:3001";
    private static string napcatHttp = "http://127.0.0.1:3000";
    private static string token = "";
    private static string napcatToken = "";
    private static readonly HashSet<string> allowIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static int listenPort = 18574;
    private static string listenHost = "+";
    private static int maxClients = 64;
    private static int ratePerMinute = 120;
    private static string logFile = "relay.log";
    private static int activeClients;
    private static readonly ConcurrentDictionary<string, Queue<DateTime>> hits =
            new ConcurrentDictionary<string, Queue<DateTime>>();
    private static readonly object logLock = new object();

    private static void Main(string[] args)
    {
        LoadConfig(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "relay.ini"));
        if (token.Length < 16)
        {
            Console.WriteLine("relay.ini 里的 token 太短，至少 16 位。");
            return;
        }

        HttpListener listener = new HttpListener();
        listener.Prefixes.Add("http://" + listenHost + ":" + listenPort + "/");
        try
        {
            listener.Start();
        }
        catch (Exception)
        {
            // 非管理员时给当前端口加 URL ACL 再重试一次
            try
            {
                System.Diagnostics.Process netsh = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("netsh",
                        "http add urlacl url=http://" + listenHost + ":" + listenPort + "/ user=Everyone")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                netsh.WaitForExit(5000);
                listener.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("监听失败（需要管理员或端口被占用）：" + ex.Message);
                Console.WriteLine("请右键“以管理员身份运行” start-relay.cmd");
                try { Console.ReadLine(); } catch (Exception) { }
                return;
            }
        }

        Log("relay started on http://0.0.0.0:" + listenPort + "/");
        Log("upstream ws=" + napcatWs + " http=" + napcatHttp);
        Log("allowIps=" + (allowIps.Count == 0 ? "(all)" : string.Join(",", allowIps)));
        Console.WriteLine("QQ Relay 已启动，端口 " + listenPort + "，Ctrl+C 退出");

        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = listener.GetContext();
            }
            catch (Exception)
            {
                break;
            }
            ThreadPool.QueueUserWorkItem(delegate { Handle(context); });
        }
    }

    private static void LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine("找不到 " + path);
            return;
        }
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            string key = line.Substring(0, eq).Trim();
            string value = line.Substring(eq + 1).Trim();
            switch (key)
            {
                case "listenPort": int.TryParse(value, out listenPort); break;
                case "listenHost": listenHost = value; break;
                case "napcatWs": napcatWs = value; break;
                case "napcatHttp": napcatHttp = value; break;
                case "token": token = value; break;
                case "napcatToken": napcatToken = value; break;
                case "maxClients": int.TryParse(value, out maxClients); break;
                case "ratePerMinute": int.TryParse(value, out ratePerMinute); break;
                case "logFile": logFile = value; break;
                case "allowIps":
                    allowIps.Clear();
                    foreach (string ip in value.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        allowIps.Add(ip.Trim());
                    }
                    break;
            }
        }
    }

    private static void Handle(HttpListenerContext context)
    {
        string ip = context.Request.RemoteEndPoint == null ? "?" : context.Request.RemoteEndPoint.Address.ToString();
        try
        {
            if (allowIps.Count > 0 && !allowIps.Contains(ip))
            {
                Fail(context, 403);
                return;
            }
            if (!RateOk(ip))
            {
                Fail(context, 429);
                return;
            }
            if (!AuthOk(context.Request))
            {
                Fail(context, 401);
                return;
            }

            string path = context.Request.Url == null ? "/" : context.Request.Url.AbsolutePath;
            if (path == "/client-config" || path == "/client-config.json")
            {
                ServeClientConfig(context);
                return;
            }
            if (path == "/ws" || path == "/")
            {
                if (!context.Request.IsWebSocketRequest)
                {
                    Fail(context, 400);
                    return;
                }
                if (Interlocked.Increment(ref activeClients) > maxClients)
                {
                    Interlocked.Decrement(ref activeClients);
                    Fail(context, 503);
                    return;
                }
                try
                {
                    ProxyWebSocket(context).Wait();
                }
                finally
                {
                    Interlocked.Decrement(ref activeClients);
                }
                return;
            }

            if (path.StartsWith("/api/"))
            {
                ProxyApi(context, path.Substring(5)).Wait();
                return;
            }

            Fail(context, 404);
        }
        catch (Exception ex)
        {
            Log("error " + ip + " " + ex.Message);
            try { Fail(context, 500); } catch (Exception) { }
        }
    }

    /** 下发共享配置：同样需要 Bearer Token，只读。 */
    private static void ServeClientConfig(HttpListenerContext context)
    {
        if (context.Request.HttpMethod != "GET")
        {
            Fail(context, 405);
            return;
        }
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client-config.json");
        if (!File.Exists(path))
        {
            Fail(context, 404);
            return;
        }
        byte[] data = File.ReadAllBytes(path);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentLength64 = data.Length;
        context.Response.OutputStream.Write(data, 0, data.Length);
        context.Response.Close();
    }
    private static string UpstreamToken()
    {
        return string.IsNullOrWhiteSpace(napcatToken) ? token : napcatToken;
    }
    private static bool AuthOk(HttpListenerRequest request)
    {
        string header = request.Headers["Authorization"];
        if (header != null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return FixedEquals(header.Substring(7).Trim(), token);
        }
        string query = request.QueryString["token"];
        return query != null && FixedEquals(query, token);
    }

    private static bool FixedEquals(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length)
        {
            return false;
        }
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    private static bool RateOk(string ip)
    {
        if (ratePerMinute <= 0)
        {
            return true;
        }
        Queue<DateTime> queue = hits.GetOrAdd(ip, delegate { return new Queue<DateTime>(); });
        lock (queue)
        {
            DateTime now = DateTime.UtcNow;
            while (queue.Count > 0 && (now - queue.Peek()).TotalSeconds > 60)
            {
                queue.Dequeue();
            }
            if (queue.Count >= ratePerMinute)
            {
                return false;
            }
            queue.Enqueue(now);
            return true;
        }
    }

    private static async Task ProxyWebSocket(HttpListenerContext context)
    {
        HttpListenerWebSocketContext clientContext = await context.AcceptWebSocketAsync(null);
        using (WebSocket client = clientContext.WebSocket)
        using (ClientWebSocket upstream = new ClientWebSocket())
        {
            upstream.Options.SetRequestHeader("Authorization", "Bearer " + UpstreamToken());
            await upstream.ConnectAsync(new Uri(napcatWs), CancellationToken.None);

            Task a = Pump(client, upstream);
            Task b = Pump(upstream, client);
            await Task.WhenAny(a, b);
            try { client.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(2000); } catch (Exception) { }
            try { upstream.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(2000); } catch (Exception) { }
        }
    }

    private static async Task Pump(WebSocket from, WebSocket to)
    {
        byte[] buffer = new byte[16 * 1024];
        try
        {
            while (from.State == WebSocketState.Open && to.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await from.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                await to.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count),
                        result.MessageType, result.EndOfMessage, CancellationToken.None);
            }
        }
        catch (Exception)
        {
        }
    }

    private static async Task ProxyApi(HttpListenerContext context, string action)
    {
        if (context.Request.HttpMethod != "POST")
        {
            Fail(context, 405);
            return;
        }
        string body;
        using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync();
        }

        string target = napcatHttp.TrimEnd('/') + "/" + action;
        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(target);
        request.Method = "POST";
        request.ContentType = "application/json";
        request.Timeout = 12000;
        request.Headers["Authorization"] = "Bearer " + UpstreamToken();
        byte[] data = Encoding.UTF8.GetBytes(body ?? "{}");
        request.ContentLength = data.Length;
        using (Stream stream = await request.GetRequestStreamAsync())
        {
            await stream.WriteAsync(data, 0, data.Length);
        }

        using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
        using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
        {
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            byte[] payload = Encoding.UTF8.GetBytes(await reader.ReadToEndAsync());
            context.Response.ContentLength64 = payload.Length;
            await context.Response.OutputStream.WriteAsync(payload, 0, payload.Length);
        }
        context.Response.Close();
    }

    private static void Fail(HttpListenerContext context, int status)
    {
        context.Response.StatusCode = status;
        context.Response.ContentLength64 = 0;
        context.Response.Close();
    }

    private static void Log(string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message;
        try
        {
            lock (logLock)
            {
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logFile),
                        line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
        }
        Console.WriteLine(line);
    }
}
