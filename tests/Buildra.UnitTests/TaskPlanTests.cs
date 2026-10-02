using Buildra.Application.Planning;
namespace Buildra.UnitTests;
public sealed class TaskPlanTests
{
    [Fact]
    public void CannotCreateReadyTaskWithoutAcceptanceCriteria() => Assert.Throws<InvalidOperationException>(() => TaskPlan.Parse("""{"title":"Search","description":"Add search","acceptanceCriteria":[],"summary":"Plan ready","needsClarification":false}"""));
    [Fact]
    public void ClarificationCanOmitImplementationScope()
    {
        var plan = TaskPlan.Parse("""{"title":"","description":"","acceptanceCriteria":[],"summary":"Which page needs search?","needsClarification":true}""");
        Assert.True(plan.NeedsClarification);
    }
    [Fact]
    public void DoesNotAcceptNullCriteria() => Assert.Throws<InvalidOperationException>(() => TaskPlan.Parse("""{"title":"Search","description":"Add search","acceptanceCriteria":null,"summary":"Plan ready","needsClarification":false}"""));
}
