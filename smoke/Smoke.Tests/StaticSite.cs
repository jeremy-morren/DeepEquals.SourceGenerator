// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeepEquals.Smoke.Tests;

/// <summary>
/// Serves a published Blazor application over HTTP for the duration of a test. A browser will not
/// load Blazor from a file:// URL, and an in-process host keeps the test free of an external server
/// tool.
/// </summary>
public sealed class StaticSite : IAsyncDisposable
{
    private readonly WebApplication _app;

    private StaticSite(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    /// <summary>The origin the site is listening on, with no trailing slash.</summary>
    public string Url { get; }

    public static async Task<StaticSite> StartAsync(string webRoot)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { WebRootPath = webRoot });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        WebApplication app = builder.Build();

        // Blazor's payload includes extensions the default provider has never heard of, and the
        // runtime refuses anything served as the wrong type.
        FileExtensionContentTypeProvider types = new();
        types.Mappings[".wasm"] = "application/wasm";
        types.Mappings[".blat"] = "application/octet-stream";
        types.Mappings[".dat"] = "application/octet-stream";
        types.Mappings[".pdb"] = "application/octet-stream";

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = types,
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
        });

        await app.StartAsync();

        string url = app.Urls.GetEnumerator() is var _ && app.Urls.Count > 0
            ? System.Linq.Enumerable.First(app.Urls)
            : throw new InvalidOperationException("the static site did not report a URL");

        return new StaticSite(app, url.TrimEnd('/'));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
