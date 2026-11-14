using System.Collections.Generic;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A bag is a table of names. The name is the whole key: what a step writes is what the line that
/// reads it says, and a declaration says what kind the name holds rather than being a second thing
/// to keep in step with it.
/// </summary>
public class VariableBindingTests
{
    [Fact]
    public void Writing_a_name_makes_one_slot_for_it()
    {
        var bag = new VariableBag();
        bag.Set("total", Value.FromNumber(3));

        Assert.Equal(1, bag.Count);
        Assert.Equal(3d, bag.Values["total"].AsNumber());
    }

    [Fact]
    public void Writing_one_name_again_keeps_one_slot()
    {
        var bag = new VariableBag();
        bag.Set("total", Value.FromNumber(1));
        bag.Set("total", Value.FromNumber(2));

        Assert.Equal(1, bag.Count);
        Assert.Equal(2d, bag.Values["total"].AsNumber());
    }

    [Fact]
    public void Removing_a_name_takes_its_value_with_it()
    {
        var bag = new VariableBag();
        bag.Set("total", Value.FromNumber(1));

        Assert.True(bag.Remove("total"));
        Assert.False(bag.Contains("total"));
        Assert.Equal(0, bag.Count);
        Assert.Empty(bag.Values);
    }

    /// <summary>The snapshot is a copy: a watcher must not be able to change the table by writing to it.</summary>
    [Fact]
    public void The_values_a_watcher_reads_are_a_copy()
    {
        var bag = new VariableBag();
        bag.Set("total", Value.FromNumber(1));

        var seen = bag.Values;
        if (seen is IDictionary<string, Value> writable)
        {
            writable["total"] = Value.FromNumber(9);
        }

        Assert.Equal(1d, bag.Values["total"].AsNumber());
    }

    /// <summary>A declaration survives a clear, so a name the macro declared stays type-checked.</summary>
    [Fact]
    public void Clearing_a_declared_name_puts_the_kinds_empty_value_back()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Number);
        bag.Set("count", Value.FromNumber(7));

        Assert.True(bag.Clear("count"));
        Assert.True(bag.IsDeclared("count"));
        Assert.Equal(0d, bag.Values["count"].AsNumber());
        Assert.Throws<VariableTypeException>(() => bag.Set("count", Value.FromText("many")));
    }

    [Fact]
    public void Clearing_a_name_nothing_declared_takes_it_out_of_the_table()
    {
        var bag = new VariableBag();
        bag.Set("count", Value.FromNumber(7));

        Assert.True(bag.Clear("count"));
        Assert.False(bag.Contains("count"));
    }

    /// <summary>Renaming moves the value and the declaration; a change of case is a rename too.</summary>
    [Fact]
    public void Renaming_moves_the_value_and_the_declaration()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Number);
        bag.Set("count", Value.FromNumber(4));

        Assert.True(bag.Rename("count", "Count"));
        Assert.Equal(4d, bag.Values["Count"].AsNumber());
        Assert.True(bag.IsDeclared("Count"));
        Assert.Single(bag.Values);
    }

    [Fact]
    public void Renaming_onto_a_name_that_is_taken_changes_nothing()
    {
        var bag = new VariableBag();
        bag.Set("count", Value.FromNumber(1));
        bag.Set("other", Value.FromNumber(2));

        Assert.False(bag.Rename("count", "other"));
        Assert.Equal(1d, bag.Values["count"].AsNumber());
        Assert.Equal(2d, bag.Values["other"].AsNumber());
    }

    /// <summary>
    /// A block borrows a name for its own rounds. A name the macro already had comes back with the
    /// value and the kind it had; a name the block introduced is taken out again.
    /// </summary>
    [Fact]
    public void A_block_that_borrows_a_name_puts_it_back()
    {
        var bag = new VariableBag();
        bag.Declare("item", ValueKind.Text);
        bag.Set("item", Value.FromText("kept"));

        var mark = bag.Mark("item");
        bag.Set("item", Value.FromText("inner"));
        bag.Restore("item", mark);

        Assert.Equal("kept", bag.Values["item"].AsText());
        Assert.True(bag.IsDeclared("item"));

        var fresh = bag.Mark("round");
        bag.Set("round", Value.FromNumber(1));
        bag.Restore("round", fresh);

        Assert.False(bag.Contains("round"));
    }

    [Fact]
    public void A_declared_number_refuses_words()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Number);

        var refusal = Assert.Throws<VariableTypeException>(() =>
            bag.Set("count", Value.FromText("many")));

        Assert.Equal("count", refusal.Name);
        Assert.Equal(ValueKind.Number, refusal.Expected);
        Assert.Equal(ValueKind.Text, refusal.Actual);

        // The refused write left the name as empty as the declaration made it.
        Assert.True(bag.Values["count"].IsNull);
    }

    /// <summary>
    /// A line that reads as the declared kind is the kind. The call path already judges "3" a
    /// number, and a write that refused it would make the same value pass one gate and fail the
    /// next.
    /// </summary>
    [Fact]
    public void A_declared_name_takes_a_line_that_reads_as_its_kind()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Number);

        bag.Set("count", Value.FromText("3"));

        Assert.Equal(3d, bag.Values["count"].AsNumber());
    }

    [Fact]
    public void A_text_name_takes_every_kind()
    {
        var bag = new VariableBag();
        bag.Declare("note", ValueKind.Text);

        bag.Set("note", Value.FromNumber(3));
        bag.Set("note", Value.FromList([Value.FromText("a")]));

        Assert.True(bag.Values["note"].IsList);
    }

    /// <summary>Empty means "nothing was written here yet", which is not a value of the wrong kind.</summary>
    [Fact]
    public void Nothing_written_yet_is_allowed_for_every_kind()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Number);

        bag.Set("count", Value.Null);

        Assert.True(bag.Contains("count"));
    }

    /// <summary>
    /// Changing what a name holds puts the kind's empty value there rather than leaving a number
    /// where a list is now declared.
    /// </summary>
    [Fact]
    public void Declaring_another_kind_resets_what_did_not_fit()
    {
        var bag = new VariableBag();
        bag.Declare("rows", ValueKind.Number);
        bag.Set("rows", Value.FromNumber(2));

        bag.Declare("rows", ValueKind.List);

        Assert.True(bag.Values["rows"].IsList);
        Assert.Empty(bag.Values["rows"].Items);
    }

    /// <summary>Declaring the same kind again keeps the value the run has already written.</summary>
    [Fact]
    public void Declaring_the_same_kind_again_keeps_the_value()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Number);
        bag.Set("count", Value.FromNumber(2));

        bag.Declare("count", ValueKind.Number);

        Assert.Equal(2d, bag.Values["count"].AsNumber());
    }

    /// <summary>A value that can stand for the new kind is kept rather than thrown away.</summary>
    [Fact]
    public void Declaring_another_kind_keeps_a_value_that_still_fits()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Text);
        bag.Set("count", Value.FromText("3"));

        bag.Declare("count", ValueKind.Number);

        Assert.Equal(3d, bag.Values["count"].AsNumber());
    }

    /// <summary>A declaration says what may be written; it is not a value, so seeding fills it.</summary>
    [Fact]
    public void A_declaration_alone_leaves_room_for_a_starting_value()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Number);

        Assert.True(bag.SetIfAbsent("count", Value.FromNumber(3)));
        Assert.Equal(3d, bag.Values["count"].AsNumber());
    }

    /// <summary>A run that wrote the empty value wrote an answer, and seeding must not replace it.</summary>
    [Fact]
    public void The_empty_value_a_run_wrote_is_still_written()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Number);
        bag.SetIfAbsent("count", Value.FromNumber(3));

        bag.Set("count", Value.Null);

        Assert.False(bag.SetIfAbsent("count", Value.FromNumber(3)));
        Assert.True(bag.Values["count"].IsNull);
    }

    /// <summary>A value the new kind cannot hold stops being a value, so the default may be written.</summary>
    [Fact]
    public void Declaring_another_kind_makes_room_for_the_declared_start_again()
    {
        var bag = new VariableBag();
        bag.Declare("rows", ValueKind.Number);
        bag.SetIfAbsent("rows", Value.FromNumber(2));

        bag.Declare("rows", ValueKind.List);

        Assert.True(bag.SetIfAbsent("rows", Value.FromList([Value.FromText("a")])));
        Assert.Single(bag.Values["rows"].Items);
    }

    /// <summary>
    /// A name around which one caller left spaces is the name: the table stores one spelling, so a
    /// declared name and a written name cannot end up as two entries that shadow each other.
    /// </summary>
    [Fact]
    public void A_name_is_stored_without_the_space_around_it()
    {
        var bag = new VariableBag();
        bag.Declare(" count ", ValueKind.Number);
        bag.Set(" count ", Value.FromNumber(3));

        Assert.Equal(1, bag.Count);
        Assert.True(bag.IsDeclared("count"));
        Assert.True(bag.TryGet("count", out var value));
        Assert.Equal(3d, value.AsNumber());

        Assert.True(bag.Rename(" count ", " total "));
        Assert.True(bag.Contains("total"));
        Assert.False(bag.Contains("count"));
    }

    [Fact]
    public void A_declaration_is_forgotten_when_the_run_starts_over()
    {
        var bag = new VariableBag();
        bag.Declare("count", ValueKind.Number);

        bag.ResetDeclarations();

        Assert.False(bag.IsDeclared("count"));
        bag.Set("count", Value.FromText("words"));
        Assert.Equal("words", bag.Values["count"].AsText());
    }
}
