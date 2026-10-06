namespace Viktor.Tests;

/// <summary>
/// How the tests themselves run, rather than what the product does: a case whose assertions are
/// dropped is worse than no case, because it reports a green tick for something nobody checked.
/// </summary>
public class UiTests
{
    [Fact]
    public void A_failed_assertion_inside_ui_run_comes_back_out()
    {
        var carried = false;
        try
        {
            Ui.Run(() => Assert.Fail("this has to come back out"));
        }
        catch (Exception)
        {
            carried = true;
        }

        // The interface thread hands the exception back, so xunit sees a failure rather than a
        // case that quietly did nothing. An earlier version of Ui.Run took the dispatch overload
        // that drops it, and a dozen interface cases passed while their assertions were failing.
        Assert.True(carried);
    }
}
