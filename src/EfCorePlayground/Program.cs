using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EfCorePlayground;

// Enable legacy timestamp behavior so Npgsql accepts DateTime without explicit UTC kind.
// This avoids "timestamp with time zone literal cannot be generated for Unspecified DateTime" errors.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().RunAsync();
