using System.Security.Claims;
using CorporateStandardBotTest.BusinessLogic.Models;
using CorporateStandardBotTest.BusinessLogic.Services;
using Microsoft.AspNetCore.Mvc;
using MinimalHelpers.Routing;

namespace CorporateStandardBotTest.Api;

public class ChatEndpoints : IEndpointRouteHandlerBuilder
{
    public static void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/chat")
            .WithTags("ChatEndpoints");

        group.MapPost("complete", HandleCompleteAsync)
            .Produces<AiChatMessage>()
            .ProducesProblem(500);
    }

    private static async Task<IResult> HandleCompleteAsync(HttpContext context, [FromBody] AiCompletionRequest request,
        [FromServices] IKnowledgeBaseService kbService)
    {
        var userEmail = context.User.FindFirstValue(ClaimTypes.Upn) ?? context.User.FindFirstValue(ClaimTypes.Email);
        var result = await kbService.GetResponseAsync(request, userEmail);

        return result.Match<IResult>(
            success => Results.Ok(success),
            error => Results.Problem(error.Message, statusCode: 500)
        );
    }
}