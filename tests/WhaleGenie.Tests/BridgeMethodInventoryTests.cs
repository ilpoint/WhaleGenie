using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie;
using WhaleGenie.Bridge;

namespace WhaleGenie.Tests;

/// <summary>
/// The page's commands and the program's registered methods are one list. A handler that is gone
/// from the program but still called by a page is a dead button; a handler the program added and
/// nobody calls is a door with no sign. The page may also use bridge.ready, which is the client
/// handshake and not a registered method.
/// </summary>
public class BridgeMethodInventoryTests
{
    private static readonly HashSet<string> Expected = new(StringComparer.Ordinal)
    {
        "app.info", "app.quit", "app.openExternal",
        "app.openSettings", "app.openRun", "app.openAbout", "app.openManual",
        "dialog.respond", "dialog.pickOpenPackage", "dialog.pickSavePackage", "dialog.pickImage",
        "editor.newMacro", "editor.open",
        "editor.sessionOpen", "editor.sessionTabs", "editor.sessionNew",
        "editor.sessionActivate",
        "editor.sessionState", "editor.sessionPatch",
        "editor.sessionStep", "editor.sessionStepEnabled", "editor.sessionStepMeta",
        "editor.sessionParameter",
        "editor.sessionParameterRowAdd",
        "editor.sessionParameterRowRemove", "editor.sessionParameterRowCell",
        "editor.sessionConditionRowAdd", "editor.sessionConditionGroupAdd",
        "editor.sessionConditionRowRemove",
        "editor.sessionConditionRow", "editor.sessionConditionJoin",
        "editor.sessionRecordStart", "editor.sessionRecordStop", "editor.sessionRecordState",
        "editor.sessionStepDuplicate",
        "editor.sessionUndo", "editor.sessionRedo",
        "editor.sessionMove",
        "editor.sessionRemove", "editor.sessionTriggerAdd", "editor.sessionTriggerRemove",
        "editor.sessionTriggerBinding", "editor.sessionTriggerLoop", "editor.sessionTriggerOnce",
        "editor.sessionTriggerEnabled", "editor.sessionTriggerTimer", "editor.sessionTriggerFile",
        "editor.sessionTriggerProcess", "editor.sessionTriggerHotstring",
        "editor.sessionTriggerColor", "editor.sessionTriggerIdle", "editor.sessionTriggerWindow",
        "editor.sessionTriggerInterface",
        "editor.sessionGlobalAdd", "editor.sessionGlobalPatch", "editor.sessionGlobalRemove",
        "editor.sessionVariableAdd", "editor.sessionVariablePatch",
        "editor.sessionVariableRemove", "editor.sessionSave", "editor.sessionClose",
        "editor.sessionActions",
        "mcp.state", "mcp.setGrants", "mcp.copyConfig", "mcp.openAudit",
        "project.state", "project.toggle", "project.delete", "project.export",
        "project.system", "project.open", "project.import", "project.save",
        "project.saveAs", "project.reload", "project.closeAll",
        "strings.bundle", "theme.get", "theme.set",
        "window.minimize", "window.maximize", "window.close", "window.pin",
        "window.drag", "window.focus",
    };

    [Fact]
    public void The_registered_methods_are_the_page_inventory()
    {
        using var services = WindowedServices.Build();
        var server = services.GetRequiredService<BridgeServer>();

        Assert.Equal(Expected.Order(StringComparer.Ordinal), server.Methods);
    }

    [Fact]
    public void The_page_calls_only_registered_methods()
    {
        var called = new HashSet<string>(StringComparer.Ordinal);
        var pattern = new Regex(@"\b(?:client|this|bridge\.value)\.call\(['""]([a-zA-Z.]+)['""]",
            RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(RepoPaths.Root, "webui", "src"), "*.ts", SearchOption.AllDirectories))
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(file)))
            {
                var method = match.Groups[1].Value;
                if (method != "bridge.ready")
                {
                    called.Add(method);
                }
            }
        }

        Assert.Empty(called.Except(Expected));
    }
}
