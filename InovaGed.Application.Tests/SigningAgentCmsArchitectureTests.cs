using Xunit;

public sealed class SigningAgentCmsArchitectureTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void AgentProjectUsesWebSdkAndLoopbackOnly()
    {
        var csproj = File.ReadAllText(Path.Combine(Root, "InovaGed.Signing.Agent/InovaGed.Signing.Agent.csproj"));
        var program = File.ReadAllText(Path.Combine(Root, "InovaGed.Signing.Agent/Program.cs"));
        Assert.Contains("Microsoft.NET.Sdk.Web", csproj);
        Assert.Contains("IPAddress.IsLoopback", program);
        Assert.DoesNotContain("AllowAnyOrigin", program);
        Assert.Contains("SignedCms", program);
    }

    [Fact]
    public void OfficialMigrationPreservesPreviousSignaturesAndAddsCmsEvidence()
    {
        var migration = File.ReadAllText(Path.Combine(Root, "database/migrations/2026_07_signature_cms_agent.sql"));
        Assert.Contains("signature_validation_check", migration);
        Assert.Contains("CMS_DETACHED", migration);
        Assert.Contains("content_download_token_hash", migration);
        Assert.DoesNotContain("DROP TABLE", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP COLUMN", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET reg_status = 'I'", migration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DigitalSignatureModuleIsRegisteredInSharedComposition()
    {
        var source = File.ReadAllText(Path.Combine(Root, "InovaGed.Infrastructure/DependencyInjection.cs"));
        Assert.Contains("AddDigitalSignatureModule", source);
        Assert.Contains("ValidateOnStart", source);
        Assert.Contains("NotConfiguredSignatureValidationService", source);
        Assert.Contains("CmsDetachedSignatureValidationService", source);
    }
}

public sealed class SigningAgentCmsEndToEndGuards
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void AgentCmsModeRegistersPostgresRepositoriesInsteadOfNoopDefaults()
    {
        var source = File.ReadAllText(Path.Combine(Root, "InovaGed.Infrastructure/DependencyInjection.cs"));
        Assert.Contains("services.AddScoped<ISigningSessionRepository, PostgresSigningSessionRepository>()", source);
        Assert.Contains("services.AddScoped<ISignatureEvidenceRepository, PostgresSignatureEvidenceRepository>()", source);
        Assert.Contains("else\n        {\n            services.AddScoped<ISigningSessionRepository, NoopSigningSessionRepository>()", source);
    }

    [Fact]
    public void PublicSigningControllerDoesNotBindInternalPrepareCommand()
    {
        var source = File.ReadAllText(Path.Combine(Root, "InovaGed.Web/Controller/SigningApiController.cs"));
        Assert.Contains("CreateSigningSessionRequest request", source);
        Assert.DoesNotContain("Create([FromBody] PrepareSignatureCommand", source);
        Assert.Contains("CompleteSigningSessionRequest request", source);
    }

    [Fact]
    public void CertificateIdentityUsesHmacInsteadOfPlainSha256CpfSearch()
    {
        var source = File.ReadAllText(Path.Combine(Root, "InovaGed.Infrastructure/Signatures/CmsDetachedSignatureServices.cs"));
        Assert.Contains("HMACSHA256", source);
        Assert.DoesNotContain("SHA256.HashData(Encoding.UTF8.GetBytes(cpf))", source);
    }
}
