using System.Security.Claims;
using InovaGed.Application.Documents;
using InovaGed.Application.Ged.Documents;
using InovaGed.Application.Identity;
using InovaGed.Domain.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using WebGed.WebApi.Controllers;
using Xunit;

namespace InovaGed.Application.Tests.WebApi;

public sealed class DocumentsControllerMoveTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task SearchFolders_envia_tenant_e_user_do_current_user()
    {
        var move = new FakeDocumentMoveService();
        var controller = CreateController(move);

        await controller.SearchFolders("fin", CancellationToken.None);

        Assert.Equal(TenantId, move.SearchTenantId);
        Assert.Equal(UserId, move.SearchUserId);
    }

    [Fact]
    public async Task Move_envia_is_admin_false_para_usuario_comum()
    {
        var move = new FakeDocumentMoveService();
        var controller = CreateController(move, roles: ["OPERADOR"]);

        await controller.Move(NewMoveRequest(), CancellationToken.None);

        Assert.False(move.LastIsAdmin);
    }

    [Fact]
    public async Task Move_envia_is_admin_true_para_administrador()
    {
        var move = new FakeDocumentMoveService();
        var controller = CreateController(move, roles: [DocumentMoveAuthorizationRoles.AdministradorOphir]);

        await controller.Move(NewMoveRequest(), CancellationToken.None);

        Assert.True(move.LastIsAdmin);
    }

    [Fact]
    public async Task Move_propaga_cancellation_token()
    {
        using var cts = new CancellationTokenSource();
        var move = new FakeDocumentMoveService();
        var controller = CreateController(move);

        await controller.Move(NewMoveRequest(), cts.Token);

        Assert.Equal(cts.Token, move.LastCancellationToken);
    }

    [Fact]
    public async Task MoveBulk_valida_lista_vazia_sem_chamar_servico()
    {
        var move = new FakeDocumentMoveService();
        var controller = CreateController(move);

        var result = await controller.MoveBulk(new DocumentBulkMoveRequestVM { DocumentIds = [], DestinationFolderId = Guid.NewGuid() }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, move.BulkCalls);
    }

    [Fact]
    public async Task MoveBulk_rejeita_ids_duplicados()
    {
        var id = Guid.NewGuid();
        var move = new FakeDocumentMoveService();
        var controller = CreateController(move);

        var result = await controller.MoveBulk(new DocumentBulkMoveRequestVM { DocumentIds = [id, id], DestinationFolderId = Guid.NewGuid() }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, move.BulkCalls);
    }

    [Theory]
    [InlineData("ACCESS_DENIED", StatusCodes.Status403Forbidden)]
    [InlineData("DOCUMENT_NOT_FOUND", StatusCodes.Status404NotFound)]
    [InlineData("CONFLICT", StatusCodes.Status409Conflict)]
    public async Task Move_mapeia_falhas(string code, int expectedStatus)
    {
        var move = new FakeDocumentMoveService { MoveResult = Result<DocumentMoveResultDto>.Fail(code, "falha") };
        var controller = CreateController(move);

        var result = await controller.Move(NewMoveRequest(), CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
    }

    [Fact]
    public async Task Move_sucesso_retorna_200()
    {
        var controller = CreateController(new FakeDocumentMoveService());

        var result = await controller.Move(NewMoveRequest(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Move_ignora_tenant_e_usuario_do_body_quando_houver_campos_extras()
    {
        var move = new FakeDocumentMoveService();
        var controller = CreateController(move);

        await controller.Move(NewMoveRequest(), CancellationToken.None);

        Assert.Equal(TenantId, move.LastTenantId);
        Assert.Equal(UserId, move.LastUserId);
    }

    private static DocumentsController CreateController(FakeDocumentMoveService moveService, IReadOnlyList<string>? roles = null)
    {
        var currentUser = new FakeCurrentUser(TenantId, UserId, roles ?? ["OPERADOR"]);
        var controller = new DocumentsController(null!, moveService, currentUser, NullLogger<DocumentsController>.Instance);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(ClaimTypes.Email, currentUser.Email),
            new Claim(ClaimTypes.Name, "Usuário Teste")
        }.Concat(currentUser.Roles.Select(role => new Claim(ClaimTypes.Role, role))), "Test");
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return controller;
    }

    private static DocumentMoveRequestVM NewMoveRequest() => new() { DocumentId = Guid.NewGuid(), DestinationFolderId = Guid.NewGuid(), Reason = "teste" };

    private sealed record FakeCurrentUser(Guid TenantId, Guid UserId, IReadOnlyList<string> Roles) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string Email => "user@example.test";
    }

    private sealed class FakeDocumentMoveService : IDocumentMoveService
    {
        public Result<DocumentMoveResultDto> MoveResult { get; set; } = Result<DocumentMoveResultDto>.Ok(new DocumentMoveResultDto { DocumentId = Guid.NewGuid(), Success = true, Message = "ok" });
        public Guid LastTenantId { get; private set; }
        public Guid LastUserId { get; private set; }
        public bool LastIsAdmin { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public int BulkCalls { get; private set; }
        public Guid SearchTenantId { get; private set; }
        public Guid SearchUserId { get; private set; }

        public Task<Result<DocumentMoveResultDto>> MoveAsync(Guid tenantId, Guid userId, string? userName, Guid documentId, Guid destinationFolderId, string? reason, string source, bool isAdmin, CancellationToken ct)
        {
            LastTenantId = tenantId;
            LastUserId = userId;
            LastIsAdmin = isAdmin;
            LastCancellationToken = ct;
            return Task.FromResult(MoveResult);
        }

        public Task<Result<DocumentBulkMoveResultDto>> MoveBulkAsync(Guid tenantId, Guid userId, string? userName, IReadOnlyList<Guid> documentIds, Guid destinationFolderId, string? reason, string source, bool isAdmin, CancellationToken ct)
        {
            BulkCalls++;
            LastTenantId = tenantId;
            LastUserId = userId;
            LastIsAdmin = isAdmin;
            LastCancellationToken = ct;
            return Task.FromResult(Result<DocumentBulkMoveResultDto>.Ok(new DocumentBulkMoveResultDto { BatchId = Guid.NewGuid(), Total = documentIds.Count, SuccessCount = documentIds.Count }));
        }

        public Task<IReadOnlyList<FolderOptionDto>> SearchFoldersAsync(Guid tenantId, Guid userId, string? term, CancellationToken ct)
        {
            SearchTenantId = tenantId;
            SearchUserId = userId;
            return Task.FromResult<IReadOnlyList<FolderOptionDto>>([new FolderOptionDto { Id = Guid.NewGuid(), Name = term ?? "folder" }]);
        }

        public Task<IReadOnlyList<DocumentMoveHistoryDto>> GetMoveHistoryAsync(Guid tenantId, Guid documentId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DocumentMoveHistoryDto>>([]);
    }
}
