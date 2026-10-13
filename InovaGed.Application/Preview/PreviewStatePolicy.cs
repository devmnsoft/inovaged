namespace InovaGed.Application.Preview;

public static class PreviewStatePolicy
{
    public static bool CanTransition(PreviewProcessingStatus? current, PreviewProcessingStatus next, out string impediment)
    {
        if (current is null || current == next)
        {
            impediment = string.Empty;
            return true;
        }

        var allowed = current switch
        {
            PreviewProcessingStatus.Pending => next is PreviewProcessingStatus.Processing or PreviewProcessingStatus.Error or PreviewProcessingStatus.Canceled,
            PreviewProcessingStatus.Processing => next is PreviewProcessingStatus.Ready or PreviewProcessingStatus.Error or PreviewProcessingStatus.Canceled or PreviewProcessingStatus.Pending,
            PreviewProcessingStatus.Ready or PreviewProcessingStatus.Error or PreviewProcessingStatus.Canceled => next is PreviewProcessingStatus.Pending or PreviewProcessingStatus.Processing,
            _ => false
        };

        impediment = allowed
            ? string.Empty
            : "A situação atual do preview não permite esta ação. Atualize o documento e tente novamente.";
        return allowed;
    }
}
