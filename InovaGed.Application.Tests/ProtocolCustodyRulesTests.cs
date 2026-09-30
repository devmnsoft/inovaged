using InovaGed.Application.Ged.Protocols;
using Xunit;

namespace InovaGed.Application.Tests;

public sealed class ProtocolCustodyRulesTests
{
    private static readonly Guid User = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SectorA = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid SectorB = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public void Repeated_forward_to_same_destination_is_idempotent()
    {
        Assert.True(ProtocolCustodyRules.IsSameDestinationRepeat(ProtocolCustodyRules.Waiting, SectorA, SectorA));
        Assert.Null(ProtocolCustodyRules.SecondForwardBlock(ProtocolCustodyRules.Waiting, SectorA, SectorA));
    }

    [Fact]
    public void Second_incompatible_forward_is_blocked()
    {
        var message = ProtocolCustodyRules.SecondForwardBlock(ProtocolCustodyRules.Waiting, SectorA, SectorB);
        Assert.Contains("movimentação ativa", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Receive_requires_the_identified_pending_movement()
    {
        Assert.Equal(ReceiveVerdict.Applied, ProtocolCustodyRules.DecideReceive(ProtocolCustodyRules.Waiting, null, User));
        Assert.Equal(ReceiveVerdict.Idempotent, ProtocolCustodyRules.DecideReceive(ProtocolCustodyRules.Received, User, User));
        Assert.Equal(ReceiveVerdict.Conflict, ProtocolCustodyRules.DecideReceive(ProtocolCustodyRules.Received, Other, User));
        Assert.Equal(ReceiveVerdict.Invalid, ProtocolCustodyRules.DecideReceive(ProtocolCustodyRules.Reversed, null, User));
    }

    [Fact]
    public void Reverse_before_receipt_is_allowed_and_after_receipt_points_to_return()
    {
        Assert.Null(ProtocolCustodyRules.ReverseBlock(ProtocolCustodyRules.Waiting));
        Assert.Contains("devolução", ProtocolCustodyRules.ReverseBlock(ProtocolCustodyRules.Received)!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("devolução", ProtocolCustodyRules.ReverseBlock(ProtocolCustodyRules.ReturnConfirmed)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Adjustment_return_is_not_a_physical_return()
    {
        Assert.True(ProtocolProcessTransitions.Can("IN_REVIEW", "RETURNED_FOR_ADJUSTMENT"));
        Assert.False(ProtocolProcessTransitions.Can("RETURNED_FOR_ADJUSTMENT", "FINISHED"));
        Assert.False(string.Equals("RETURNED_FOR_ADJUSTMENT", ProtocolCustodyRules.ReturnPending, StringComparison.Ordinal));
    }

    [Fact]
    public void Closed_process_has_no_ordinary_transition()
    {
        Assert.True(ProtocolCustodyRules.IsClosed("FINALIZADO"));
        Assert.True(ProtocolProcessTransitions.CanReopen("FINISHED"));
        Assert.False(ProtocolProcessTransitions.Can("FINISHED", "IN_REVIEW"));
        Assert.False(ProtocolProcessTransitions.Can("REJECTED", "APPROVED"));
    }

    [Fact]
    public void User_without_grant_cannot_act_and_admin_can()
    {
        Assert.False(ProtocolCustodyRules.CanAct(false, false, true));
        Assert.False(ProtocolCustodyRules.CanAct(false, true, false));
        Assert.True(ProtocolCustodyRules.CanAct(true, false, false));
    }

    [Theory]
    [InlineData(0, 0, "NONE", null)]
    [InlineData(0, 3, "BLOCKED", null)]
    [InlineData(2, 1, "PARTIAL", "PARTIALLY_EXECUTED")]
    [InlineData(2, 0, "COMPLETE", "EXECUTED")]
    public void Retention_outcome_does_not_turn_approval_into_completion(int executed, int blocked, string outcome, string? status)
    {
        Assert.Equal(outcome, ProtocolCustodyRules.RetentionOutcome(executed, blocked));
        Assert.Equal(status, ProtocolCustodyRules.RetentionCaseStatus(outcome));
    }

    [Fact]
    public void Attachment_failure_is_not_reported_as_full_success()
    {
        Assert.Contains("não foi concluída", ProtocolCustodyRules.AttachmentSummary(2, 1, 1), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não foi concluída", ProtocolCustodyRules.AttachmentSummary(1, 0, 1), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Arquivos armazenados.", ProtocolCustodyRules.AttachmentSummary(1, 1, 0));
    }

    [Fact]
    public void Portuguese_labels_cover_institutional_and_request_movements()
    {
        Assert.Equal("A receber", ProtocolCustodyRules.Label("AGUARDANDO_RECEBIMENTO"));
        Assert.Equal("A receber", ProtocolCustodyRules.Label("PENDING_RECEIPT"));
        Assert.Equal("Devolução pendente", ProtocolCustodyRules.Label("RETURN_PENDING"));
        Assert.NotEqual(ProtocolCustodyRules.Label("RETURNED_FOR_ADJUSTMENT"), ProtocolCustodyRules.Label("DEVOLUCAO_PENDENTE"));
    }
}
