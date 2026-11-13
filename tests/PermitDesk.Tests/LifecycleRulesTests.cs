using PermitDesk.Api.Domain;

namespace PermitDesk.Tests;

public class LifecycleRulesTests
{
    [Fact]
    public void Draft_can_be_submitted()
    {
        Assert.True(LifecycleRules.CanTransition(ApplicationStates.Draft, ApplicationStates.Submitted));
    }

    [Fact]
    public void Denied_is_terminal()
    {
        Assert.True(LifecycleRules.IsTerminal(ApplicationStates.Denied));
    }
}
