using DaxDiff.Application;
using DaxDiff.Web;
using DaxDiff.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Power BI calls go straight from the browser to api.powerbi.com; there is no backend.
builder.Services.AddSingleton(_ => new HttpClient());
builder.Services.AddSingleton<BrowserFiles>();
builder.Services.AddSingleton(sp => new WebSettings(builder.Configuration, sp.GetRequiredService<BrowserFiles>(), builder.HostEnvironment.BaseAddress));
builder.Services.AddSingleton(sp => sp.GetRequiredService<WebSettings>().Auth);
builder.Services.AddSingleton<ComparisonRunner>();
builder.Services.AddSingleton<ProjectState>();

var host = builder.Build();
await host.Services.GetRequiredService<WebSettings>().LoadAsync();
await host.RunAsync();
