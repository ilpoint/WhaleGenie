using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

public class FlowStructureTests
{
    [Fact]
    public void Every_catalogued_container_exposes_its_declared_child_lists()
    {
        Ui.Run(() =>
        {
            var containers = ActionCatalog.Definitions
                .Where(definition => definition.Parameters.Any(parameter =>
                    parameter.Kind is ActionParameterKind.Steps))
                .ToList();

            Assert.NotEmpty(containers);
            foreach (var definition in containers)
            {
                var step = new MacroStep { Type = definition.Key };

                step.EnsureDeclaredLists();

                Assert.Equal(
                    definition.Parameters
                        .Where(parameter => parameter.Kind is ActionParameterKind.Steps)
                        .Select(parameter => parameter.Name),
                    step.StepLists.Select(list => list.Name));
                Assert.True(step.IsExpanded);
                Assert.All(step.StepLists, list => Assert.Empty(list.Steps));
            }
        });
    }

    [Fact]
    public void A_condition_only_flow_action_has_no_step_list()
    {
        Ui.Run(() =>
        {
            var step = new MacroStep { Type = "control.waitUntil" };

            step.EnsureDeclaredLists();

            Assert.Empty(step.StepLists);
        });
    }
}
