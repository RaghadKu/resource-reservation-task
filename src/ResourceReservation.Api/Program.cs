using ResourceReservation.Api.Extensions;
using ResourceReservation.Application;
using ResourceReservation.Infrastructure;
using ResourceReservation.Infrastructure.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddApiAuthentication(builder.Configuration);

builder.Services.AddApiExceptionHandling();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddSwagger();

WebApplication app = builder.Build();

app.ConfigurePipeline();

await IdentitySeeder.InitializeAsync(app.Services, applyMigrations: app.Environment.IsDevelopment());

await app.RunAsync();
