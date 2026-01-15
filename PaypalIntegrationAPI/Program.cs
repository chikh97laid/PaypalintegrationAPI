using Microsoft.EntityFrameworkCore;
using PayPalIntegrationAPI.Client;
using Polly;

var builder = WebApplication.CreateBuilder(args);

// Configure EF Core + Npgsql
builder.Services.AddDbContext<PayPalIntegrationAPI.Data.AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services to the container.

builder.Services.AddHttpClient("PayPal", client =>
{
    
    client.Timeout = TimeSpan.FromSeconds(10);

}).AddTransientHttpErrorPolicy(policy =>
    policy.WaitAndRetryAsync(
        retryCount: 3,
        sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))
    ))
    .AddTransientHttpErrorPolicy(policy =>
        policy.CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30)
        )
);

builder.Services.AddSingleton<IPayPalClient, PayPalClient>();

// builder.Services.AddDbContext<AppDbContext>(options =>
// options.UseNpgsql(builder.Configuration.GetConnectionString("myConnection")));

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseStaticFiles();
app.MapFallbackToFile("checkout.html");
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
