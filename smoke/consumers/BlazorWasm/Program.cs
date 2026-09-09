using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace DeepEquals.Smoke.BlazorWasm
{
    public static class Program
    {
        public static System.Threading.Tasks.Task Main(string[] args)
        {
            WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<Result>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");
            return builder.Build().RunAsync();
        }
    }
}
