namespace CorporateStandardBotTest.BusinessLogic.Settings;

public class KnowledgeBaseUrlSettings
{
    public const string Position = "KnowledgeBaseUrlSettings";
    
    public Dictionary<string, string> AzureStorageConnectionStrings { get; set; } = new();
}
