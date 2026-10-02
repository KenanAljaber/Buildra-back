using Buildra.Domain.Tasks;
using Status = Buildra.Domain.Tasks.TaskStatus;
namespace Buildra.UnitTests;

public sealed class WorkflowTests
{
    [Fact]
    public void ReviewRejectionReturnsToDeveloperBeforeApproval()
    {
        var task = new DevelopmentTask { AcceptanceCriteria = "Tests pass" };
        foreach (var status in new[] { Status.Ready, Status.InDevelopment, Status.InReview, Status.ChangesRequested, Status.InDevelopment, Status.InReview, Status.Approved })
            task.TransitionTo(status);
        task.PullRequestUrl = "https://github.com/owner/repo/pull/1";
        task.TransitionTo(Status.Completed);
        Assert.Equal(Status.Completed, task.Status);
        Assert.Throws<InvalidOperationException>(() => task.TransitionTo(Status.Ready));
    }
    [Fact]
    public void CannotSkipReview() => Assert.Throws<InvalidOperationException>(() => new DevelopmentTask().TransitionTo(Status.Approved));
    [Fact]
    public void CannotStartWithoutAcceptanceCriteria() => Assert.Throws<InvalidOperationException>(() => new DevelopmentTask().TransitionTo(Status.Ready));
    [Fact]
    public void CannotCompleteWithoutPullRequest()
    {
        var task = new DevelopmentTask { AcceptanceCriteria = "Works" };
        foreach (var status in new[] { Status.Ready, Status.InDevelopment, Status.InReview, Status.Approved }) task.TransitionTo(status);
        Assert.Throws<InvalidOperationException>(() => task.TransitionTo(Status.Completed));
    }
}
