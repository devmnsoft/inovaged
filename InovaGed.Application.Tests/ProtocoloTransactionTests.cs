using System.Reflection;
using InovaGed.Web.Models.Protocolo;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class ProtocoloTransactionTests
{
    [Fact]
    public void ProtocoloNovoVM_ExposesIdForIdempotency()
    {
        var vm = new ProtocoloNovoVM();
        var prop = typeof(ProtocoloNovoVM).GetProperty(nameof(ProtocoloNovoVM.Id));
        Assert.NotNull(prop);
        Assert.True(prop.PropertyType == typeof(Guid?));

        var guid = Guid.NewGuid();
        vm.Id = guid;
        Assert.Equal(guid, vm.Id);
    }

    [Fact]
    public void ProtocoloNovo_View_IncludesIdHiddenInput()
    {
        var root = GlobalJsonContractTests.Root("InovaGed.Web/Views/Protocolo/Novo.cshtml");
        if (File.Exists(root))
        {
            var content = File.ReadAllText(root);
            Assert.Contains("asp-for=\"Id\"", content);
            Assert.Contains("type=\"hidden\"", content);
        }
    }

    [Fact]
    public void ProtocoloController_Novo_SeparatesCommitFromPostCommitForwarding()
    {
        var root = GlobalJsonContractTests.Root("InovaGed.Web/Controller/ProtocoloController.cs");
        Assert.True(File.Exists(root));
        var content = File.ReadAllText(root);

        // Transaction commit occurs before creation ForwardAsync
        var commitIndex = content.IndexOf("tx.Commit();", StringComparison.Ordinal);
        var forwardIndex = content.IndexOf("\"Abertura do protocolo\"", StringComparison.Ordinal);
        Assert.True(commitIndex > 0, "tx.Commit() deve existir");
        Assert.True(forwardIndex > 0, "ForwardAsync com 'Abertura do protocolo' deve existir");
        Assert.True(commitIndex < forwardIndex, "tx.Commit() deve ser executado ANTES do ForwardAsync");

        // Rollback is guarded to only occur on uncommitted transaction
        Assert.Contains("if (!committed)", content);
        Assert.Contains("tx.Rollback();", content);

        // Forward failure does NOT report protocol creation as undone
        Assert.Contains("encaminhamento", content);
        Assert.Contains("pendente", content);
        Assert.Contains("Protocolo {num.Numero} criado", content);

        // Idempotent resume check on existing Id
        Assert.Contains("where tenant_id = @TenantId and id = @Id and reg_status = 'A'", content);
        Assert.Contains("já persistido; retomando sem duplicar", content);

        // Attachments stored in DB inside tx, preventing orphan files
        Assert.Contains("ged.protocolo_documento", content);
        Assert.Contains("arquivo_bytes", content);
    }
}
