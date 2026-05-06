namespace CorporateStandardBotTest.BusinessLogic.Models;

public record AiChat(Guid ThreadId, ICollection<AiChatMessage> Messages);