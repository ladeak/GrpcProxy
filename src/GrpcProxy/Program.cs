using System.Reflection;
using GrpcProxy;
using GrpcProxy.Grpc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions() { Args = args, ContentRootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) });
builder.Configuration.AddCommandLine(args);
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();
builder.Services.AddGrpc().AddProxy();
builder.Services.Configure<GrpcProxyOptions>(builder.Configuration.GetSection("GrpcProxy"));
builder.Services.Configure<GrpcProxyMapping>(builder.Configuration.GetSection("p"));

if (!string.IsNullOrWhiteSpace(builder.Configuration.GetValue<string>("p:SeqAddress")))
    builder.Logging.AddSeq(builder.Configuration.GetValue<string>("p:SeqAddress"), builder.Configuration.GetValue<string>("p:SeqKey"));

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapGrpcService();

var startupMapping = app.Services.GetRequiredService<IOptions<GrpcProxyMapping>>().Value;
await ProtoLoader.LoadProtoFileAsync(app.Services.GetRequiredService<IProxyServiceRepository>(), startupMapping);
var protoOptions = app.Services.GetRequiredService<IOptions<GrpcProxyOptions>>().Value;
foreach (var mapping in protoOptions.Mappings ?? Enumerable.Empty<GrpcProxyMapping>())
    await ProtoLoader.LoadProtoFileAsync(app.Services.GetRequiredService<IProxyServiceRepository>(), mapping);

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

