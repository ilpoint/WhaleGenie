using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// 示例宏是"手上什么都没有"的人第一眼看到的东西，所以它得自己站得住：每一步都点得出真的动作、
/// 每一格都写的是那个动作真有的参数，宏里的检查一条问题都不报，被调用的那个宏在主列表里。
/// 把它改坏了，等于把新人的第一课改坏。
/// </summary>
public class SampleMacroTests
{
    /// <summary>
    /// 动作名与参数名都对着动作目录核一遍：写错一个名字，用户拿到的是一个打不开的宏，而这一点在
    /// 界面上要等到编辑那一步才看得见。
    /// </summary>
    [Fact]
    public void The_sample_names_actions_and_fields_that_are_really_there()
    {
        Ui.Run(() =>
        {
            var unknown = new List<string>();

            foreach (var step in Steps())
            {
                if (ActionCatalog.Find(step.Type) is not { } action)
                {
                    unknown.Add(step.Type);
                    continue;
                }

                foreach (var parameter in step.Parameters)
                {
                    // 一个调用交给被调用宏的那些值，名字由那个宏声明，动作目录里自然没有它们。
                    if (parameter.Name.StartsWith(MacroCallFields.ArgumentPrefix,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!action.Parameters.Any(one => one.Name == parameter.Name))
                    {
                        unknown.Add($"{step.Type}.{parameter.Name}");
                    }
                }
            }

            Assert.Empty(unknown);
        });
    }

    /// <summary>
    /// 插进列表就能跑，说的是它自己站得住：调用找得到被调用的子宏，读的名字都是自己写出来的，块
    /// 与条件里的步骤也算在内。编辑器写宏时会指着这些地方，示例更不该一进列表就被标上问题。
    /// </summary>
    [Fact]
    public void The_sample_has_nothing_the_editor_would_mark_as_wrong()
    {
        Ui.Run(() =>
        {
            var macros = SampleMacro.All();
            var callables = macros.Select(macro => macro.Callable).ToList();

            foreach (var macro in macros)
            {
                Assert.Empty(MacroCheck.Inspect(macro.Steps, macro.Callable, callables));
            }
        });
    }

    /// <summary>
    /// 主示例与它调用的子宏一起进列表，两边都有步骤，每一步都有自己的标识：断点、运行高亮与步骤结果
    /// 都按这四个字符认人，两个宏里的步骤共用一个名字会让那个报告指错行。
    /// </summary>
    [Fact]
    public void Inserting_the_sample_leaves_something_to_run_in_the_list()
    {
        Ui.Run(() =>
        {
            var viewModel = new MainViewModel();
            foreach (var macro in SampleMacro.All())
            {
                viewModel.AddMacro(macro);
            }

            Assert.True(viewModel.HasMacros);
            Assert.All(viewModel.Macros, macro => Assert.NotEmpty(macro.Steps));

            var ids = Steps().Select(step => step.Id).ToList();
            Assert.All(ids, id => Assert.Equal(4, id.Length));
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        });
    }

    /// <summary>Every step of the sample, the ones inside blocks and conditions included.</summary>
    private static IEnumerable<MacroStep> Steps()
        => SampleMacro.All().SelectMany(macro => Every(macro.Steps));

    private static IEnumerable<MacroStep> Every(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;

            foreach (var parameter in step.Parameters)
            {
                foreach (var nested in Every(parameter.Steps))
                {
                    yield return nested;
                }

                if (parameter.Condition is { } condition)
                {
                    foreach (var nested in Every([condition]))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }
}
