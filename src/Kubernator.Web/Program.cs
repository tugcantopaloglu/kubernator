using Kubernator.Core.DependencyInjection;
using Kubernator.Runtime.DependencyInjection;
using Kubernator.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddKubernatorCore();
builder.Services.AddKubernatorRuntime();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5050);
});

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
