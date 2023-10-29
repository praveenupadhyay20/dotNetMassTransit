using CommandCenter.Setting;
using MassTransit;
using Microsoft.OpenApi.Models;
using Polly;
using Polly.Timeout;
using ProcessCenter.Client;
using ProcessCenter.Entity;
using ProcessCenter.MongoDB;
using ProcessCenter.Settings;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddMongo().AddMongoRepository<Process>("processItems").AddMongoRepository<Order>("pizzaItems");

var configuration = builder.Configuration;

var rabbitMqSettings = configuration.GetSection(nameof(RabbitMQSettings)).Get<RabbitMQSettings>();
var serviceSettings = configuration.GetSection(nameof(ServiceSettings)).Get<ServiceSettings>();
builder.Services.AddMassTransit(mass =>
{
    mass.AddConsumers(Assembly.GetEntryAssembly());
    mass.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitMqSettings.Host);

        cfg.ConfigureEndpoints(context, 
            new KebabCaseEndpointNameFormatter(serviceSettings.ServiceName, false));
        cfg.UseMessageRetry(b =>
        {
            b.Interval(3, TimeSpan.FromSeconds(5));
        });
    });
});

builder.Services.AddHttpClient<OrderClient>(a =>
{
    a.BaseAddress = new Uri("https://localhost:7243");
})
.AddTransientHttpErrorPolicy(b => b.Or<TimeoutRejectedException>().WaitAndRetryAsync(
    5,
    c => TimeSpan.FromSeconds(Math.Pow(2, c))
    )).AddTransientHttpErrorPolicy(b => b.Or<TimeoutRejectedException>().CircuitBreakerAsync(
        3,
        TimeSpan.FromSeconds(15)
    )).AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(1));
    
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ProcessCenter", Version = "v1" });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
