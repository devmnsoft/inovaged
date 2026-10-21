using System.Security.Claims;
using InovaGed.Application.Identity;
using InovaGed.Application.Security;
using InovaGed.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace InovaGed.Application.Tests;

public sealed class BillingPermissionTests
{
    [Theory]
    [InlineData("Index","BILLING_VIEW",true)]
    [InlineData("Review","BILLING_VIEW",false)]
    [InlineData("Review","BILLING_REVIEW",true)]
    [InlineData("Settle","BILLING_REVIEW",false)]
    [InlineData("Settle","BILLING_SETTLE",true)]
    [InlineData("SaveDuplicatePolicy","BILLING_VIEW",false)]
    [InlineData("SaveDuplicatePolicy","BILLING_MANAGE",true)]
    public async Task Permissions_are_checked_for_each_operation(string action,string granted,bool allowed)
    {
        var user=new CurrentUser();
        var permissions=new Permissions(granted);
        var controller=new BillingController(user,null!,null!,null!,null!,permissions);
        var descriptor=new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor{RouteValues=new Dictionary<string,string?>{{"action",action}}};
        var context=new ActionContext(new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity())},new RouteData(),descriptor);
        controller.ControllerContext=new ControllerContext(context);
        var executing=new ActionExecutingContext(context,new List<IFilterMetadata>(),new Dictionary<string,object?>(),controller);
        var called=false;
        await controller.OnActionExecutionAsync(executing,()=>{called=true;return Task.FromResult(new ActionExecutedContext(context,new List<IFilterMetadata>(),controller));});
        Assert.Equal(allowed,called);
        if(!allowed) Assert.IsType<ForbidResult>(executing.Result);
        Assert.Equal(user.TenantId,permissions.Tenant);
        Assert.Equal(user.UserId,permissions.User);
    }
    private sealed class Permissions(string granted):IPermissionService
    {
        public Guid Tenant {get;private set;}
        public Guid User {get;private set;}
        public Task<bool> HasAsync(Guid tenantId,Guid userId,string code,CancellationToken ct)
        {Tenant=tenantId;User=userId;return Task.FromResult(code==granted);}
    }
    private sealed class CurrentUser:ICurrentUser
    {
        public bool IsAuthenticated=>true;
        public Guid TenantId {get;}=Guid.NewGuid();
        public Guid UserId {get;}=Guid.NewGuid();
        public string Email=>"financeiro@example.test";
        public IReadOnlyList<string> Roles=>[];
    }
}
