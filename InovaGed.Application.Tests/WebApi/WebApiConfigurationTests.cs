using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace InovaGed.Application.Tests.WebApi;

public sealed class WebApiConfigurationTests
{
    [Fact]
    public void Program_ThrowsClearErrorWhenConnectionStringIsMissing()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.Sources.Clear();
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Jwt:Key"] = "0123456789abcdef0123456789abcdef",
                        ["Jwt:Issuer"] = "tests",
                        ["Jwt:Audience"] = "tests"
                    });
                });
                builder.UseEnvironment(Environments.Production);
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.Server);
        Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
