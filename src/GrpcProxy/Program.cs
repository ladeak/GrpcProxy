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

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapGrpcService();

var startupMapping = app.Services.GetRequiredService<IOptions<GrpcProxyMapping>>().Value;
if (startupMapping.Address != null && startupMapping.ProtoPath != null)
    await ProtoLoader.LoadProtoFileAsync(app.Services.GetRequiredService<IProxyServiceRepository>(), startupMapping.Address, startupMapping.ProtoPath);
var protoOptions = app.Services.GetRequiredService<IOptions<GrpcProxyOptions>>().Value;
foreach (var mapping in protoOptions.Mappings ?? Enumerable.Empty<GrpcProxyMapping>())
    if (mapping.Address != null && mapping.ProtoPath != null)
        await ProtoLoader.LoadProtoFileAsync(app.Services.GetRequiredService<IProxyServiceRepository>(), mapping.Address, mapping.ProtoPath);

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

