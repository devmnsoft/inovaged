using InovaGed.Application.Administration;
using Xunit;

namespace InovaGed.Application.Tests.Administration;

public class CpfProtectionTests
{
    [Fact] public void ValidCpfIsNormalizedAndMasked(){ var cpf="529.982.247-25"; Assert.True(CpfProtection.IsValid(cpf)); Assert.Equal("52998224725", CpfProtection.Normalize(cpf)); Assert.Equal("***.***.***-25", CpfProtection.Mask(cpf)); }
    [Fact] public void InvalidCpfIsRejected(){ Assert.False(CpfProtection.IsValid("111.111.111-11")); }
    [Fact] public void SearchHashIsDeterministicAndKeyed(){ var a=CpfProtection.SearchHash("52998224725","secret-key"); var b=CpfProtection.SearchHash("529.982.247-25","secret-key"); Assert.Equal(a,b); Assert.NotEqual(CpfProtection.Normalize("52998224725"),a); }
}
