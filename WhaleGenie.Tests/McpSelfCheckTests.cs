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
public class McpSelfCheckTests
{
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

                await using var host = new McpServerHost(viewModel);
                await host.StartAsync();
                await using var client = await ConnectAsync(host.Endpoint);

                var report = await new McpSelfCheck(client, new McpSelfCheckOptions
                {
                    Endpoint = host.Endpoint,
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

                await host.StopAsync();
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

                await using var host = new McpServerHost(viewModel);
                await host.StartAsync();
                await using var client = await ConnectAsync(host.Endpoint);

                // The six probes need the apply path, because one of the six is a second apply.
                var report = await new McpSelfCheck(client, new McpSelfCheckOptions
                {
                    Endpoint = host.Endpoint,
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

                await host.StopAsync();
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            return true;
        });
    }

    [Fact]
    public void The_self_check_reports_a_server_that_is_gone_instead_of_throwing()
    {
        Ui.RunAsync(async () =>
        {
            await using var host = new McpServerHost(new MainViewModel());
            await host.StartAsync();
            await using var client = await ConnectAsync(host.Endpoint);
            var endpoint = host.Endpoint;
            await host.StopAsync();

            var report = await new McpSelfCheck(client, new McpSelfCheckOptions
            {
                Endpoint = endpoint,
                CallTimeout = TimeSpan.FromSeconds(5),
            }).RunAsync();

            Assert.False(report.Passed);
            Assert.NotEmpty(report.Checks);
            Assert.All(report.Checks, check => Assert.False(string.IsNullOrWhiteSpace(check.Detail)));
            Assert.Contains("fail", report.ToMarkdown(), StringComparison.Ordinal);
            return true;
        });
    }

    private static async Task<McpClient> ConnectAsync(string endpoint)
        => await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
        }));

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
