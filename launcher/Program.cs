using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;

Console.Write("Адрес RemotePlay-сервера (ссылка /tv): ");
var entered = args.FirstOrDefault() ?? Console.ReadLine();
if (!Uri.TryCreate(entered, UriKind.Absolute, out var target) ||
    target.Scheme is not ("http" or "https"))
{
    Console.Error.WriteLine("Некорректный адрес. Пример: http://192.168.1.10:8080/tv");
    return 1;
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
var app = builder.Build();
var client = new HttpClient(new SocketsHttpHandler
{
    AllowAutoRedirect = false,
    UseCookies = false
});

app.Run(async context =>
{
    var path = context.Request.Path == "/" ? "/tv" : context.Request.Path.Value;
    var destination = new Uri(target, path + context.Request.QueryString);
    using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), destination);

    foreach (var header in context.Request.Headers)
        if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());

    if (context.Request.ContentLength > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
    {
        request.Content = new StreamContent(context.Request.Body);
        if (!string.IsNullOrEmpty(context.Request.ContentType))
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
    }

    try
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers.Concat(response.Content.Headers))
            context.Response.Headers[header.Key] = header.Value.ToArray();
        context.Response.Headers.Remove("transfer-encoding");
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
    catch (Exception error) when (error is HttpRequestException or SocketException)
    {
        context.Response.StatusCode = 502;
        await context.Response.WriteAsync($"Основной сервер недоступен: {error.Message}");
    }
});

await app.StartAsync();
var localUrl = app.Urls.Single();
Console.WriteLine($"Открываю {localUrl}");
Console.WriteLine("Для остановки нажмите Ctrl+C.");

try
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        Process.Start("open", localUrl);
    else
        Process.Start(new ProcessStartInfo(localUrl) { UseShellExecute = true });
}
catch
{
    Console.WriteLine($"Откройте вручную: {localUrl}");
}

await app.WaitForShutdownAsync();
return 0;
