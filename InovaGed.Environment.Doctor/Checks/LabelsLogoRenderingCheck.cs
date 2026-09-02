namespace InovaGed.Environment.Doctor.Checks;

public static class LabelsLogoRenderingCheck
{
    public static int Run(string root, TextWriter output, TextWriter error)
    {
        var failures = new List<string>();
        string Read(string relative) => File.ReadAllText(Path.Combine(root, relative));
        void Require(bool condition, string message) { if (!condition) failures.Add(message); }

        const string partialPath = "InovaGed.Web/Views/Shared/Branding/_PrintLogo.cshtml";
        var partial = Read(partialPath);
        Require(partial.Contains("PrintLogoViewModel", StringComparison.Ordinal), "_PrintLogo deve receber PrintLogoViewModel.");
        Require(partial.Contains("src=\"@Model.PrintImageSource\"", StringComparison.Ordinal), "_PrintLogo deve usar exclusivamente PrintImageSource.");
        Require(!partial.Contains("src=\"\"", StringComparison.Ordinal), "_PrintLogo contém src vazio.");
        Require(partial.Contains("Model.ImageLoaded", StringComparison.Ordinal), "_PrintLogo deve impedir imagem não carregada.");

        foreach (var view in new[] { "LocDeskFolderLabel.cshtml", "LocDeskBoxLabel.cshtml", "LocDeskFolderHolLabel.cshtml" })
        {
            var text = Read($"InovaGed.Web/Views/Labels/{view}");
            Require(text.Contains("_LocDeskLabel", StringComparison.Ordinal) || text.Contains("Branding/_PrintLogo", StringComparison.Ordinal), $"{view} não usa o fluxo compartilhado de logo.");
            Require(text.Contains("data-label-print-now", StringComparison.Ordinal), $"{view} não possui o botão de impressão.");
            Require(text.Contains("window.print()", StringComparison.Ordinal) || text.Contains("labels-print-page.js", StringComparison.Ordinal), $"{view} não aciona window.print.");
        }

        var wizard = Read("InovaGed.Web/Views/Labels/PrintWizard.cshtml");
        var formStart = wizard.IndexOf("<form method=\"post\"", StringComparison.Ordinal);
        var formEnd = formStart >= 0 ? wizard.IndexOf("</form>", formStart, StringComparison.Ordinal) : -1;
        var selected = wizard.IndexOf("asp-for=\"SelectedLogoAssetId\"", StringComparison.Ordinal);
        Require(formStart >= 0 && selected > formStart && selected < formEnd, "SelectedLogoAssetId não está dentro do formulário principal.");
        Require(File.Exists(Path.Combine(root, "InovaGed.Web/wwwroot/js/labels-print-page.js")), "labels-print-page.js não existe.");
        foreach (var field in new[] { "LogoWidthMm", "LogoHeightMm", "PreserveAspectRatio", "LogoFitMode", "LogoPosition", "LogoOffsetXmm", "LogoOffsetYmm" })
            Require(wizard.Contains($"asp-for=\"{field}\"", StringComparison.Ordinal), $"PrintWizard não envia {field}.");
        var locDeskPartial = Read("InovaGed.Web/Views/Shared/_LocDeskLogo.cshtml");
        Require(locDeskPartial.Contains("PrintLogoViewModel", StringComparison.Ordinal) && locDeskPartial.Contains("Branding/_PrintLogo", StringComparison.Ordinal), "_LocDeskLogo não encaminha PrintLogoViewModel.");

        foreach (var failure in failures) error.WriteLine($"[FALHA] {failure}");
        if (failures.Count != 0) return 2;
        output.WriteLine("[OK] Logo de etiquetas: Data URI obrigatória, partial segura, wizard e ação window.print verificados.");
        return 0;
    }
}
