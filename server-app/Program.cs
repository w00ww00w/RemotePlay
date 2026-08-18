using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
var queues = new ConcurrentDictionary<string, ConcurrentQueue<JsonElement>>();
var page = ReadResource("index.html");
var port = 0;
string[] tvUrls = [];

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(0));
var app = builder.Build();

bool IsLocal(HttpContext context) => IPAddress.IsLoopback(context.Connection.RemoteIpAddress!);
bool Authorized(HttpContext context) => IsLocal(context) ||
    context.Request.Query["token"] == token || context.Request.Cookies["remoteplay"] == token;

app.MapGet("/tv", context =>
{
    context.Response.Cookies.Append("remoteplay", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = "/"
    });
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers.CacheControl = "no-store";
    return context.Response.WriteAsync(page);
});

app.MapGet("/", async context =>
{
    if (!Authorized(context)) { context.Response.StatusCode = 403; return; }
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers.CacheControl = "no-store";
    await context.Response.WriteAsync(page);
});

app.MapGet("/api/config", (HttpContext context) =>
    Authorized(context) ? Results.Json(new { token, tvUrls }) : Results.Json(new { error = "Недействительная ссылка" }, statusCode: 403));

app.MapPost("/api/send", async (HttpContext context) =>
{
    if (!Authorized(context)) return Results.Json(new { error = "Недействительная ссылка" }, statusCode: 403);
    try
    {
        var message = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
        if (!message.TryGetProperty("to", out var to) || string.IsNullOrWhiteSpace(to.GetString()) ||
            !message.TryGetProperty("from", out _) || !message.TryGetProperty("type", out _))
            return Results.Json(new { error = "Некорректное сообщение" }, statusCode: 400);
        var queue = queues.GetOrAdd(to.GetString()!, _ => new ConcurrentQueue<JsonElement>());
        queue.Enqueue(message.Clone());
        while (queue.Count > 100) queue.TryDequeue(out _);
        return Results.Json(new { ok = true });
    }
    catch (JsonException)
    {
        return Results.Json(new { error = "Некорректный JSON" }, statusCode: 400);
    }
});

app.MapGet("/api/poll", (HttpContext context) =>
{
    if (!Authorized(context)) return Results.Json(new { error = "Недействительная ссылка" }, statusCode: 403);
    var id = context.Request.Query["id"].ToString();
    if (string.IsNullOrWhiteSpace(id)) return Results.Json(new { error = "Нужен id" }, statusCode: 400);
    var messages = new List<JsonElement>();
    if (queues.TryGetValue(id, out var queue))
        while (queue.TryDequeue(out var message)) messages.Add(message);
    return Results.Json(messages);
});

await app.StartAsync();
port = new Uri(app.Urls.Single()).Port;
tvUrls = LocalAddresses().Select(address => $"http://{address}:{port}/tv").ToArray();
var localUrl = $"http://localhost:{port}";
Console.WriteLine($"\nНа компьютере: {localUrl}");
Console.WriteLine("На телевизоре:");
foreach (var address in tvUrls) Console.WriteLine($"  {address}");
Console.WriteLine("\nНе закрывайте это окно. Для остановки нажмите Ctrl+C.\n");

try
{
    if (OperatingSystem.IsWindows())
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" \"{localUrl}\"") { CreateNoWindow = true, UseShellExecute = false });
    else if (OperatingSystem.IsMacOS())
        Process.Start(new ProcessStartInfo("/usr/bin/open", localUrl) { UseShellExecute = false });
    else
        Process.Start(new ProcessStartInfo("xdg-open", localUrl) { UseShellExecute = false });
}
catch { Console.WriteLine($"Откройте вручную: {localUrl}"); }

await app.WaitForShutdownAsync();
return 0;

static string ReadResource(string name)
{
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!;
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static IEnumerable<IPAddress> LocalAddresses() => NetworkInterface.GetAllNetworkInterfaces()
    .Where(x => x.OperationalStatus == OperationalStatus.Up)
    .SelectMany(x => x.GetIPProperties().UnicastAddresses)
    .Select(x => x.Address)
    .Where(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))
    .OrderByDescending(x => x.ToString().StartsWith("192.168.") || x.ToString().StartsWith("10."));
