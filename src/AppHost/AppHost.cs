var builder = DistributedApplication.CreateBuilder(args);

// Match launchSettings / Angular schoolAiSupportUrl (avoid random Aspire ports like 7122).
builder.AddProject<Projects.WebApi>("webapi")
    .WithHttpsEndpoint(port: 7127, isProxied: false)
    .WithHttpEndpoint(port: 5043, isProxied: false)
    .WithUrls(context =>
    {
        var baseUrl = context.Urls.FirstOrDefault();
        if (baseUrl is not null)
        {
            context.Urls.Add(new()
            {
                Url = baseUrl.Url.TrimEnd('/') + "/devui",
                DisplayText = "DevUI Visual App"
            });
        }
    });

builder.Build().Run();