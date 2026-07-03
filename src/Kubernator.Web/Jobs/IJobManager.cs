namespace Kubernator.Web.Jobs;

public interface IJobManager
{
    JobRecord Enqueue<TPayload>(string kind, TPayload payload, string? keyId = null, string? keyName = null);
    JobRecord? Get(string id);
    IReadOnlyList<JobRecord> List(int limit = 100);
    bool Cancel(string id);
}
