using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Tests;

// The two catalogues against each other: the same actions, and fields that follow real siblings.
public partial class ActionContractTests
{
    [Fact]
    public void The_two_catalogues_hold_the_same_actions()
    {
        var displayed = Ui.Run(() => ActionCatalog.Definitions.Select(action => action.Key).ToList());
        var engine = ActionSemanticsCatalog.All.Select(action => action.Key).ToList();

        Assert.Equal(engine, displayed);
        Assert.Contains("control.case", engine);
        Assert.True(ActionSemanticsCatalog.Find("control.case")!.Hidden);
    }

    /// <summary>
    /// A field that says it follows another field is only a question while that other field is on
    /// the same action. A rename that leaves the follower pointing at the old name makes the field
    /// disappear without any error, so every reference and every value it names is checked here.
    /// </summary>
    [Fact]
    public void A_displayed_field_only_follows_a_real_sibling()
    {
        var problems = new List<string>();

        Ui.Run(() =>
        {
            foreach (var action in ActionCatalog.Definitions)
            {
                foreach (var parameter in action.Parameters)
                {
                    Check(action, parameter, parameter.Name, "AppliesWhen",
                        parameter.AppliesWhen, parameter.AppliesWhenValues, requireValues: true);
                    Check(action, parameter, parameter.Name, "AppliesWhenAlso",
                        parameter.AppliesWhenAlso, parameter.AppliesWhenAlsoValues, requireValues: true);
                    Check(action, parameter, parameter.Name, "EnabledBySibling",
                        parameter.EnabledBySibling, [], requireValues: false);
                }
            }
        });

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        void Check(ActionDefinition action, ActionParameter parameter, string field,
            string fact, string siblingName, IReadOnlyList<string> values, bool requireValues)
        {
            if (siblingName.Length == 0)
            {
                if (values.Count > 0)
                {
                    problems.Add($"{action.Key}.{field}: {fact}Values is set without a sibling");
                }

                return;
            }

            var sibling = action.Parameters.FirstOrDefault(candidate =>
                candidate.Name == siblingName);
            if (sibling is null)
            {
                problems.Add($"{action.Key}.{field}: {fact} names {siblingName}, which the action has not");
                return;
            }

            if (requireValues && values.Count == 0)
            {
                problems.Add($"{action.Key}.{field}: {fact} names {siblingName} but no values");
                return;
            }

            if (!requireValues)
            {
                return;
            }

            foreach (var value in values.Where(value => value != ActionParameter.AnyValue))
            {
                var valid = sibling.Shape switch
                {
                    _ when value.Length == 0 => true,
                    ActionValueShape.Choice => sibling.Options.Contains(value, StringComparer.Ordinal),
                    ActionValueShape.Flag => value is "true" or "false",
                    // A number or text field can be compared with any literal; the sibling only
                    // has to exist for the question to be asked.
                    _ => true,
                };
                if (!valid)
                {
                    problems.Add($"{action.Key}.{field}: {fact} expects {siblingName}={value}, "
                        + "which that field cannot be");
                }
            }
        }
    }
}
