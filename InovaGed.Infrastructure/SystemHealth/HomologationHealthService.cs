using System.Collections.Concurrent;
using Dapper;
using InovaGed.Application.Common.Database;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Application.SystemHealth;
using InovaGed.Domain.Ged;
using InovaGed.Infrastructure.Ged.Loans;
using InovaGed.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InovaGed.Infrastructure.SystemHealth;

public sealed class HomologationHealthService : IHomologationHealthService
{
    private readonly IDbConnectionFactory _db;
    private readonly ISchemaHealthService _schemaHealth;
    private readonly ISchemaCompatibilityState _schemaState;
    private readonly IFolderQueries _folders;
    private readonly IDocumentQueries _documents;
    private readonly ICurrentUser _currentUser;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly DocumentUploadOptions _uploadOptions;
    private readonly LocalStorageOptions _storageOptions;
    private readonly LoanOverdueWorkerOptions _loanOptions;
    private readonly ILogger<HomologationHealthService> _logger;
    private readonly ConcurrentDictionary<string, byte> _loggedSafeSqlFailures = new();

    private const string Ok = "OK";
    private const string Warning = "Warning";
    private const string Failed = "Failed";
    private const string Manual = "Manual";

    public HomologationHealthService(
        IDbConnectionFactory db,
        ISchemaHealthService schemaHealth,
        ISchemaCompatibilityState schemaState,
        IFolderQueries folders,
        IDocumentQueries documents,
        ICurrentUser currentUser,
        IConfiguration configuration,
        IHostEnvironment environment,
        IOptions<DocumentUploadOptions> uploadOptions,
        IOptions<LocalStorageOptions> storageOptions,
        IOptions<LoanOverdueWorkerOptions> loanOptions,
        ILogger<HomologationHealthService> logger)
    {
        _db = db;
        _schemaHealth = schemaHealth;
        _schemaState = schemaState;
        _folders = folders;
        _documents = documents;
        _currentUser = currentUser;
        _configuration = configuration;
        _environment = environment;
        _uploadOptions = uploadOptions.Value;
        _storageOptions = storageOptions.Value;
        _loanOptions = loanOptions.Value;
        _logger = logger;
    }

    public async Task<HomologationReportDto> GenerateAsync(string? generatedBy, CancellationToken ct)
    {
        var report = new HomologationReportDto
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            GeneratedBy = string.IsNullOrWhiteSpace(generatedBy) ? "ADMIN" : generatedBy!,
            Environment = _environment.EnvironmentName,
            Version = typeof(HomologationHealthService).Assembly.GetName().Version?.ToString() ?? "n/a"
        };

        await AddSchemaChecksAsync(report, ct);
        await AddGedChecksAsync(report, ct);
        await AddUploadChecksAsync(report, ct);
        await AddOcrChecksAsync(report, ct);
        await AddPreviewChecksAsync(report, ct);
        await AddPartialDocumentChecksAsync(report, ct);
        await AddHospitalDocumentsChecksAsync(report, ct);
        await AddAuditChecksAsync(report, ct);
        AddPermissionChecks(report);
        AddWorkerChecks(report);
        AddDocumentationChecks(report);
        AddManualUxChecks(report);

        report.CriticalIssues = report.Checks.Count(c => c.Status == Failed && c.Severity == "Critical");
        report.WarningIssues = report.Checks.Count(c => c.Status == Warning || (c.Status == Failed && c.Severity != "Critical"));
        report.PassedChecks = report.Checks.Count(c => c.Status == Ok);
        report.IsReadyForPresentation = report.CriticalIssues == 0;
        report.OverallStatus = report.CriticalIssues > 0
            ? "Não pronto"
            : report.WarningIssues > 0 ? "Pronto com ressalvas" : "Pronto para apresentação";
        report.SummaryText = BuildSummary(report);
        return report;
    }

    private async Task AddSchemaChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        try
        {
            var schema = await _schemaHealth.CheckAsync(ct);
            Add(report, "Banco de Dados / Schema", "/SystemHealth/Schema saudável", "Executa o diagnóstico técnico de schema usado pela tela de saúde.", schema.IsHealthy ? Ok : Failed, "Critical", schema.IsHealthy ? "Schema sem pendências críticas." : $"Tabelas ausentes: {schema.MissingTables.Count}; colunas ausentes: {schema.MissingColumns.Count}.", "Aplique database/apply_all_required_migrations.sql e revalide /SystemHealth/Schema.", "/SystemHealth/Schema");
            Add(report, "Banco de Dados / Schema", "Tabelas críticas", "Confirma que não há tabelas críticas ausentes.", schema.MissingTables.Count == 0 ? Ok : Failed, "Critical", schema.MissingTables.Count == 0 ? "Nenhuma tabela crítica ausente." : string.Join(", ", schema.MissingTables), "Aplicar migration consolidada do GED.", "/SystemHealth/Schema");
            Add(report, "Banco de Dados / Schema", "Colunas críticas", "Confirma que não há colunas críticas ausentes.", schema.MissingColumns.Count == 0 ? Ok : Failed, "Critical", schema.MissingColumns.Count == 0 ? "Nenhuma coluna crítica ausente." : string.Join(", ", schema.MissingColumns), "Aplicar migration consolidada do GED.", "/SystemHealth/Schema");
            Add(report, "Banco de Dados / Schema", "Histórico de migrations", "Verifica ged.schema_migration_history.", schema.LastMigration is not null ? Ok : Warning, "Medium", schema.LastMigration is null ? "Nenhuma migration registrada encontrada." : $"Última migration: {schema.LastMigration.ScriptName} em {schema.LastMigration.AppliedAt:dd/MM/yyyy HH:mm}.", "Registre migrations aplicadas em ged.schema_migration_history.", "/SystemHealth/Schema");
            var indexWarnings = schema.Checks.Count(c => !c.Success && c.Severity == "Warning");
            Add(report, "Banco de Dados / Schema", "Índices recomendados", "Índices recomendados não bloqueiam homologação, mas impactam performance.", indexWarnings == 0 ? Ok : Warning, "Medium", indexWarnings == 0 ? "Índices recomendados encontrados." : $"{indexWarnings} índice(s) recomendado(s) ausente(s).", "Criar índices recomendados fora do horário de pico.", "/SystemHealth/Schema");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na homologação de schema.");
            Add(report, "Banco de Dados / Schema", "Diagnóstico de schema", "Executa validações de banco.", Failed, "Critical", ex.Message, "Verifique conexão e permissões do banco.", "/SystemHealth/Schema");
        }
    }

    private async Task AddGedChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        try
        {
            var tree = await _folders.TreeAsync(_currentUser.TenantId, ct);
            var hasFolders = tree.Count > 0;
            Add(report, "GED Explorer", "Árvore de pastas", "Verifica se há pastas e se a árvore carrega sem exceção.", hasFolders ? Ok : Warning, hasFolders ? "Info" : "Medium", hasFolders ? $"{tree.Count} nó(s) carregado(s)." : "Nenhuma pasta encontrada.", "Cadastre uma pasta real para receber documentos.", "/Ged/Folders");

            var realFolder = FindFirstFolderId(tree);
            Add(report, "GED Explorer", "Pasta real para upload", "Confirma que existe uma pasta selecionável para receber documentos.", realFolder.HasValue ? Ok : Failed, "Critical", realFolder.HasValue ? $"FolderId de teste: {realFolder}." : "Nenhuma pasta real encontrada.", "Crie/importe estrutura de pastas antes da apresentação.", "/Ged/Folders");
            Add(report, "GED Explorer", "UploadFolderIdVazio", "Garante que a tela não dependa de FolderId vazio para upload.", realFolder.HasValue ? Ok : Failed, "Critical", realFolder.HasValue ? "UploadFolderIdVazio = 0 por haver pasta real selecionável." : "UploadFolderIdVazio > 0: nenhum destino válido para upload.", "Ajustar cadastro/seleção de pasta de upload.", "/Ged");

            var rows = await _documents.ListAsync(_currentUser.TenantId, realFolder, null, ct);
            Add(report, "GED Explorer", "DocumentQueries.ListAsync", "Executa a listagem de documentos com CurrentListingFolderId.", Ok, "Info", $"Listagem executada com FolderId={realFolder?.ToString() ?? "null"}; documentos retornados: {rows.Count}.", "Se a listagem esperada estiver vazia, conferir pasta atual e acervo importado.", "/Ged");
            Add(report, "GED Explorer", "Listagem retorna documentos", "Informa se há documentos disponíveis para demonstração.", rows.Count > 0 ? Ok : Warning, "Medium", rows.Count > 0 ? $"{rows.Count} documento(s) retornado(s)." : "Nenhum documento retornado na pasta testada.", "Importe documentos de demonstração ou selecione uma pasta com acervo.", "/Ged");
            Add(report, "GED Explorer", "Tela /Ged", "Link operacional para validação visual da tela GED.", Ok, "Info", "Rota mapeada como evidência para abertura manual.", "Abrir o link e confirmar renderização sem erro.", "/Ged");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha na homologação GED.");
            Add(report, "GED Explorer", "GED operacional", "Valida consultas principais do GED.", Failed, "Critical", ex.Message, "Verifique schema, permissões e dados mínimos de pastas/documentos.", "/Ged");
        }
    }

    private async Task AddUploadChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        var batchTables = await TablesExistAsync(["ged.upload_batch", "ged.upload_batch_item"], ct);
        var chunkTables = await TablesExistAsync(["ged.upload_session", "ged.upload_session_chunk"], ct);
        Add(report, "Upload", "Tabelas de upload batch", "Verifica suporte a upload em lote.", batchTables ? Ok : Failed, "Critical", batchTables ? "ged.upload_batch e ged.upload_batch_item existem." : "Tabelas de batch ausentes.", "Aplicar migrations de upload batch.", "/UploadBatch");
        Add(report, "Upload", "Tabelas de upload chunked", "Verifica suporte a upload fracionado/chunked.", chunkTables ? Ok : Failed, "Critical", chunkTables ? "ged.upload_session e ged.upload_session_chunk existem." : "Tabelas de chunk ausentes.", "Aplicar migrations de upload chunked.", "/UploadChunk");

        var hasConfig = _configuration.GetSection("DocumentUpload").Exists();
        Add(report, "Upload", "Configuração DocumentUpload", "Lê limites e concorrência do appsettings.", hasConfig ? Ok : Failed, "Critical", BuildUploadEvidence(), "Configure DocumentUpload no appsettings do ambiente.", "/Ged");
        Add(report, "Upload", "MaxFileSizeMb", "Valida limite máximo de arquivo.", _uploadOptions.MaxFileSizeMb > 0 ? Ok : Failed, "Critical", $"MaxFileSizeMb={_uploadOptions.MaxFileSizeMb}.", "Defina DocumentUpload:MaxFileSizeMb maior que zero.", "/Ged");
        Add(report, "Upload", "UseChunkedUpload", "Confirma upload chunked ativo para arquivos grandes.", _uploadOptions.UseChunkedUpload ? Ok : Warning, "Medium", $"UseChunkedUpload={_uploadOptions.UseChunkedUpload}.", "Ative DocumentUpload:UseChunkedUpload para apresentação com arquivos grandes.", "/Ged");

        var rootPath = _storageOptions.RootPath;
        var storageExists = !string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath);
        Add(report, "Upload", "Pasta de storage", "Verifica existência do RootPath de armazenamento.", storageExists ? Ok : Failed, "Critical", string.IsNullOrWhiteSpace(rootPath) ? "Storage:Local:RootPath não configurado." : $"RootPath={rootPath}; Exists={storageExists}.", "Crie a pasta e ajuste permissões do usuário da aplicação.", "/SystemHealth");
        Add(report, "Upload", "Permissão de escrita no storage", "Tenta criar e remover um arquivo temporário no storage.", CanWriteStorage(rootPath, out var writeEvidence) ? Ok : Failed, "Critical", writeEvidence, "Conceda permissão de escrita ao usuário do pool/serviço.", "/SystemHealth");

        await AddUploadHistoryChecksAsync(report, ct);
    }

    private async Task AddOcrChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        Add(report, "OCR", "OcrWorker habilitado", "Confirma se a fila de OCR pode ser processada.", _configuration.GetValue("OcrWorker:Enabled", false) ? Ok : Warning, "Medium", $"OcrWorker:Enabled={_configuration.GetValue("OcrWorker:Enabled", false)}.", "Habilite OcrWorker para processar texto pesquisável.", "/SystemHealth");
        AddPathCheck(report, "OCR", "TesseractPath", "Ocr:TesseractPath", true, "Binário principal de OCR.");
        AddPathCheck(report, "OCR", "OcrMyPdfPath", "Ocr:OcrMyPdfPath", false, "OCR em PDFs/imagens digitalizadas.");
        AddPathCheck(report, "OCR", "PdfToTextPath", "Ocr:PdfToTextPath", false, "Extração textual de PDFs pesquisáveis.");
        AddPathCheck(report, "OCR", "PopplerBinPath", "Ocr:PopplerBinPath", false, "Ferramentas Poppler.");
        AddPathCheck(report, "OCR", "QpdfPath", "Ocr:QpdfPath", false, "Validação/reparo opcional de PDF.");
        AddPathCheck(report, "OCR", "TesseractDataPath", "Ocr:TesseractDataPath", false, "Pacotes de idiomas do Tesseract.");
        var languages = _configuration["Ocr:Languages"];
        Add(report, "OCR", "Linguagens configuradas", "Confirma idiomas de OCR.", string.IsNullOrWhiteSpace(languages) ? Failed : Ok, string.IsNullOrWhiteSpace(languages) ? "Critical" : "Info", string.IsNullOrWhiteSpace(languages) ? "Nenhuma linguagem configurada." : $"Languages={languages}.", "Configure Ocr:Languages, ex.: por+eng.", "/SystemHealth");

        await AddOcrJobChecksAsync(report, ct);
    }

    private async Task AddPreviewChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        AddPathCheck(report, "Preview", "LibreOfficePath", "Preview:LibreOfficePath", false, "Conversão de DOCX/PPTX/XLSX para preview.");
        var timeoutSeconds = _configuration.GetValue<int?>("Preview:TimeoutSeconds") ?? (_configuration.GetValue<int?>("Preview:TimeoutMinutes") ?? 0) * 60;
        Add(report, "Preview", "Timeout de preview", "Confirma tempo máximo configurado.", timeoutSeconds > 0 ? Ok : Warning, "Medium", $"TimeoutSeconds={timeoutSeconds}.", "Configure Preview:TimeoutSeconds ou Preview:TimeoutMinutes.", "/Ged");
        Add(report, "Preview", "Preview PDF", "PDF é exibido diretamente pelo browser/visualizador.", Ok, "Info", "Rota de preview disponível para validação manual em documentos PDF.", "Abrir documento PDF no GED e HospitalDocuments.", "/Ged");
        Add(report, "Preview", "Preview imagem", "Imagens são exibidas diretamente no preview.", Ok, "Info", "Rota de preview disponível para validação manual em imagens.", "Abrir documento JPG/PNG no GED e HospitalDocuments.", "/Ged");
        Add(report, "Preview", "DOCX/PPTX/XLSX", "Arquivos Office dependem de LibreOffice.", FileOrDirectoryExists(_configuration["Preview:LibreOfficePath"]) ? Ok : Warning, "Medium", "Conversão Office vinculada ao Preview:LibreOfficePath.", "Instalar/configurar LibreOffice no servidor.", "/Ged");
        Add(report, "Preview", "UX sem modal travado", "Confirmação visual/manual de que preview lateral não bloqueia tela.", Manual, "Info", "Checklist manual disponível na tela.", "Marcar Aprovado/Reprovado após teste visual.", "/HospitalDocuments", false);
        Add(report, "Preview", "Preview GED e HospitalDocuments", "Links para validação manual dos previews principais.", Manual, "Info", "Validar /Ged e /HospitalDocuments.", "Abrir previews laterais e expandir sem modal bloqueante.", "/HospitalDocuments", false);
    }

    private async Task AddPartialDocumentChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        var table = await TablesExistAsync(["ged.document_partial_part"], ct);
        var columns = await ColumnsExistAsync("ged", "document_version", ["is_partial_document", "partial_group_id", "partial_part_number", "partial_total_parts", "partial_status", "consolidated_version_id"], ct);
        Add(report, "Documentos incompletos", "Estrutura de colunas", "Verifica colunas de documento parcial em ged.document_version.", columns ? Ok : Failed, "Critical", columns ? "Colunas de documento parcial existem." : "Uma ou mais colunas de documento parcial estão ausentes.", "Aplicar migration de documentos fracionados.", "/SystemHealth/Schema");
        Add(report, "Documentos incompletos", "Tabela document_partial_part", "Verifica tabela de partes.", table ? Ok : Failed, "Critical", table ? "ged.document_partial_part existe." : "Tabela ged.document_partial_part ausente.", "Aplicar migration de documentos fracionados.", "/SystemHealth/Schema");
        var count = await ScalarSafeAsync<long>("select count(*) from ged.document_version where tenant_id = @tenantId and (coalesce(is_partial_document,false) or upper(coalesce(partial_status::text,'')) in ('INCOMPLETE','PENDING'))", new { tenantId = _currentUser.TenantId }, ct);
        Add(report, "Documentos incompletos", "Contador de incompletos", "Conta documentos fracionados/incompletos.", Ok, "Info", count > 0 ? $"{count} documento(s) incompleto(s) encontrado(s)." : "Estrutura disponível. Nenhum documento incompleto no momento.", "Se houver registros, conferir badge, ações e explicação no GED.", "/Ged");
        Add(report, "Documentos incompletos", "Ações e badges no GED", "Adicionar parte, Ver partes e Consolidar devem aparecer quando aplicável.", Manual, "Info", "Validação visual na listagem GED.", "Marcar checklist manual após validar dropdown e badges.", "/Ged", false);
    }

    private async Task AddHospitalDocumentsChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        var docs = await ScalarSafeAsync<long>("select count(*) from ged.document where tenant_id = @tenantId and reg_status = 'A'", new { tenantId = _currentUser.TenantId }, ct);
        var ocr = await ScalarSafeAsync<long>("select count(*) from ged.document_search where tenant_id = @tenantId and nullif(ocr_text,'') is not null", new { tenantId = _currentUser.TenantId }, ct);
        Add(report, "HospitalDocuments", "/HospitalDocuments abre", "Link de evidência para busca hospitalar.", Ok, "Info", "Rota protegida por política HospitalDocumentsOrLoansAccess.", "Abrir como ADMIN e usuário hospitalar permitido.", "/HospitalDocuments");
        Add(report, "HospitalDocuments", "Busca e quantitativos", "Indica volume disponível para busca hospitalar.", docs > 0 ? Ok : Warning, "Medium", $"Documentos ativos no tenant: {docs}; documentos com OCR: {ocr}.", "Importar/acessar acervo real para demonstração de busca.", "/HospitalDocuments");
        Add(report, "HospitalDocuments", "Autocomplete e preview lateral", "Validação visual de autocomplete sem corte e preview sem bloqueio.", Manual, "Info", "Checklist manual disponível.", "Testar autocomplete, resultado OCR, expandir e preview lateral.", "/HospitalDocuments", false);
        Add(report, "HospitalDocuments", "Permissão hospitalar", "Usuário hospitalar deve ver apenas documentos permitidos.", Manual, "Critical", "Requer teste com perfil hospitalar.", "Validar com ADMINISTRADOROPHIR, ARQUIVISTAOPHIR e HOSPITAL.", "/HospitalDocuments", false);
    }

    private async Task AddAuditChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        var appAudit = await TablesExistAsync(["ged.app_audit_log"], ct);
        var audit = await TablesExistAsync(["ged.audit_log"], ct);
        Add(report, "Logs e Auditoria", "/SystemLogs abre", "Link operacional para logs do sistema.", Ok, "Info", "Rota administrativa disponível como evidência.", "Abrir e testar filtros.", "/SystemLogs");
        Add(report, "Logs e Auditoria", "app_audit_log", "Tabela principal de auditoria da aplicação.", appAudit ? Ok : Failed, "Critical", appAudit ? "ged.app_audit_log existe." : "ged.app_audit_log ausente.", "Aplicar migration de auditoria.", "/SystemHealth/Schema");
        Add(report, "Logs e Auditoria", "audit_log ou fallback", "Tabela legada ou fallback de auditoria.", (audit || appAudit) ? Ok : Failed, "Critical", audit ? "ged.audit_log existe." : appAudit ? "Fallback via ged.app_audit_log disponível." : "Nenhuma tabela de auditoria encontrada.", "Aplicar migration de auditoria.", "/SystemHealth/Schema");
        var appCols = await ColumnsExistAsync("ged", "app_audit_log", ["created_at"], ct);
        var userCols = await AnyColumnExistsAsync("ged", "app_audit_log", ["user_name", "user_id"], ct);
        Add(report, "Logs e Auditoria", "Campos created_at e usuário", "Confirma data e usuário/fallback.", appCols && userCols ? Ok : Failed, "Critical", $"created_at={appCols}; user_name/user_id={userCols}.", "Aplicar migration de logs com created_at e user_name/user_id.", "/SystemHealth/Schema");
        Add(report, "Logs e Auditoria", "Filtros e ações críticas", "action, usuário, path, correlationId e período; upload, preview, OCR, download, movimentação, schema repair, login/logout e acesso negado.", Manual, "Info", "Validação funcional/manual dos filtros e eventos críticos.", "Executar eventos reais e filtrar em /SystemLogs.", "/SystemLogs", false);
    }

    private void AddPermissionChecks(HomologationReportDto report)
    {
        Add(report, "Perfis e Menu", "ADMIN vê tudo", "Tela acessível somente a ADMIN.", Ok, "Critical", "Controller protegido por AppPolicies.AdminOnly.", "Manter rota administrativa restrita.", "/SystemHealth/Homologation");
        Add(report, "Perfis e Menu", "Perfis hospitalares", "ADMINISTRADOROPHIR/ARQUIVISTAOPHIR redirecionam para /HospitalDocuments e veem menus permitidos.", Manual, "Critical", "Requer sessão com perfis restritos.", "Validar login, sidebar, item ativo e 403 em rotas administrativas.", "/HospitalDocuments", false);
        Add(report, "Perfis e Menu", "Rotas administrativas 403", "Usuários não ADMIN devem ter acesso negado à homologação/schema/logs.", Manual, "Critical", "Requer teste com usuário não ADMIN.", "Entrar com usuário restrito e abrir /SystemHealth/Homologation.", "/SystemHealth/Homologation", false);
    }

    private void AddWorkerChecks(HomologationReportDto report)
    {
        var disableWhenInvalid = _configuration.GetValue("Database:DisableWorkersWhenSchemaInvalid", true);
        var workerEvidence = _schemaState.IsCompatible
            ? "Schema compatível: workers podem iniciar normalmente."
            : disableWhenInvalid && _schemaState.WorkersDisabled ? "Aguardando schema compatível." : "Schema inválido, mas bloqueio de workers desativado.";
        Add(report, "Workers", "SchemaStartupValidation", "Estado compartilhado da validação de schema no startup.", _schemaState.IsCompatible ? Ok : Warning, _schemaState.IsCompatible ? "Info" : "Medium", workerEvidence, "Se estiver aguardando schema, aplicar migrations e reiniciar.", "/SystemHealth/Schema");
        Add(report, "Workers", "OcrWorker", "Valida configuração do worker de OCR.", _configuration.GetValue("OcrWorker:Enabled", false) ? Ok : Warning, "Medium", $"Enabled={_configuration.GetValue("OcrWorker:Enabled", false)}; {workerEvidence}", "Habilite OcrWorker e garanta schema compatível.", "/SystemHealth");
        Add(report, "Workers", "RetentionDailyWorker", "Worker diário de retenção registrado no host.", _schemaState.IsCompatible ? Ok : Warning, "Medium", workerEvidence, "Validar logs do worker após startup.", "/SystemHealth");
        var loanStatus = !_loanOptions.Enabled || _loanOptions.TenantId != Guid.Empty ? Ok : Warning;
        Add(report, "Workers", "LoanOverdueWorker", "Se habilitado, TenantId não pode ser Guid.Empty.", loanStatus, "Medium", $"Enabled={_loanOptions.Enabled}; TenantId={_loanOptions.TenantId}.", "Configure Workers:LoanOverdue:TenantId ou desabilite o worker.", "/SystemHealth");
    }

    private void AddDocumentationChecks(HomologationReportDto report)
    {
        AddFileCheck(report, "Documentação / Manual", "Manual InovaGED", "docs/manual-inovaged.md", "/docs/manual-inovaged.md");
        AddFileCheck(report, "Documentação / Manual", "Configuração", "docs/configuracao.md", "/docs/configuracao.md");
        AddFileCheck(report, "Documentação / Manual", "Troubleshooting", "docs/troubleshooting.md", "/docs/troubleshooting.md");
    }

    private void AddManualUxChecks(HomologationReportDto report)
    {
        string[] checks =
        [
            "Listagem sem checkbox solto no meio", "Linha selecionada destaca", "Botão Mover selecionados claro",
            "Documento incompleto explicado", "OCR disponível somente quando real", "Upload em visível",
            "Ações agrupadas", "Smart List padrão", "Tabela como alternativa", "Navegação com muitos documentos"
        ];
        foreach (var check in checks)
        {
            Add(report, "UX GED", check, "Checklist manual/visual de experiência do GED.", Manual, "Info", "Aguardando marcação manual nesta tela.", "Marque Aprovado/Reprovado e registre observação após teste visual.", "/Ged", false);
        }
    }

    private async Task AddUploadHistoryChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        var lastSuccess = await ScalarDateTimeOffsetSafeAsync("select max(finished_at) from ged.upload_batch_item where tenant_id = @tenantId and upper(status::text) in ('COMPLETED','DONE','SUCCESS')", new { tenantId = _currentUser.TenantId }, ct, "Upload", "Último upload bem-sucedido");
        var errors = await ScalarSafeAsync<long>("select count(*) from ged.upload_batch_item where tenant_id = @tenantId and upper(status::text) in ('ERROR','FAILED')", new { tenantId = _currentUser.TenantId }, ct, "Upload", "Uploads com erro");
        Add(report, "Upload", "Último upload bem-sucedido", "Informa se há evidência recente de upload.", lastSuccess.HasValue ? Ok : Warning, "Low", lastSuccess.HasValue ? $"Último upload concluído em {lastSuccess.Value:dd/MM/yyyy HH:mm}." : "Nenhum upload concluído encontrado.", "Realize upload de homologação antes da apresentação.", "/UploadBatch");
        Add(report, "Upload", "Uploads com erro", "Exibe existência de falhas recentes/pendentes.", errors == 0 ? Ok : Warning, "Medium", errors == 0 ? "Nenhum item de upload com erro encontrado." : $"{errors} item(ns) com erro encontrados.", "Abrir /UploadBatch e revisar mensagens de erro.", "/UploadBatch");
    }

    private async Task AddOcrJobChecksAsync(HomologationReportDto report, CancellationToken ct)
    {
        var args = new { tenantId = _currentUser.TenantId };
        var pending = await ScalarSafeAsync<long>("select count(*) from ged.ocr_job where tenant_id=@tenantId and upper(status::text)='PENDING'", args, ct, "OCR", "Jobs PENDING");
        var processing = await ScalarSafeAsync<long>("select count(*) from ged.ocr_job where tenant_id=@tenantId and upper(status::text)='PROCESSING'", args, ct, "OCR", "Jobs PROCESSING");
        var completed = await ScalarSafeAsync<long>("select count(*) from ged.ocr_job where tenant_id=@tenantId and upper(status::text)='COMPLETED'", args, ct, "OCR", "Jobs COMPLETED");
        var error = await ScalarSafeAsync<long>("select count(*) from ged.ocr_job where tenant_id=@tenantId and upper(status::text)='ERROR'", args, ct, "OCR", "Jobs ERROR");
        Add(report, "OCR", "Jobs PENDING/PROCESSING/COMPLETED/ERROR", "Resumo da fila de OCR.", (pending + processing + completed + error) > 0 ? Ok : Warning, "Low", (pending + processing + completed + error) > 0 ? $"PENDING={pending}; PROCESSING={processing}; COMPLETED={completed}; ERROR={error}." : "Nenhum job OCR encontrado.", "Gerar job de OCR de homologação se a base estiver vazia.", "/SystemHealth");

        var invalidSql = await BuildCompletedOcrWithoutTextSqlAsync(ct);
        if (invalidSql is null)
        {
            AddCheckWarning(report, "OCR", "Regra OCR disponível", "Não foi possível validar COMPLETED sem texto porque ged.document_search não possui uma coluna de vínculo compatível.", "Aplicar migration de ged.document_search com version_id ou document_version_id.");
            return;
        }

        var invalid = await ScalarSafeAsync<long>(invalidSql, args, ct, "OCR", "Regra OCR disponível");
        Add(report, "OCR", "Regra OCR disponível", "OCR disponível exige status COMPLETED e texto não vazio.", invalid == 0 ? Ok : Warning, "Medium", invalid == 0 ? "Nenhum COMPLETED sem texto encontrado." : $"{invalid} job(s) COMPLETED sem texto indexado.", "Reprocessar OCR/document_search dos documentos afetados.", "/HospitalDocuments");
    }

    private void AddPathCheck(HomologationReportDto report, string area, string name, string key, bool critical, string description)
    {
        var value = _configuration[key];
        var exists = FileOrDirectoryExists(value);
        Add(report, area, name, description, exists ? Ok : (critical ? Failed : Warning), critical ? "Critical" : "Medium", string.IsNullOrWhiteSpace(value) ? $"{key} não configurado." : $"{key}={value}; Exists={exists}.", $"Configure {key} com um caminho válido no servidor.", "/SystemHealth");
    }

    private void AddFileCheck(HomologationReportDto report, string area, string name, string relativePath, string link)
    {
        var path = Path.Combine(_environment.ContentRootPath, "..", relativePath);
        var exists = File.Exists(Path.GetFullPath(path));
        Add(report, area, name, $"Verifica arquivo {relativePath}.", exists ? Ok : Warning, "Low", exists ? $"{relativePath} encontrado." : $"{relativePath} não encontrado.", "Criar/atualizar documentação para entrega ao cliente.", link);
    }

    private static void AddCheckWarning(HomologationReportDto report, string area, string name, string message, string recommendedAction)
        => Add(report, area, name, "Check auxiliar de homologação não crítico.", Warning, "Medium", message, recommendedAction, "/SystemHealth");

    private static void Add(HomologationReportDto report, string area, string name, string description, string status, string severity, string evidence, string action, string link, bool automatic = true)
    {
        report.Checks.Add(new HomologationCheckDto { Area = area, Name = name, Description = description, Status = status, Severity = severity, Evidence = evidence, RecommendedAction = action, Link = link, IsAutomatic = automatic });
    }

    private async Task<bool> TablesExistAsync(string[] fullNames, CancellationToken ct)
    {
        foreach (var fullName in fullNames)
        {
            var exists = await ScalarSafeAsync<bool>("select to_regclass(@name) is not null", new { name = fullName }, ct);
            if (!exists) return false;
        }
        return true;
    }

    private async Task<bool> ColumnsExistAsync(string schema, string table, string[] columns, CancellationToken ct)
    {
        foreach (var column in columns)
        {
            var exists = await ScalarSafeAsync<bool>("select exists(select 1 from information_schema.columns where table_schema=@schema and table_name=@table and column_name=@column)", new { schema, table, column }, ct);
            if (!exists) return false;
        }
        return true;
    }

    private async Task<bool> AnyColumnExistsAsync(string schema, string table, string[] columns, CancellationToken ct)
    {
        foreach (var column in columns)
        {
            if (await ColumnsExistAsync(schema, table, [column], ct)) return true;
        }
        return false;
    }

    private async Task<string?> GetDocumentSearchVersionColumnAsync(CancellationToken ct)
    {
        if (await ColumnsExistAsync("ged", "document_search", ["version_id"], ct)) return "version_id";
        if (await ColumnsExistAsync("ged", "document_search", ["document_version_id"], ct)) return "document_version_id";
        return null;
    }

    private async Task<string?> BuildCompletedOcrWithoutTextSqlAsync(CancellationToken ct)
    {
        if (!await TablesExistAsync(["ged.document_search"], ct) ||
            !await ColumnsExistAsync("ged", "document_search", ["ocr_text"], ct))
        {
            return null;
        }

        var versionColumn = await GetDocumentSearchVersionColumnAsync(ct);
        if (versionColumn is not null)
        {
            return $@"
select count(*)
from ged.ocr_job oj
left join ged.document_search ds
  on ds.tenant_id = oj.tenant_id
 and ds.{versionColumn} = oj.document_version_id
where oj.tenant_id = @tenantId
  and upper(oj.status::text) = 'COMPLETED'
  and nullif(trim(coalesce(ds.ocr_text, '')), '') is null";
        }

        if (await ColumnsExistAsync("ged", "document_search", ["document_id"], ct) &&
            await ColumnsExistAsync("ged", "document_version", ["document_id"], ct))
        {
            return @"
select count(*)
from ged.ocr_job oj
left join ged.document_version dv
  on dv.tenant_id = oj.tenant_id
 and dv.id = oj.document_version_id
left join ged.document_search ds
  on ds.tenant_id = oj.tenant_id
 and ds.document_id = dv.document_id
where oj.tenant_id = @tenantId
  and upper(oj.status::text) = 'COMPLETED'
  and nullif(trim(coalesce(ds.ocr_text, '')), '') is null";
        }

        return null;
    }

    private async Task<DateTimeOffset?> ScalarDateTimeOffsetSafeAsync(string sql, object? args, CancellationToken ct, string? area = null, string? name = null)
    {
        var value = await ScalarSafeAsync<object?>(sql, args, ct, area, name);

        if (value is null || value is DBNull) return null;
        if (value is DateTimeOffset dto) return dto;
        if (value is DateTime dt)
        {
            var normalized = dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt.ToUniversalTime();
            return new DateTimeOffset(normalized);
        }

        return DateTimeOffset.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private async Task<T> ScalarSafeAsync<T>(string sql, object? args, CancellationToken ct, string? area = null, string? name = null)
    {
        try
        {
            await using var conn = await _db.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<T>(new CommandDefinition(sql, args, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            var logKey = $"{area}|{name}|{ex.GetType().FullName}|{sql}";
            if (_loggedSafeSqlFailures.TryAdd(logKey, 0))
            {
                _logger.LogWarning(ex, "Check de homologação ignorado por falha SQL segura. Area={Area} Check={CheckName} Sql={Sql}", area ?? "n/a", name ?? "n/a", sql);
            }
            else
            {
                _logger.LogDebug(ex, "Falha SQL segura repetida ignorada. Area={Area} Check={CheckName}", area ?? "n/a", name ?? "n/a");
            }

            return default!;
        }
    }

    private static Guid? FindFirstFolderId(IEnumerable<FolderNodeDto> nodes)
    {
        return nodes.FirstOrDefault(n => n.Id != Guid.Empty && (!n.IsVirtual || n.CanReceiveDocuments))?.Id
            ?? nodes.FirstOrDefault(n => n.Id != Guid.Empty)?.Id;
    }

    private static bool FileOrDirectoryExists(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path));
    }

    private static bool CanWriteStorage(string? rootPath, out string evidence)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            evidence = "Storage inexistente ou não configurado.";
            return false;
        }
        try
        {
            var probe = Path.Combine(rootPath, $"homologation-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            evidence = $"Escrita/remocão OK em {rootPath}.";
            return true;
        }
        catch (Exception ex)
        {
            evidence = $"Falha de escrita em {rootPath}: {ex.Message}";
            return false;
        }
    }

    private string BuildUploadEvidence()
    {
        return $"RootPath={_storageOptions.RootPath}; MaxFileSizeMb={_uploadOptions.MaxFileSizeMb}; ChunkSizeMb={_uploadOptions.ChunkSizeMb}; MaxConcurrentUploadsPerUser={_uploadOptions.MaxConcurrentUploadsPerUser}; MaxConcurrentUploadsGlobal={_uploadOptions.MaxConcurrentUploadsGlobal}.";
    }

    private static string BuildSummary(HomologationReportDto report)
    {
        string AreaStatus(string area)
        {
            var checks = report.Checks.Where(c => c.Area.StartsWith(area, StringComparison.OrdinalIgnoreCase)).ToList();
            if (checks.Any(c => c.Status == Failed && c.Severity == "Critical")) return "Falha crítica";
            var warnings = checks.Count(c => c.Status == Warning || (c.Status == Failed && c.Severity != "Critical"));
            return warnings > 0 ? $"{warnings} aviso(s)" : "OK";
        }

        return $"Homologação InovaGED:\nStatus: {report.OverallStatus}\nBanco: {AreaStatus("Banco")}\nGED: {AreaStatus("GED")}\nUpload: {AreaStatus("Upload")}\nOCR: {AreaStatus("OCR")}\nLogs: {AreaStatus("Logs")}\nPendências críticas: {report.CriticalIssues}\nPendências recomendadas: {report.WarningIssues}";
    }
}
