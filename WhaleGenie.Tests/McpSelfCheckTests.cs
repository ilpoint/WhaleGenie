using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The self-check is the ruler the MCP surface is judged by: it is run against a real loopback
/// server, through the same official client an outside AI would use, and it must not throw when
/// the server is broken. These checks keep the ruler honest; the numbers it reports are meant to be
/// read by a person comparing two builds, not asserted here.
/// </summary>
public class McpSelfCheckTests : IDisposable
{
    /// <summary>The two switches, standing in for the MCP flyout's settings file.</summary>
    private readonly McpGate _gate = new();

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// The check that keeps prose honest reads tool names out of it. A resource address is not one,
    /// and a name nobody registered is what it is there to catch.
    /// </summary>
    [Fact]
    public void A_tool_name_in_the_prose_has_to_be_a_tool_that_exists()
    {
        var named = McpSelfCheck.NamedTools(
        [
            "Call whalegenie_list_macros, then whalegenie_put_macro. The project page is whalegenie://project.",
            "Read one step with whalegenie_get_step and change it with whalegenie_patch_macro_x.",
        ]);

        Assert.Contains("whalegenie_list_macros", named);
        Assert.Contains("whalegenie_get_step", named);
        Assert.DoesNotContain("whalegenie", named);
        Assert.Contains("whalegenie_patch_macro_x", named);
    }

    [Fact]
    public void The_self_check_closes_the_loop_and_reports_its_cost()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Scratch();
            try
            {
                var path = Path.Combine(folder, "project.wgmacro");
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "原宏" });
                viewModel.CurrentPath = path;
                viewModel.SavePackage(path);

                await using var loop = McpLoopback.Start(viewModel);
                await using var client = await loop.ConnectAsync();

                // Writing the project is behind the control switch, as it is for a client: a
                // check that was not allowed to write would skip the two rows it is here for.
                _gate.Control = true;

                var report = await new McpSelfCheck(client, new McpSelfCheckOptions
                {
                    Where = loop.Label,
                    AllowApply = true,
                    AllowSave = true,
                }).RunAsync();

                Save(report);

                foreach (var name in new[]
                         {
                             "loop.status", "loop.open", "loop.put", "loop.validate",
                             "loop.apply", "loop.save",
                         })
                {
                    var check = report.Checks.Single(entry => entry.Name == name);
                    Assert.True(check.Result == McpCheckResult.Pass, $"{name}: {check.Result} — {check.Detail}");
                }

                // The scenario ends with a deliberate second apply, so nine is what the same script
                // costs now; below five the scenario plainly did not run.
                Assert.InRange(report.Metrics["scenario.calls"], 5, 9);

                // Changing one number in a forty-step macro must cost a fraction of sending it
                // again; that ratio is the whole reason the patch tool exists.
                Assert.True(report.Metrics["patch.saving"] >= 5,
                    $"one parameter cost 1/{report.Metrics["patch.saving"]:0.##} of the whole macro");
                Assert.All(
                    report.Checks.Where(check => check.Name.StartsWith("patch.", StringComparison.Ordinal)),
                    check => Assert.True(check.Result == McpCheckResult.Pass,
                        $"{check.Name}: {check.Result} — {check.Detail}"));
                Assert.True(report.Metrics["scenario.responseBytes"] > 0);
                Assert.Contains("| check | result | detail |", report.ToMarkdown(), StringComparison.Ordinal);
                Assert.Contains("| metric | value |", report.ToMarkdown(), StringComparison.Ordinal);

                await loop.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    [Fact]
    public void The_self_check_says_which_wrong_macro_the_server_caught()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Scratch();
            try
            {
                var path = Path.Combine(folder, "project.wgmacro");
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "原宏" });
                viewModel.CurrentPath = path;
                viewModel.SavePackage(path);

                await using var loop = McpLoopback.Start(viewModel);
                await using var client = await loop.ConnectAsync();

                // The six probes need the apply path, because one of the six is a second apply.
                _gate.Control = true;

                var report = await new McpSelfCheck(client, new McpSelfCheckOptions
                {
                    Where = loop.Label,
                    AllowApply = true,
                }).RunAsync();

                foreach (var name in new[]
                         {
                             "reject.action_key", "reject.child_keys", "reject.record_field",
                             "reject.enum_value", "reject.bad_json", "reject.double_apply",
                         })
                {
                    var check = report.Checks.Single(entry => entry.Name == name);
                    Assert.True(check.Result != McpCheckResult.Fail, $"{name}: {check.Detail}");
                }

                await loop.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    /// <summary>
    /// A server started with the two permissions open is not a server with a hole in it. The check
    /// has to read what the server says it allows and expect that, or it reports a failure that is
    /// not one — and the read-only probe it would otherwise use is a real mouse move, which an
    /// allowed server carries out while the report calls it broken.
    /// </summary>
    [Fact]
    public void The_self_check_expects_the_permissions_the_server_reports()
    {
        Ui.RunAsync(async () =>
        {
            var folder = Scratch();
            var control = _gate.Control;
            var runs = _gate.Run;
            try
            {
                var path = Path.Combine(folder, "project.wgmacro");
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "原宏" });
                viewModel.CurrentPath = path;
                viewModel.SavePackage(path);

                _gate.Control = true;
                _gate.Run = true;

                await using var loop = McpLoopback.Start(viewModel);
                await using var client = await loop.ConnectAsync();

                // Writing the project is behind the control switch, as it is for a client: a
                // check that was not allowed to write would skip the two rows it is here for.
                _gate.Control = true;

                var report = await new McpSelfCheck(client, new McpSelfCheckOptions
                {
                    Where = loop.Label,
                    AllowApply = true,
                    AllowSave = true,
                }).RunAsync();

                Assert.True(report.Passed, string.Join("; ", report.Checks
                    .Where(check => check.Result == McpCheckResult.Fail)
                    .Select(check => $"{check.Name}: {check.Detail}")));

                foreach (var name in new[] { "gate.control_on", "gate.run_on" })
                {
                    var check = report.Checks.Single(entry => entry.Name == name);
                    Assert.True(check.Result == McpCheckResult.Pass, $"{name}: {check.Detail}");
                }

                // And the read-only pair is not reported against a server that is not read-only.
                Assert.DoesNotContain(report.Checks,
                    check => check.Name.EndsWith("_off", StringComparison.Ordinal));

                await loop.StopAsync();
            }
            finally
            {
                _gate.Control = control;
                _gate.Run = runs;
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    /// <summary>
    /// The one goodbye a stdio server gets is the client closing its end of the pipe. That has to end
    /// the session rather than leave the host waiting: this run exists only for that client.
    /// </summary>
    [Fact]
    public void Closing_the_pipe_ends_the_session()
    {
        Ui.RunAsync(async () =>
        {
            var loop = McpLoopback.Start(new MainViewModel());
            await using var client = await loop.ConnectAsync();

            loop.CloseClientEnd();
            var ended = loop.Host.WaitForShutdownAsync();
            var finished = await Task.WhenAny(ended, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(ended, finished);
            await loop.DisposeAsync();
            return true;
        });
    }

    private static string Scratch()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-selfcheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Writes the report where a run asked for it, so two builds can be compared. Only
    /// written when asked: a check that silently leaves files behind is a check that gets deleted.</summary>
    private static void Save(McpSelfCheckReport report)
    {
        if (Environment.GetEnvironmentVariable("MCP_SELFCHECK_REPORT") is not { Length: > 0 } path)
        {
            return;
        }

        File.WriteAllText(path, report.ToMarkdown(), new UTF8Encoding(false));
    }
}
