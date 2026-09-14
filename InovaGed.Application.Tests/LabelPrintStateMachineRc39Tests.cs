using InovaGed.Application.Labels.Printing;

namespace InovaGed.Application.Tests;

public sealed class LabelPrintStateMachineRc39Tests
{
    private readonly LabelPrintStateMachine machine = new();

    [Fact] public void pending_can_preview() => Assert.True(machine.CanTransition(LabelPrintJobStatus.Pending, LabelPrintJobStatus.Previewed));
    [Fact] public void previewed_can_ready() => Assert.True(machine.CanTransition(LabelPrintJobStatus.Previewed, LabelPrintJobStatus.ReadyToPrint));
    [Fact] public void ready_can_generate_artifact() => Assert.True(machine.CanTransition(LabelPrintJobStatus.ReadyToPrint, LabelPrintJobStatus.PdfGenerated));
    [Fact] public void artifact_can_print() => Assert.True(machine.CanMarkPrinted(LabelPrintJobStatus.PdfGenerated));
    [Fact] public void printed_is_terminal() => Assert.True(machine.IsTerminal(LabelPrintJobStatus.Printed));
    [Fact] public void cancelled_is_terminal() => Assert.True(machine.IsTerminal(LabelPrintJobStatus.Cancelled));
    [Fact] public void invalid_transition_is_rejected() => Assert.Throws<InvalidOperationException>(() => machine.EnsureTransition(LabelPrintJobStatus.Pending, LabelPrintJobStatus.Printed));
}
