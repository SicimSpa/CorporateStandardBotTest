using System.Diagnostics;

namespace CorporateStandardBotTest.BusinessLogic.Observability;

public class Instrumentation(string serviceName) : IDisposable
{
    public readonly ActivitySource ActivitySource = new(serviceName);

    public void Dispose()
    {
        ActivitySource.Dispose();
    }
}