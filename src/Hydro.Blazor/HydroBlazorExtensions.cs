#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Hydro.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Hydro.Blazor;

public static class HydroBlazorExtensions
{
    public static void AddHydroBlazor(this IServiceCollection services)
    {
        services.AddScoped<HydroBlazorContext>();
    }

    public static void MapHydroBlazorComponent(this IEndpointRouteBuilder app, Type componentType)
    {
        var componentName = componentType.Name;
        var options = app.ServiceProvider.GetRequiredService<HydroOptions>();
        
        app.MapPost($"{options.BasePath}/{componentName}/{{method?}}", async (
            [FromServices] IServiceProvider serviceProvider,
            [FromServices] HydroOptions hydroOptions,
            [FromServices] IAntiforgery antiforgery,
            [FromServices] ILogger<HydroBlazorComponent> logger,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext httpContext,
            string method
        ) =>
        {
            if (hydroOptions.AntiforgeryTokenEnabled)
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(httpContext);
                }
                catch (AntiforgeryValidationException exception)
                {
                    logger.LogWarning(exception, "Antiforgery token not valid");
                    var requestToken = antiforgery.GetTokens(httpContext).RequestToken;
                    httpContext.Response.Headers.Append(HydroConsts.ResponseHeaders.RefreshToken, requestToken);
                    return Results.BadRequest(new { token = requestToken });
                }
            }

            if (!httpContext.Request.HasFormContentType)
            {
                return Results.BadRequest("Hydro form doesn't contain form which is required");
            }

            var hydroData = await httpContext.Request.ReadFormAsync();
            
            var modelStr = hydroData["__hydro_model"].FirstOrDefault();
            var parametersStr = hydroData["__hydro_parameters"].FirstOrDefault("{}");
            
            Dictionary<string, object>? state = null;
            if (!string.IsNullOrEmpty(modelStr))
            {
                var persistentState = serviceProvider.GetRequiredService<IPersistentState>();
                var decompressed = persistentState.Decompress(modelStr);
                state = JsonConvert.DeserializeObject<Dictionary<string, object>>(decompressed, HydroComponent.JsonSerializerSettings);
            }
            
            var actionParams = JsonConvert.DeserializeObject<Dictionary<string, object>>(parametersStr, HydroComponent.JsonSerializerSettings);
            var componentIds = JsonConvert.DeserializeObject<string[]>(hydroData["__hydro_componentIds"].FirstOrDefault("[]"));
            
            var componentId = componentIds != null && componentIds.Length > 0 ? componentIds[0] : Guid.NewGuid().ToString("N");

            var formValues = new Dictionary<string, object>();
            foreach (var kvp in hydroData)
            {
                if (!kvp.Key.StartsWith("__hydro"))
                {
                    formValues[kvp.Key] = kvp.Value.Count == 1 ? (object)kvp.Value[0] : (object)kvp.Value.ToArray();
                }
            }

            var blazorParams = new Dictionary<string, object?>
            {
                { "HydroId", componentId },
                { "HydroName", componentName },
                { "HydroActionName", method },
                { "HydroActionParameters", actionParams },
                { "HydroState", state },
                { "HydroFormValues", formValues }
            };

            using var scope = serviceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;
            var hydroContext = scopedProvider.GetRequiredService<HydroBlazorContext>();

            await using var htmlRenderer = new HtmlRenderer(scopedProvider, loggerFactory);
            
            var htmlRoot = await htmlRenderer.Dispatcher.InvokeAsync(async () => 
            {
                var parameters = ParameterView.FromDictionary(blazorParams);
                var output = await htmlRenderer.RenderComponentAsync(componentType, parameters);
                return output.ToHtmlString();
            });
            
            var finalHtml = await WrapHtml(htmlRoot, componentId, componentName, componentType, hydroContext.ComponentInstance, scopedProvider);

            return Results.Content(finalHtml, MediaTypeNames.Text.Html);
        });
    }

    private static async Task<string> WrapHtml(string html, string componentId, string componentName, Type componentType, IComponent? componentInstance, IServiceProvider serviceProvider)
    {
        var htmlDocument = new HtmlDocument();
        htmlDocument.LoadHtml(html);
        
        var rootElement = htmlDocument.DocumentNode.ChildNodes.FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
        if (rootElement == null) return html;

        rootElement.SetAttributeValue("id", componentId);
        rootElement.SetAttributeValue("hydro-name", componentName);
        rootElement.SetAttributeValue("x-data", "hydro");
        rootElement.SetAttributeValue("key", componentId);

        var hydroAttribute = rootElement.SetAttributeValue("hydro", null);
        hydroAttribute.QuoteType = AttributeValueQuote.WithoutValue;

        var persistentState = serviceProvider.GetRequiredService<IPersistentState>();
        var serializedState = componentInstance != null 
            ? PropertyInjector.SerializeDeclaredProperties(componentType, componentInstance)
            : "{}";
        
        var compressed = persistentState.Compress(serializedState);
        
        var scriptNode = htmlDocument.CreateElement("script");
        scriptNode.SetAttributeValue("type", "text/hydro");
        scriptNode.SetAttributeValue("data-id", componentId);
        scriptNode.AppendChild(htmlDocument.CreateTextNode(compressed));
        rootElement.AppendChild(scriptNode);

        return await Task.FromResult(rootElement.OuterHtml);
    }
}
#endif
