namespace Kubernator.Web.Jobs;

public interface IJobManager
{
    JobRecord Enqueue(JobSubmission submission);
    JobRecord? Get(string id);
    IReadOnlyList<JobRecord> List(int limit = 100);
    bool Cancel(string id);
}
