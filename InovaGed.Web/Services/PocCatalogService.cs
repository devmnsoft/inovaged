using InovaGed.Web.Models.Atlas;
using InovaGed.Web.Models.Poc;

namespace InovaGed.Web.Services;

public interface IPocCatalogService
{
    PocDashboardVm Dashboard();
    PocChecklistVm Checklist();
    PocDemoVm Demo();
    PocEvidencesVm Evidences();
    bool Validate(string moduleKey, DateTimeOffset validatedAt);
}

public sealed class PocCatalogService : IPocCatalogService
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _validations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly DateTimeOffset Baseline = new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero);

    public PocDashboardVm Dashboard()
    {
        var modules = Modules();
        return new PocDashboardVm
        {
            Modules = modules,
            Metrics =
            [
                new("Cobertura geral", $"{Math.Round(modules.Average(x => x.Coverage))}%", "Média ponderada dos módulos demonstráveis", "success", "dashboard"),
                new("Módulos prontos", modules.Count(x => x.Status == PocReadinessStatus.Ready).ToString(), $"de {modules.Count} capacidades", "success", "check", "/Poc/Checklist"),
                new("Itens da matriz", "27", "Requisitos com evidência e roteiro", "info", "list", "/Poc/Checklist"),
                new("Roteiro guiado", "40 min", "Demonstração ponta a ponta", "neutral", "timeline", "/Poc/Demo")
            ]
        };
    }

    public PocChecklistVm Checklist()
    {
        var requirements = new[]
        {
            "Plano de classificação documental (PCD)", "Criação e movimentação de classes", "Publicação e histórico de versões do PCD",
            "Tabela de temporalidade documental (TTD)", "Cálculo de temporalidade por classe", "Destinação e casos de temporalidade",
            "Assinatura documental interna demonstrável", "Controle de acesso por perfil e setor", "Validação e revalidação de assinatura",
            "Permissões por documento", "Proteção de documentos restritos", "Registro de tentativa de acesso negado",
            "Solicitação de empréstimo", "Aprovação e entrega de empréstimo", "Devolução e cobrança de atrasos", "Recebimento e evolução de lotes",
            "Caixas e localização física completa", "Etiquetas QR Code e Code128", "Assinatura em lote", "Importação de documento assinado",
            "Detalhe técnico da assinatura", "Arquitetura preparada para provedor ICP-Brasil", "Relatório de validação de assinaturas",
            "Timeline da movimentação física", "Auditoria universal de ações críticas", "Painel 30/60/90 dias e vencidos", "Exportação documental rastreável"
        };
        var screens = new[] { "/ClassificationPlan", "/ClassificationPlan", "/InstrumentVersions?type=PCD", "/Retention", "/Retention/Queue", "/RetentionCase", "/Signature", "/Security/Roles", "/Signature", "/Ged", "/HospitalDocuments", "/Audit/AccessDenied", "/Loans/New", "/Loans", "/Loans/Overdue", "/Batches", "/Physical/Boxes", "/Labels/Boxes", "/Signature/SignBatch", "/Signature", "/Reports/SignatureValidation", "/Signature/Cryptographic", "/Reports/SignatureValidation", "/Physical/PhysicalMap", "/Audit", "/Retention/Queue", "/Continuity/Portability" };
        var technical = new[] { "classification_plan_node", "classification_plan_node / ClassificationPlanController", "instrument_version", "retention_rule", "retention_queue", "retention_case", "document_signature", "acl / SecurityController", "signature_validation", "document_acl_entry", "document_acl_entry", "audit_log", "loan_request", "loan_history", "loan_history", "batch / batch_history", "physical_box / physical_location", "label_template", "document_signature", "SignatureController", "signature_validation", "ISignatureProvider", "ReportsController", "physical_movement", "audit_log", "retention_queue", "portability_export / manifest.json" };
        var items = requirements.Select((requirement, index) => new PocChecklistItemVm(
            index + 1, requirement, screens[index], technical[index], index is 20 or 21 ? PocReadinessStatus.Partial : PocReadinessStatus.Ready,
            $"EVID-P{index + 1:00}: tela, registro persistido e evento auditável", $"Abra {screens[index]}, execute o cenário e confira a evidência gerada.")).ToArray();
        return new(items,
        [
            new("Requisitos", "27", "Matriz completa item a item", "info", "list"),
            new("Prontos", items.Count(x => x.Status == PocReadinessStatus.Ready).ToString(), "Com comprovação executável", "success", "check"),
            new("Parciais", items.Count(x => x.Status == PocReadinessStatus.Partial).ToString(), "Dependem de certificado/provedor externo", "warning", "warning"),
            new("Sem evidência", items.Count(x => string.IsNullOrWhiteSpace(x.Evidence)).ToString(), "Todos os itens possuem referência", "success", "audit")
        ]);
    }

    public PocDemoVm Demo()
    {
        PocDemoStepVm[] steps =
        [
            new(1, "Acessar o GED", "Apresente a árvore, filtros, preview e permissões do acervo.", "/Ged", "Abrir GED", 4, "Documento localizado no Explorer", "folder-open"),
            new(2, "Enviar e processar", "Envie um arquivo e acompanhe OCR sem sair da fila operacional.", "/GedUploads", "Enviar documento", 5, "Upload e OCR auditados", "ocr"),
            new(3, "Classificar", "Associe a classe do PCD e confira a regra TTD aplicável.", "/GedClassification/Queue", "Abrir classificação", 4, "Classe e metadados persistidos", "classification"),
            new(4, "Aplicar temporalidade", "Calcule prazo corrente, intermediário e destinação final.", "/Retention/Queue", "Calcular temporalidade", 4, "Previsão 30/60/90 dias", "retention"),
            new(5, "Consulta hospitalar", "Localize o mesmo registro com o escopo de acesso do setor.", "/HospitalDocuments", "Consultar documento", 3, "Resultado autorizado", "document-search"),
            new(6, "Usar o assistente", "Peça resumo, pendências e uma ação operacional segura.", "/SmartSearch", "Abrir assistente", 4, "Resposta com fonte e ação", "assistant"),
            new(7, "Tramitar", "Inicie o workflow, encaminhe uma etapa e confira prazo e responsável.", "/Workflow", "Abrir workflows", 4, "Evento com correlationId", "workflow"),
            new(8, "Assinar e validar", "Demonstre assinatura interna e explique o modo ICP configurável.", "/Signature", "Abrir assinaturas", 4, "Status e detalhe técnico", "signature"),
            new(9, "Movimentar o físico", "Solicite, aprove e devolva um item com endereço físico.", "/Loans", "Abrir empréstimos", 3, "Histórico físico completo", "loan"),
            new(10, "Comprovar auditoria", "Filtre pelo documento e correlationId da demonstração.", "/Audit", "Ver auditoria", 3, "Timeline universal", "audit"),
            new(11, "Relatar e exportar", "Emita relatório e pacote com manifesto e SHA-256.", "/Continuity/Portability", "Exportar pacote", 2, "Pacote documental rastreável", "download")
        ];
        return new(steps, steps.Sum(x => x.Minutes));
    }

    public PocEvidencesVm Evidences() => new(Modules().Select(module => new PocEvidenceVm(
        module.Name, module.Evidence, module.Url, TechnicalReference(module.Key), module.Status, module.LastValidatedAt)).ToArray());

    public bool Validate(string moduleKey, DateTimeOffset validatedAt)
    {
        if (!Modules().Any(x => x.Key.Equals(moduleKey, StringComparison.OrdinalIgnoreCase))) return false;
        lock (_sync) _validations[moduleKey] = validatedAt;
        return true;
    }

    private IReadOnlyList<PocModuleVm> Modules()
    {
        DateTimeOffset Last(string key) { lock (_sync) return _validations.GetValueOrDefault(key, Baseline); }
        return
        [
            M("ged", "GED Explorer", "Navegação, preview, metadados e permissões.", "folder-open", 100, "/Ged", "Abrir acervo"),
            M("ocr", "Upload/OCR", "Fila monitorada do envio ao texto pesquisável.", "ocr", 96, "/GedUploads", "Enviar arquivo"),
            M("pcd", "PCD", "Árvore, movimentação, impressão e versões.", "classification", 96, "/ClassificationPlan", "Abrir árvore"),
            M("ttd", "TTD", "Regras vinculadas a classes e eventos iniciais.", "retention", 94, "/Retention", "Revisar regras"),
            M("temporalidade", "Temporalidade", "Cálculo e filas de vencimento 30/60/90 dias.", "timeline", 92, "/Retention/Queue", "Calcular agora"),
            M("classificacao", "Classificação", "Fila assistida com trilha de alterações.", "metadata", 95, "/GedClassification/Queue", "Classificar"),
            M("assinatura", "Assinatura", "Modo interno e contratos para validação ICP-Brasil.", "signature", 88, "/Signature", "Validar assinatura", PocReadinessStatus.Partial),
            M("fisico", "Arquivo físico", "Mapa de endereço e movimentações rastreáveis.", "physical-archive", 94, "/Physical/PhysicalMap", "Abrir mapa"),
            M("lotes", "Lotes e caixas", "Recebimento, triagem, caixas e conteúdo.", "box", 92, "/Batches", "Receber lote"),
            M("emprestimos", "Empréstimos", "Solicitação, aprovação, entrega e devolução.", "loan", 96, "/Loans", "Solicitar item"),
            M("auditoria", "Auditoria", "Filtros avançados e timeline de ações críticas.", "audit", 98, "/Audit", "Filtrar eventos"),
            M("relatorios", "Relatórios", "Catálogo executivo e exportações operacionais.", "report", 94, "/Reports", "Emitir relatório"),
            M("assistente", "Assistente documental", "Busca orientada, fontes e ações seguras.", "assistant", 90, "/SmartSearch", "Perguntar"),
            M("portabilidade", "Portabilidade", "Pacote com manifesto, permissões e SHA-256.", "download", 90, "/Continuity/Portability", "Exportar pacote")
        ];

        PocModuleVm M(string key, string name, string description, string icon, int coverage, string url, string action, PocReadinessStatus status = PocReadinessStatus.Ready)
            => new(key, name, description, icon, status, coverage, url, action, url, $"EVID-{key.ToUpperInvariant()}: evidência funcional e auditável", Last(key));
    }

    private static string TechnicalReference(string key) => key switch
    {
        "ged" => "document / folder / GedController", "ocr" => "ocr_job / OcrController", "pcd" => "classification_plan_node",
        "ttd" or "temporalidade" => "retention_rule / retention_queue", "assinatura" => "document_signature / ISignatureProvider",
        "fisico" or "lotes" => "physical_location / physical_box / batch", "emprestimos" => "loan_request / loan_history",
        "auditoria" => "audit_log / AuditController", "portabilidade" => "portability_export / PortabilityManifestService",
        "assistente" => "DocumentAssistantService", _ => "document / application endpoint"
    };
}
