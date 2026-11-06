namespace Umbraco.Community.Automate.Jira.Tests;

public class BuildSwitchTests
{
    // The AUTOMATE_n constant and the referenced Automate major must agree, or a line would silently
    // compile against the wrong dependency set.
    [Fact]
    public void Constant_MatchesReferencedAutomateMajor()
    {
        var referenced = typeof(Umbraco.Automate.Core.Actions.ActionBase<,>).Assembly.GetName().Version!.Major;

#if AUTOMATE_17
        Assert.Equal(17, referenced);
#elif AUTOMATE_18
        Assert.Equal(18, referenced);
#else
        Assert.Fail("Neither AUTOMATE_17 nor AUTOMATE_18 is defined.");
#endif
    }
}
