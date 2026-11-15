using RitmoGuatemalaMVC.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<ReadOnlySqlConnectionFactory>();
builder.Services.AddScoped<ClientesDashboardRepository>();
builder.Services.AddHostedService<DatabaseConnectionVerifier>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Clientes}/{action=Index}/{id?}");

app.Run();
