using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// A macro's declaration is read twice: once by the engine, which needs the shape a run works with,
/// and once by the call form and the tools, which need the shape a person writing a call works with.
/// Two readings of one declaration are two chances to say different things, so what they say about
/// the same macro is compared here rather than left to whoever changes one of them to remember the
/// other.
/// </summary>
public class CallableSignatureTests
{
    private static MacroCallable Project() => new(
        "整理",
        [
            new MacroParameter
            {
                Name = " 次数 ",
                Default = "3",
                Required = false,
                Type = ActionReturnType.Number,
                Description = "要做多少遍。",
            },
            new MacroParameter { Name = "标签", Required = true },
            new MacroParameter { Name = string.Empty, Required = true },
        ],
        [
            new MacroResult
            {
                Name = "答案",
                Type = ActionReturnType.Number,
                Description = "整理完留下的数。",
            },
            new MacroResult { Name = " " },
        ]);

    [Fact]
    public void The_call_form_and_the_engine_read_one_declaration_the_same_way()
    {
        var call = Project();
        var form = CallableSignature.Arguments(call);
        var engine = call.Signature.Arguments;

        // An argument row with no name is not something a call can fill in, so the form does not
        // show one; the engine keeps it, because it is what refuses the macro as a declaration with
        // an empty name. Both facts are true at once, and this is where that is written down.
        Assert.Equal(["次数", "标签"], form.Select(argument => argument.Name));
        var named = engine.Where(argument => argument.Name.Trim().Length > 0).ToList();
        Assert.Equal(named.Select(argument => argument.Name.Trim()), form.Select(argument => argument.Name));

        Assert.Equal(named.Select(argument => argument.Type), form.Select(argument => argument.Type));
        Assert.Equal(named.Select(argument => argument.Required), form.Select(argument => argument.Required));
        Assert.Equal(named.Select(argument => argument.Default), form.Select(argument => argument.Default));
    }

    [Fact]
    public void One_argument_is_read_by_name_whatever_the_case_of_the_call()
    {
        var call = Project();

        var found = CallableSignature.Argument(call, " 次数 ");

        Assert.NotNull(found);
        Assert.Equal("次数", found!.Name);
        Assert.Null(CallableSignature.Argument(call, "次数之外的"));
    }

    [Fact]
    public void What_the_row_says_under_it_depends_on_what_the_declaration_promises()
    {
        var arguments = CallableSignature.Arguments(Project());
        var optional = arguments[0];
        var required = arguments[1];

        // The default is named rather than written into the row: it belongs to the declaration, so
        // a changed default reaches every call that never filled this argument in.
        Assert.Contains("要做多少遍。", optional.Hint, System.StringComparison.Ordinal);
        Assert.Contains("3", optional.Hint, System.StringComparison.Ordinal);
        Assert.Contains(Strings.Get("ActionType.Number"), optional.Hint, System.StringComparison.Ordinal);

        Assert.DoesNotContain(Strings.Get("Add.CallArgumentDefault"), required.Hint,
            System.StringComparison.Ordinal);
        Assert.Contains(Strings.Get("Add.CallArgumentRequired"), required.Hint,
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void The_results_page_is_read_the_same_way_as_the_run()
    {
        var call = Project();
        var page = CallableSignature.Results(call);

        // A result row with no name is offered to nobody, so the page leaves it out while the engine
        // still sees it and refuses the declaration.
        Assert.Equal(["答案"], page.Select(result => result.Name));
        Assert.Equal(2, call.Signature.Results.Count);
        Assert.Equal(ActionReturnType.Number, page[0].Type);
        Assert.Contains("整理完留下的数。", page[0].Hint, System.StringComparison.Ordinal);
    }
}
