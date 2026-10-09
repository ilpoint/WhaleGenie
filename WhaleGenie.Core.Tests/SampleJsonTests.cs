using System.Threading.Tasks;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// JSON as an API hands it back: nested objects with lists of objects inside them, which is where a
/// path that can be read has to be a path that can be written. See <see cref="SampleFiles"/>.
/// </summary>
public class SampleJsonTests
{
    [RealSampleFact("sample-1.json", "sample-2.json")]
    public async Task Values_come_out_of_json_however_deeply_they_sit()
    {
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.name"), SampleFiles.Param("resultVariable", "shop")),
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.products[0].price"),
                SampleFiles.Param("resultVariable", "price")),
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.products[1].shipping.express.cost"),
                SampleFiles.Param("resultVariable", "express")),
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.products[0].details.features[2]"),
                SampleFiles.Param("resultVariable", "lastFeature")),
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-2.json"),
                SampleFiles.Param("query", "users[1].name"),
                SampleFiles.Param("resultVariable", "second")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);
        Assert.Equal("Sample.Cat Store", run.Value("shop").AsText());
        Assert.Equal(29.99, run.Value("price").AsNumber(), 2);
        Assert.Equal(15.99, run.Value("express").AsNumber(), 2);
        Assert.Equal("Long-lasting battery", run.Value("lastFeature").AsText());
        Assert.Equal("Jane Smith", run.Value("second").AsText());
    }

    [RealSampleFact("sample-1.json")]
    public async Task A_value_inside_a_json_list_can_be_changed()
    {
        // A setting inside a list — the price of the second product — is the everyday case for
        // changing a file like this, and a path that can be read but not written leaves the macro
        // author reading a file they cannot change.
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("file.writeJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.products[0].price"),
                SampleFiles.Param("value", "25.99")),
            SampleFiles.Step("file.writeJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.products[1].shipping.express.deliveryTime"),
                SampleFiles.Param("value", "same day")),
            SampleFiles.Step("file.writeJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.contact.email"),
                SampleFiles.Param("value", "hello@sample.cat")),
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.products[0].price"),
                SampleFiles.Param("resultVariable", "price")),
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.products[1].shipping.express.deliveryTime"),
                SampleFiles.Param("resultVariable", "when")),
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.name"), SampleFiles.Param("resultVariable", "shop")),
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.contact.email"),
                SampleFiles.Param("resultVariable", "email")),
            SampleFiles.Step("file.readJson", SampleFiles.Param("path", "sample-1.json"),
                SampleFiles.Param("query", "store.products[1].variants[1].color"),
                SampleFiles.Param("resultVariable", "colour")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);
        Assert.Equal(25.99, run.Value("price").AsNumber(), 2);
        Assert.Equal("same day", run.Value("when").AsText());
        Assert.Equal("hello@sample.cat", run.Value("email").AsText());
        Assert.Equal("Sample.Cat Store", run.Value("shop").AsText());

        // What was not asked about is still where it was: a setting is changed in place, not by
        // writing a file with one value in it.
        Assert.Equal("Black", run.Value("colour").AsText());
    }
}
