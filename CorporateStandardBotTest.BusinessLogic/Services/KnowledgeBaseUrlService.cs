using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using CorporateStandardBotTest.BusinessLogic.Settings;
using Microsoft.Extensions.Options;

namespace CorporateStandardBotTest.BusinessLogic.Services;

public interface IKnowledgeBaseUrlService
{
    string GetSignedUrl(string url);
}

public class KnowledgeBaseUrlService(IOptions<KnowledgeBaseUrlSettings> settings) : IKnowledgeBaseUrlService
{
    public string GetSignedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) 
            || !settings.Value.AzureStorageConnectionStrings.TryGetValue(uri.Host, out var connectionString))
            return url;

        var (containerName, blobName) = ExtractContainerBlobName(uri.LocalPath);

        var sasBuilder = new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddDays(1))
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b"
        };
        
        sasBuilder.ContentDisposition = "inline";

        var blobClient = new BlobClient(connectionString, containerName, blobName);

        var sharedAccessSignature = blobClient.GenerateSasUri(sasBuilder);
        var sasUrl = sharedAccessSignature.ToString();
        return sasUrl;
    }

    private (string ContainerName, string BlobName) ExtractContainerBlobName(string? path)
    {
        path = path?.Replace(@"\", "/") ?? string.Empty;

        var root = Path.GetPathRoot(path);
        var fileNameWithoutRoot = path[(root ?? string.Empty).Length..];
        var parts = fileNameWithoutRoot.Split('/');

        var containerName = parts.First().ToLowerInvariant();
        var blobName = string.Join('/', parts.Skip(1));

        return (containerName, blobName);
    }
}