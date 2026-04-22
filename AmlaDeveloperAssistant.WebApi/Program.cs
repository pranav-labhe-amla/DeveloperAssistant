var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi(); // ✅ correct for .NET 9

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // ✅ exposes /openapi/v1.json
}

app.UseHttpsRedirection();
app.MapControllers();
app.UseRouting();
app.Run();