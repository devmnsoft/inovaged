using System.Security.Claims;
using Dapper;
using InovaGed.Infrastructure.Common.Database;
using InovaGed.Infrastructure.Security;
using Npgsql;

namespace InovaGed.Application.Tests;

[Collection("Document AI PostgreSQL")]
public sealed class DocumentAiAccessPostgresTests
{
    [PgGatedFact]
    public async Task Record_acl_tenant_secrecy_and_live_permission_revocation_are_enforced()
    {
        await WithDatabase(async (connection, factory) =>
        {
            var tenant = Guid.NewGuid(); var other = Guid.NewGuid(); var editor = Guid.NewGuid(); var reader = Guid.NewGuid();
            var foreign = Guid.NewGuid(); var role = Guid.NewGuid(); var document = Guid.NewGuid();
            await connection.ExecuteAsync("""
insert into ged.app_user values(@editor,@tenant,'A',true),(@reader,@tenant,'A',true),(@foreign,@other,'A',true);
insert into ged.app_role values(@role,@tenant); insert into ged.role values(@role,@tenant);
insert into ged.user_role values(@editor,@role),(@reader,@role),(@foreign,@role);
insert into ged.permission values('Documents.View','A'),('GED.DOCUMENTS','A');
insert into ged.role_permission values(@role,@tenant,'Documents.View','A'),(@role,@tenant,'GED.DOCUMENTS','A');
insert into ged.document values(@document,@tenant,false,'A');
insert into ged.document_acl values(@document,@editor,null,true,true);
""", new { tenant, other, editor, reader, foreign, role, document });
            var auth = new AbacAuthorizationService(factory);
            Task<bool> Can(Guid t, Guid u, string action = "VIEW") => auth.CanAccessDocumentAsync(t, u, document, action, new Dictionary<string, string>(), default);
            Assert.True(await Can(tenant, editor));
            Assert.Equal(new[] { document }, (await auth.FilterDocumentsAsync(tenant, editor, new[] { document, Guid.NewGuid() }, "VIEW", default)).ToArray());
            Assert.Empty(await auth.FilterDocumentsAsync(tenant, reader, new[] { document }, "VIEW", default));
            Assert.True(await Can(tenant, editor, "EDIT"));
            Assert.False(await Can(tenant, reader)); // module permission alone does not grant the record
            Assert.False(await Can(tenant, foreign));
            Assert.False(await Can(other, foreign));
            await connection.ExecuteAsync("update ged.document_acl set can_write=false where document_id=@document", new { document });
            Assert.True(await Can(tenant, editor));
            Assert.False(await Can(tenant, editor, "EDIT"));
            await connection.ExecuteAsync("delete from ged.role_permission where permission_code='Documents.View'");
            Assert.False(await Can(tenant, editor)); // no stale permission cache
            await connection.ExecuteAsync("insert into ged.role_permission values(@role,@tenant,'Documents.View','A'); delete from ged.document_acl; update ged.document set is_confidential=true", new { tenant, role });
            Assert.False(await Can(tenant, editor)); // confidential records never fall back to module permission
        });
    }

    [PgGatedFact]
    public async Task Institutional_protocol_requires_active_sector_and_admin_still_obeys_tenant()
    {
        await WithDatabase(async (connection, factory) =>
        {
            var tenant = Guid.NewGuid(); var user = Guid.NewGuid(); var sector = Guid.NewGuid(); var protocol = Guid.NewGuid();
            await connection.ExecuteAsync("""
insert into ged.protocolo values(@protocol,@tenant,@sector,@sector,'A');
insert into ged.protocolo_setor values(@sector,@tenant,'A',true);
""", new { tenant, sector, protocol });
            var service = new InovaGed.Infrastructure.Ged.Loans.ProtocolAccessService(factory);
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "ADMINISTRADOROPHIR") }, "test"));
            Assert.False(await service.CanViewProtocolAsync(tenant, protocol, user, principal, default));
            await connection.ExecuteAsync("insert into ged.protocolo_usuario_setor values(@tenant,@user,@sector,'A',true)", new { tenant, user, sector });
            Assert.True(await service.CanViewProtocolAsync(tenant, protocol, user, principal, default));
            Assert.True(await service.CanManageProtocolAsync(tenant, protocol, user, principal, default));
            await connection.ExecuteAsync("update ged.protocolo_setor set ativo=false");
            Assert.False(await service.CanViewProtocolAsync(tenant, protocol, user, principal, default));
            var admin = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "ADMIN") }, "test"));
            Assert.True(await service.CanViewProtocolAsync(tenant, protocol, user, admin, default));
            Assert.False(await service.CanViewProtocolAsync(Guid.NewGuid(), protocol, user, admin, default));
        });
    }

    private static async Task WithDatabase(Func<NpgsqlConnection, NpgsqlConnectionFactory, Task> test)
    {
        var name = "ai_access_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(PgGate.Dsn());
        await admin.OpenAsync();
        await admin.ExecuteAsync($"create database {name}");
        try
        {
            var dsn = new NpgsqlConnectionStringBuilder(PgGate.Dsn()) { Database = name, Pooling = false }.ConnectionString;
            await using var connection = new NpgsqlConnection(dsn);
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
create schema ged;
create table ged.document(id uuid primary key,tenant_id uuid,is_confidential boolean,reg_status text);
create table ged.app_user(id uuid primary key,tenant_id uuid,reg_status text,is_active boolean);
create table ged.app_role(id uuid,tenant_id uuid);
create table ged.role(id uuid,tenant_id uuid);
create table ged.user_role(user_id uuid,role_id uuid);
create table ged.permission(code text,reg_status text);
create table ged.role_permission(role_id uuid,tenant_id uuid,permission_code text,reg_status text);
create table ged.document_acl(document_id uuid,user_id uuid,role_id uuid,can_read boolean,can_write boolean);
create table ged.protocolo(id uuid,tenant_id uuid,setor_atual_id uuid,setor_origem_id uuid,reg_status text);
create table ged.protocolo_setor(id uuid,tenant_id uuid,reg_status text,ativo boolean);
create table ged.protocolo_usuario_setor(tenant_id uuid,usuario_id uuid,setor_id uuid,reg_status text,ativo boolean);
create table ged.protocolo_setor_participante(tenant_id uuid,protocolo_id uuid,setor_id uuid,pode_visualizar boolean);
""");
            await test(connection, new NpgsqlConnectionFactory(dsn));
        }
        finally { await admin.ExecuteAsync($"drop database {name}"); }
    }
}
