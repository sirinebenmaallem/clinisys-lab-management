using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Clinisys.API.Controllers;
using Clinisys.API.Errors;
using Clinisys.Application.Features.LaboratoryRequests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clinisys.Tests.LaboratoryRequests;

// Direct handler/metadata tests: no host, SQL connection, or database service is constructed.
public sealed class LaboratoryRequestHttpUnitTests
{
    [Theory]
    [InlineData(LaboratoryRequestError.Validation,400)]
    [InlineData(LaboratoryRequestError.NotFound,404)]
    [InlineData(LaboratoryRequestError.Conflict,409)]
    [InlineData(LaboratoryRequestError.Internal,500)]
    [InlineData(LaboratoryRequestError.CommitUnknown,503)]
    public async Task Errors_UseExistingProblemTypesAndSafeMessages(LaboratoryRequestError error,int status)
    {
        var exception = error switch
        {
            LaboratoryRequestError.Validation => LaboratoryRequestException.Validation("request"),
            LaboratoryRequestError.NotFound => LaboratoryRequestException.NotFound(),
            LaboratoryRequestError.Conflict => LaboratoryRequestException.Conflict(),
            LaboratoryRequestError.Internal => LaboratoryRequestException.Internal(),
            _ => LaboratoryRequestException.Unknown()
        };
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        Assert.True(await new LaboratoryRequestExceptionHandler(NullLogger<LaboratoryRequestExceptionHandler>.Instance)
            .TryHandleAsync(context,exception,default));
        Assert.Equal(status,context.Response.StatusCode);
        Assert.Contains("application/problem+json",context.Response.ContentType!);
        context.Response.Body.Position=0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(status,body.RootElement.GetProperty("status").GetInt32());
        if(status==400) Assert.True(body.RootElement.TryGetProperty("errors",out _));
        if(error==LaboratoryRequestError.CommitUnknown) Assert.True(body.RootElement.GetProperty("outcomeUnknown").GetBoolean());
    }

    [Fact]
    public async Task DatabaseOutage_RemainsGenericAndDoesNotEchoDriverMessage()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await new CatalogExceptionHandler(NullLogger<CatalogExceptionHandler>.Instance)
            .TryHandleAsync(context,new SyntheticDatabaseException(),default);
        Assert.Equal(503,context.Response.StatusCode);
        context.Response.Body.Position=0;
        var text = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("SYN-SECRET",text);
    }

    [Fact]
    public void OpenApiMetadata_DeclaresCorrectSuccessAndProblemSchemas()
    {
        var controller=typeof(LaboratoryRequestsController);
        var expected=new Dictionary<string,int[]>
        { ["Create"]=[201,400,404,409,503,500], ["List"]=[200,400,409,503,500], ["Get"]=[200,404,409,503,500] };
        foreach(var action in expected)
        {
            var attributes=controller.GetMethod(action.Key)!.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
            Assert.Equal(action.Value.Order(),attributes.Select(x=>x.StatusCode).Order());
            foreach(var attribute in attributes.Where(x=>x.StatusCode>=400))
                Assert.Equal(attribute.StatusCode==400?typeof(ValidationProblemDetails):typeof(ProblemDetails),attribute.Type);
            if(action.Key!="Create") Assert.Contains("deadlock",Assert.Single(attributes,x=>x.StatusCode==409).Description!);
        }
    }
    private sealed class SyntheticDatabaseException : DbException
    { public SyntheticDatabaseException():base("SYN-SECRET SQL connection details") { } }
}
