using System.Reflection;
using GrpcProxy;
using GrpcProxy.Grpc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions() { Args = args, ContentRootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) });

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddControllers();
builder.Services.AddGrpc().AddProxy();
builder.Services.Configure<GrpcProxyOptions>(builder.Configuration.GetSection("GrpcProxy"));

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

var protoOptions = app.Services.GetRequiredService<IOptions<GrpcProxyOptions>>().Value;
foreach (var mapping in protoOptions.Mappings ?? Enumerable.Empty<GrpcProxyMapping>())
    if (mapping.ServiceAddress != null && mapping.ProtoFilePath != null)
        await ProtoLoader.LoadProtoFileAsync(app.Services.GetRequiredService<IProxyServiceRepository>(), mapping.ServiceAddress, mapping.ProtoFilePath);

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

