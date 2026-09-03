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
        Require(partial.Contains("StartsWith(\"data:image/\"", StringComparison.Ordinal), "_PrintLogo deve aceitar somente Data URI de imagem.");

        foreach (var view in new[] { "LocDeskFolderLabel.cshtml", "LocDeskBoxLabel.cshtml", "LocDeskFolderHolLabel.cshtml" })
        {
            var text = Read($"InovaGed.Web/Views/Labels/{view}");
            Require(text.Contains("_LocDeskLabel", StringComparison.Ordinal) || text.Contains("Branding/_PrintLogo", StringComparison.Ordinal), $"{view} não usa o fluxo compartilhado de logo.");
            Require(text.Contains("data-label-print-now", StringComparison.Ordinal), $"{view} não possui o botão de impressão.");
            Require(text.Contains("window.print()", StringComparison.Ordinal) || text.Contains("labels-print-page.js", StringComparison.Ordinal), $"{view} não aciona window.print.");
        }
        var labelsViews = Directory.GetFiles(Path.Combine(root, "InovaGed.Web/Views/Labels"), "*.cshtml", SearchOption.AllDirectories);
        foreach (var view in labelsViews)
        {
            var text = File.ReadAllText(view);
            Require(!text.Contains("new LocDeskLabelRenderModel", StringComparison.Ordinal), $"{Path.GetFileName(view)} instancia LocDeskLabelRenderModel no Razor.");
            Require(!text.Contains("new InovaGed.Web.Models.Labels.LocDeskLabelRenderModel", StringComparison.Ordinal), $"{Path.GetFileName(view)} instancia LocDeskLabelRenderModel qualificado no Razor.");
        }
        var renderModel = Read("InovaGed.Web/Models/Labels/LocDeskLabelRenderModel.cs");
        Require(!renderModel.Contains("required ", StringComparison.Ordinal), "LocDeskLabelRenderModel ainda depende de required members.");

        var wizard = Read("InovaGed.Web/Views/Labels/PrintWizard.cshtml");
        var formStart = wizard.IndexOf("<form method=\"post\"", StringComparison.Ordinal);
        var formEnd = formStart >= 0 ? wizard.IndexOf("</form>", formStart, StringComparison.Ordinal) : -1;
        var selected = wizard.IndexOf("asp-for=\"SelectedLogoAssetId\"", StringComparison.Ordinal);
        Require(formStart >= 0 && selected > formStart && selected < formEnd, "SelectedLogoAssetId não está dentro do formulário principal.");
        Require(wizard.Contains("id=\"label-print-form\"", StringComparison.Ordinal), "PrintWizard não identifica o formulário principal.");
        foreach (var action in new[] { "Preview", "PrintPreview", "Print" })
        {
            Require(wizard.Contains($"type=\"submit\" formaction=\"@Url.Action(\"{action}\", \"Labels\")\"", StringComparison.Ordinal),
                $"Botão {action} não é submit com formaction explícito.");
        }
        var controller = Read("InovaGed.Web/Controller/LabelsController.cs");
        Require(controller.Contains("Task<IActionResult> PrintPreview(LabelPrintWizardInputModel input", StringComparison.Ordinal), "POST PrintPreview do PrintWizard não existe.");
        Require(controller.Contains("BuildLabelRenderModelAsync(input", StringComparison.Ordinal), "As ações não usam o construtor único de renderização.");
        Require(!controller.Contains("return RedirectToAction(nameof(LocDeskBox)", StringComparison.Ordinal), "O fluxo LocDesk ainda perde o POST em redirecionamento.");
        Require(controller.Contains("SelectedLogoAssetIdPresent", StringComparison.Ordinal), "Log seguro da seleção de logo não existe.");
        Require(controller.Contains("new DynamicParameters()", StringComparison.Ordinal), "History deve usar DynamicParameters tipados.");
        Require(!controller.Contains("@startDate is null", StringComparison.OrdinalIgnoreCase), "History ainda usa parâmetro nulo sem tipo no SQL.");
        var logoController = Read("InovaGed.Web/Controller/LogoLayoutController.cs");
        var logoView = Read("InovaGed.Web/Views/LogoLayout/Edit.cshtml");
        Require(logoController.Contains("SaveGetFallback", StringComparison.Ordinal), "GET acidental de Save não possui fallback seguro.");
        Require(logoView.Contains("method=\"post\"", StringComparison.Ordinal), "LogoLayout Save não usa POST.");
        Require(logoView.Contains("AntiForgeryToken", StringComparison.Ordinal), "LogoLayout Save não envia antiforgery.");
        Require(!logoView.Contains("href=\"/Labels/LogoLayout/@Model.TemplateCode/Save", StringComparison.Ordinal), "LogoLayout Save não pode ser link GET.");
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
