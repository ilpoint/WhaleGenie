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

// Every number says what it counts, and what an empty field means.
public partial class ActionContractTests
{
    /// <summary>
    /// Every number says what it counts and what a blank cell comes to, and the unit is what puts
    /// the box's limits there: a coordinate can be negative, a length of time starts at zero, and
    /// no pixel field borrows the longest pause as its ceiling. A field that its action refuses
    /// beyond a narrower limit says that limit itself.
    /// </summary>
    [Fact]
    public void Every_number_says_what_it_counts_and_what_an_empty_cell_means()
    {
        var problems = new List<string>();

        foreach (var action in ActionSemanticsCatalog.All)
        {
            foreach (var parameter in action.Parameters)
            {
                var field = $"{action.Key}.{parameter.Name}";
                if (parameter.Shape != ActionValueShape.Number)
                {
                    if (parameter.Unit is not null || parameter.EmptyMeans is not null)
                    {
                        problems.Add($"{field}: a field that is not a number declares a number fact");
                    }

                    continue;
                }

                if (parameter.Unit is null)
                {
                    problems.Add($"{field}: no unit");
                }

                if (parameter.EmptyMeans is null)
                {
                    problems.Add($"{field}: no empty meaning");
                }

                if (parameter.MinimumValue > parameter.MaximumValue)
                {
                    problems.Add($"{field}: its range is empty");
                }

                switch (parameter.Unit)
                {
                    case ActionUnit.Pixels:
                        if (parameter.MaximumValue == StepMeta.LongestPauseMs)
                        {
                            problems.Add($"{field}: a pixel field stops at the longest pause");
                        }

                        break;
                    case ActionUnit.Milliseconds:
                        if (parameter.MinimumValue < 0m)
                        {
                            problems.Add($"{field}: a length of time starts below zero");
                        }

                        break;
                    case ActionUnit.Percent:
                        if (parameter.MinimumValue < 0m || parameter.MaximumValue > 100m)
                        {
                            problems.Add($"{field}: a share is outside 0 to 100");
                        }

                        break;
                    case ActionUnit.Axis:
                        if (parameter.MinimumValue != -100m || parameter.MaximumValue != 100m)
                        {
                            problems.Add($"{field}: a stick is outside -100 to 100");
                        }

                        break;
                }

                if (parameter.Name is "x" or "y" or "startX" or "startY" or "endX" or "endY"
                    && parameter.MinimumValue >= 0m)
                {
                    problems.Add($"{field}: a place cannot be negative");
                }

                if (parameter.Name is "width" or "height" && parameter.MinimumValue < 1m)
                {
                    problems.Add($"{field}: a size may be less than one pixel");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
