using System.Net;
using System.Text.Json;
using Bielu.AspNetCore.AsyncApi.Attributes.Attributes;
using Bielu.AspNetCore.AsyncApi.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace Bielu.AspNetCore.AsyncApi.Tests.Integration;

/// <summary>
/// Regression tests for a bug where <c>NormalizeSchemaTypes</c> (in AsyncApiJsonSchemaService)
/// collapsed a nullable property's exported "type": ["string", "null"] down to a single
/// non-null type, silently dropping nullability from the generated schema.
/// </summary>
public class NullablePropertySchemaTests
{
    private const string TestDocumentName = "v1";

    [Fact]
    public async Task GetAsyncApiDocument_NullableProperty_IsNotRequiredAndKeepsNullType()
    {
        using var host = await Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddControllers();
                    services.AddRouting();
                    services.AddAsyncApi(TestDocumentName);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapAsyncApi();
                    });
                });
            })
            .StartAsync();

        var client = host.GetTestServer().CreateClient();

        var response = await client.GetAsync($"/asyncapi/{TestDocumentName}.json");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        var schemas = root.GetProperty("components").GetProperty("schemas");
        schemas.TryGetProperty("nullablePropertyTestMessage", out var schema).ShouldBeTrue();

        var properties = schema.GetProperty("properties");

        // Non-nullable property should have a plain "string" type.
        properties.GetProperty("requiredText").GetProperty("type").GetString().ShouldBe("string");

        // Nullable property should keep both "string" and "null" in its type.
        var nullableTextType = properties.GetProperty("nullableText").GetProperty("type");
        var nullableTextTypes = nullableTextType.ValueKind == JsonValueKind.Array
            ? nullableTextType.EnumerateArray().Select(t => t.GetString()).ToArray()
            : [nullableTextType.GetString()];
        nullableTextTypes.ShouldContain("string");
        nullableTextTypes.ShouldContain("null");
    }
}

[AsyncApi]
[Channel("nullablePropertyTestBus")]
public class NullablePropertyTestBus
{
    [PublishOperation(typeof(NullablePropertyTestMessage))]
    public void ProcessMessage(NullablePropertyTestMessage message) { }
}

public class NullablePropertyTestMessage
{
    public string RequiredText { get; set; } = string.Empty;

    public string? NullableText { get; set; }
}
