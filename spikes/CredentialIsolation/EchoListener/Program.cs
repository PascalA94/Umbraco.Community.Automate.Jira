// Spike-only: logs every request's method, URL, headers and body to stdout so the
// isolation spike can see exactly what an Automate HTTP Request step sent.
var app = WebApplication.CreateBuilder(args).Build();
app.Run(async ctx =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    var headers = string.Join(" | ", ctx.Request.Headers.Select(h => $"{h.Key}: {h.Value}"));
    Console.WriteLine($"ECHO {ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString} || {headers} || BODY {body}");
    await ctx.Response.WriteAsync("ok");
});
app.Run("http://localhost:5099");
