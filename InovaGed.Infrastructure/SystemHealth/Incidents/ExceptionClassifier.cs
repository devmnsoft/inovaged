using System.Text.RegularExpressions;
using InovaGed.Application.SystemHealth.Incidents;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace InovaGed.Infrastructure.SystemHealth.Incidents;

public sealed class ExceptionClassifier : IExceptionClassifier
{
    public SystemIncidentClassification Classify(Exception exception,HttpContext? httpContext=null)
    {
        var root=Unwrap(exception); var message=root.Message;
        if(root is OperationCanceledException) return New(IncidentType.OperationCancelled,IncidentSeverity.Low,"Operação cancelada","Nenhuma ação é necessária quando o cliente encerrou a requisição.");
        if(root is PostgresException pg)
        {
            var obj=pg.TableName??pg.ColumnName??ExtractObject(pg.MessageText);
            return pg.SqlState switch
            {
                "42P01"=>New(IncidentType.DatabaseSchemaMissingTable,IncidentSeverity.High,"Tabela de banco ausente","Acesse /DatabaseReadiness e aplique as migrations pendentes.",pg.SqlState,obj),
                "42703"=>New(IncidentType.DatabaseSchemaMissingColumn,IncidentSeverity.High,"Coluna de banco ausente","Execute o hotfix de compatibilidade ou acesse /DatabaseReadiness.",pg.SqlState,obj),
                "42601"=>New(IncidentType.DatabaseSqlSyntax,IncidentSeverity.High,"Erro de sintaxe SQL","Revise o SQL gerado dinamicamente.",pg.SqlState,obj),
                _=>New(IncidentType.RouteFailure,IncidentSeverity.High,"Falha de banco na rota","Revise o diagnóstico e a saúde do banco.",pg.SqlState,obj)
            };
        }
        if(message.Contains("A parameterless default constructor or one matching signature",StringComparison.OrdinalIgnoreCase)) return New(IncidentType.DapperMaterialization,IncidentSeverity.High,"Falha de materialização Dapper","Use DbRow mutável interno e mapeamento manual.");
        if(message.Contains("Unable to resolve service for type",StringComparison.OrdinalIgnoreCase)) return New(IncidentType.DependencyInjection,IncidentSeverity.Critical,"Dependência não registrada","Registre o serviço e seu ciclo de vida no container de DI.");
        if(root.GetType().Name.Contains("CompilationFailedException",StringComparison.OrdinalIgnoreCase)||new[]{"RuntimeCompilation","RZ1017","CS1525"}.Any(x=>message.Contains(x,StringComparison.OrdinalIgnoreCase))) return New(IncidentType.RazorCompilation,IncidentSeverity.High,"Falha de compilação Razor","Corrija a view Razor indicada pelo diagnóstico.");
        if(root is UnauthorizedAccessException) return New(IncidentType.PermissionFailure,IncidentSeverity.Medium,"Falha de permissão","Revise a permissão do usuário e o modo de segurança do tenant.");
        return New(IncidentType.RouteFailure,IncidentSeverity.Medium,"Falha técnica na rota","Use o CorrelationId para revisar logs e a causa raiz.");
    }
    private static Exception Unwrap(Exception e){while(e.InnerException is not null)e=e.InnerException;return e;}
    private static string? ExtractObject(string text)=>Regex.Match(text,"(?:relation|column) \\\"(?<o>[^\\\"]+)",RegexOptions.IgnoreCase).Groups["o"].Value is {Length:>0} x?x:null;
    private static SystemIncidentClassification New(string t,string s,string title,string action,string? state=null,string? obj=null)=>new(t,s,title,action,state,obj);
}
