namespace CorporateStandardBotTest.BusinessLogic.Models;

public record AiChatMessage(Guid MessageId, AiMessageRole Role, string Content, List<AiChatReference>? References = null);