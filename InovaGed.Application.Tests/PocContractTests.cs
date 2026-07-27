using System.Text.Json;

namespace InovaGed.Application.Tests;

internal static class PocMatrixContract
{
    internal static JsonElement[] Load()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "poc", "matriz-poc-1-27.json")));
        return document.RootElement.GetProperty("itens").EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    internal static void AssertItems(params int[] expected)
    {
        var items = Load();
        foreach (var number in expected)
        {
            var item = Assert.Single(items, item => item.GetProperty("item").GetInt32() == number);
            Assert.DoesNotContain("Requisito PoC", item.GetProperty("requisito").GetString());
            foreach (var field in new[] { "classe", "modulo", "controller", "view", "endpoint", "servico", "tabelas", "policy", "teste", "passo_demonstracao", "resultado_esperado", "evidencia_auditoria", "status_real", "pendencia" })
                Assert.False(string.IsNullOrWhiteSpace(item.GetProperty(field).GetString()), $"PoC {number}: {field} is required");
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InovaGed.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("InovaGed.sln not found");
    }
}

[Trait("Category", "PoCContract")]
public sealed class PocPhase1InstrumentsTests { [Fact] public void InstrumentsHaveExecutableContracts() => PocMatrixContract.AssertItems(1, 2, 3, 4); }
[Trait("Category", "PoCContract")]
public sealed class PocPhase2RetentionTests { [Fact] public void RetentionHasExecutableContracts() => PocMatrixContract.AssertItems(5, 6, 26); }
[Trait("Category", "PoCContract")]
public sealed class PocPhase3SecurityTests { [Fact] public void SecurityHasExecutableContracts() => PocMatrixContract.AssertItems(8, 10, 11, 12, 27); }
[Trait("Category", "PoCContract")]
public sealed class PocPhase4SigningTests { [Fact] public void SigningHasExecutableContracts() => PocMatrixContract.AssertItems(7, 9, 19, 20, 21, 22, 23); }
[Trait("Category", "PoCContract")]
public sealed class PocPhase5PhysicalArchiveTests { [Fact] public void PhysicalArchiveHasExecutableContracts() => PocMatrixContract.AssertItems(16, 17, 18, 24); }
[Trait("Category", "PoCContract")]
public sealed class PocPhase6LoansTests { [Fact] public void LoansHaveExecutableContracts() => PocMatrixContract.AssertItems(13, 14, 15); }
[Trait("Category", "PoCContract")]
public sealed class PocPhase7AuditTests { [Fact] public void AuditHasExecutableContracts() => PocMatrixContract.AssertItems(25, 27); }
[Trait("Category", "PoCContract")]
public sealed class PocMatrixValidationTests { [Fact] public void MatrixContainsExactlyItemsOneThroughTwentySeven() => Assert.Equal(Enumerable.Range(1, 27), PocMatrixContract.Load().Select(item => item.GetProperty("item").GetInt32()).Order()); }
