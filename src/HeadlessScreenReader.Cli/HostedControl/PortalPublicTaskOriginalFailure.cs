using HeadlessScreenReader.Domain;

namespace HeadlessScreenReader.Cli.HostedControl;

internal sealed record PortalTaskOriginalFailure(Guid JobId, string Stage, string File, string Sha256, int Bytes, Guid? ExpectedExecutionId);

internal sealed partial class PortalPublicTasks
{
    private static PortalFault OriginalUnavailable(PublicTaskStatus status, PublicTaskFile? file = null)
    {
        var expected = file ?? new PublicTaskFile("report.json", status.Publication!.ReportSha256, status.Publication.ReportBytes, "application/json");
        var fault = new PortalFault(503, "CONTROL_PUBLIC_TASK_ORIGINAL_UNAVAILABLE");
        fault.Data[typeof(PortalTaskOriginalFailure)] = new PortalTaskOriginalFailure(status.Job.JobId,
            file is null ? "ReportVerification" : "SupportingOriginalVerification", expected.Path, expected.Sha256, expected.Bytes,
            status.Publication?.ExecutionId);
        return fault;
    }
}
