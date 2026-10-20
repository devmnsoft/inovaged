using InovaGed.Application.ArtificialIntelligence;

namespace InovaGed.Application.Tests.ArtificialIntelligence;

public sealed class FreeAssistPolicyTests
{
    [Fact]
    public void Search_terms_drop_invented_numbers_and_duplicates()
    {
        var terms = FreeAssistPolicy.AcceptSearchTerms("tomografia", ["TC", "tomografia", "laudo", "123456", "ok!"]);

        Assert.Equal(["TC", "laudo"], terms);
    }

    [Fact]
    public void Ocr_rewrite_rejects_a_text_that_changed_too_much()
    {
        Assert.Null(FreeAssistPolicy.AcceptOcrRewrite("prontuario do paciente", "texto completamente diferente e longo demais para ser correcao"));
        Assert.Equal("prontuário do paciente", FreeAssistPolicy.AcceptOcrRewrite("prontuario do paciente", "prontuário do paciente"));
    }
}
