using System;
using System.Collections.Generic;
using System.Text;
using Placebo.Functions.Contexts;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

[assembly: FunctionsStartup(typeof(Placebo.Functions.Startup))]
namespace Placebo.Functions
{
    public class Startup : FunctionsStartup
    {

        public override void Configure(IFunctionsHostBuilder builder)
        {
            var localRoot = Environment.GetEnvironmentVariable("AzureWebJobsScriptRoot");
            var azureRoot = $"{Environment.GetEnvironmentVariable("HOME")}/site/wwwroot";

            var actualRoot = localRoot ?? azureRoot;

            var configBuilder = new ConfigurationBuilder()
                .SetBasePath(actualRoot)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddJsonFile("settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();
            IConfiguration configuration = configBuilder.Build();
            builder.Services.AddSingleton(configuration);
            builder.Services.AddSingleton(configuration.GetSection("ProcessingContext").Get<ProcessingContext>());
            builder.Services.AddSingleton(configuration.GetSection("RoutingContext").Get<RoutingContext>());
            builder.Services.AddSingleton(configuration.GetSection("RecognizingContext").Get<RecognizingContext>()); 
            builder.Services.AddSingleton(configuration.GetSection("TrainingContext").Get<TrainingContext>());
            builder.Services.AddSingleton(configuration.GetSection("CosmosPersistorContext").Get<CosmosPersistorContext>());
            builder.Services.AddScoped<ServiceBusSender>();
        }
    }
}
