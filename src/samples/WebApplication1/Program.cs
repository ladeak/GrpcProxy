using Service;

var builder = WebApplication.CreateBuilder();

builder.Services.AddControllers();
builder.Services.AddGrpc();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapGrpcService<SuperServiceImpl>();
app.Run();