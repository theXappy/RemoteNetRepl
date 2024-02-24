using CSharpRepl.Services.Roslyn;
using Newtonsoft.Json;
using ScubaDiver.API;
using Xunit;
using JsonConvert = ScubaDiver.API.JsonConvert;

namespace CSharpRepl.Tests;


[Collection(nameof(RoslynServices))]
public class NewtonsoftJsonProxyTests
{
    private class TestClass
    {
        public int SomeNumber { get; set; }
    }

    [Fact]
    public void JsonSerializerSettingsWithErrors_Test()
    {
        var ass = typeof(Newtonsoft.Json.JsonReader).Assembly;
        NewtonsoftProxy.Init(ass);

        var x = NewtonsoftProxy.JsonSerializerSettingsWithErrors;

        Assert.NotNull(x);
    }



    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "{\"SomeNumber\":3}")]
    public void JsonConvert_DeserializeObject_ParseValidJson(bool allowErrors, string input)
    {
        var ass = typeof(Newtonsoft.Json.JsonReader).Assembly;
        NewtonsoftProxy.Init(ass);

        object withErrors = allowErrors ? NewtonsoftProxy.JsonSerializerSettingsWithErrors : null;

        var x = JsonConvert.DeserializeObject<TestClass>(input, withErrors);

        Assert.NotNull(x);
    }

    [Theory]
    [InlineData(true, "{\"SomeWrongPropName\":3}")]
    public void JsonConvert_DeserializeObject_FailOnBadJson(bool allowErrors, string input)
    {
        var ass = typeof(Newtonsoft.Json.JsonReader).Assembly;
        NewtonsoftProxy.Init(ass);

        object withErrors = allowErrors ? NewtonsoftProxy.JsonSerializerSettingsWithErrors : null;

        Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<TestClass>(input, withErrors));
    }


    [Fact]
    public void JsonConvert_SerializeObject_RetValidJson()
    {
        var ass = typeof(Newtonsoft.Json.JsonReader).Assembly;
        NewtonsoftProxy.Init(ass);
        object input = new TestClass() { SomeNumber = 3 };

        var x = JsonConvert.SerializeObject(input);

        Assert.NotNull(x);
        Assert.Equal("{\"SomeNumber\":3}", x);
    }


}
