using ResourceReservation.Api.Extensions;
using ResourceReservation.Infrastructure;
using ResourceReservation.Infrastructure.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiAuthentication(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();   // must come before UseAuthorization
app.UseAuthorization();
app.MapControllers();

await IdentitySeeder.InitializeAsync(app.Services, applyMigrations: app.Environment.IsDevelopment());

app.Run();
