using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

public class PersistentVariableTests
{
    [Fact]
    public void Renaming_a_global_moves_its_saved_value_with_the_name()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "persistent-variables.json");
        using var scoped = PersistentVariables.UsePathForTests(path);
        var previousGlobals = VariableCatalog.Globals.ToList();
        var previousValues = SharedVariables.Current.Values;
        VariableCatalog.Globals.Clear();
        SharedVariables.Current.Clear();
        var definition = new VariableDefinition
        {
            Name = "oldTotal",
            Scope = VariableScope.Global,
            Type = "number",
            PersistValue = true,
        };
        VariableCatalog.Globals.Add(definition);
        SharedVariables.Current.SetNumber("oldTotal", 8);
        PersistentVariables.Flush();
        try
        {
            VariableCatalog.Globals.Remove(definition);
            PersistentVariables.Rename("oldTotal", "newTotal");
            SharedVariables.Current.Rename("oldTotal", "newTotal");
            var renamed = new VariableDefinition
            {
                Name = "newTotal",
                Scope = VariableScope.Global,
                Type = "number",
            };
            VariableCatalog.Globals.Add(renamed);
            PersistentVariables.ApplyTo([renamed]);

            Assert.True(SharedVariables.Current.TryGet("newTotal", out var value));
            Assert.Equal(8, value.AsNumber());
            Assert.False(SharedVariables.Current.Contains("oldTotal"));
        }
        finally
        {
            VariableCatalog.Globals.Clear();
            foreach (var global in previousGlobals)
            {
                VariableCatalog.Globals.Add(global);
            }

            SharedVariables.Current.Clear();
            foreach (var (name, value) in previousValues)
            {
                SharedVariables.Current.Set(name, value);
            }
        }
    }

    [Fact]
    public void A_checked_global_is_restored_from_the_users_file_and_unchecking_removes_it()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "persistent-variables.json");
        using var scoped = PersistentVariables.UsePathForTests(path);

        var previousGlobals = VariableCatalog.Globals.ToList();
        var previousValues = SharedVariables.Current.Values;
        VariableCatalog.Globals.Clear();
        SharedVariables.Current.Clear();
        try
        {
            var definition = new VariableDefinition
            {
                Name = "total",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "1",
            };
            VariableCatalog.Globals.Add(definition);
            definition.PersistValue = true;
            SharedVariables.Current.SetNumber("total", 5);
            SharedVariables.Current.SetNumber("total", 7);
            PersistentVariables.Flush();

            Assert.True(File.Exists(path));
            VariableCatalog.Globals.Clear();
            SharedVariables.Current.Clear();

            var reopened = new VariableDefinition
            {
                Name = "total",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "1",
            };
            VariableCatalog.Globals.Add(reopened);
            PersistentVariables.ApplyTo([reopened]);

            Assert.True(reopened.PersistValue);
            Assert.True(SharedVariables.Current.TryGet("total", out var restored));
            Assert.Equal(7, restored.AsNumber());

            reopened.PersistValue = false;
            PersistentVariables.Flush();
            Assert.DoesNotContain("total", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            VariableCatalog.Globals.Clear();
            foreach (var variable in previousGlobals)
            {
                VariableCatalog.Globals.Add(variable);
            }

            SharedVariables.Current.Clear();
            foreach (var (name, value) in previousValues)
            {
                SharedVariables.Current.Set(name, value);
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void A_bad_saved_value_falls_back_to_the_project_default()
    {
        var previousValues = SharedVariables.Current.Values;
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "persistent-variables.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "variables": [
                { "name": "amount", "value": "not a number" }
              ]
            }
            """);
        using var scoped = PersistentVariables.UsePathForTests(path);
        var definition = new VariableDefinition
        {
            Name = "amount",
            Scope = VariableScope.Global,
            Type = "number",
            DefaultValue = "3",
        };

        try
        {
            PersistentVariables.ApplyTo([definition]);

            Assert.True(definition.PersistValue);
            Assert.True(SharedVariables.Current.TryGet("amount", out var value));
            Assert.Equal(3, value.AsNumber());
        }
        finally
        {
            PersistentVariables.Flush();
            SharedVariables.Current.Clear();
            foreach (var (name, value) in previousValues)
            {
                SharedVariables.Current.Set(name, value);
            }

            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void A_new_global_is_not_persistent_unless_it_is_checked()
    {
        var definition = new VariableDefinition
        {
            Name = "ordinary",
            Scope = VariableScope.Global,
            Type = "text",
        };

        Assert.False(definition.PersistValue);
    }
}
