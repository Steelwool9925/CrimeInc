var builder = WebApplication.CreateBuilder(args);

// OpenAPI document (Swagger UI wired up in S0.2 alongside /health).
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();

// Exposed so integration tests can spin up the host via WebApplicationFactory.
public partial class Program { }
