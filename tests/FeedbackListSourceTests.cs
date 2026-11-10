using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace PepperDash.Essentials.Plugin.WyreStorm.Tests;

/// <summary>
/// Guards for the endpoints' feedback lists. The devices need a processor to run, so these check
/// the interface by name and pin the source facts a later edit could undo without anything else
/// failing: monitoring consumers read only keyed feedbacks on <c>Feedbacks</c>, use their cached
/// values (so each must fire when its source changes), and subscribe to the list once after
/// activation (so it must be complete by the end of construction).
/// </summary>
public class FeedbackListSourceTests
{
    private static string Source(string file)
    {
        var path = Path.Combine(AssemblyFixture.SourceDirectory, "Devices", file);
        File.Exists(path).Should().BeTrue($"the source should be at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>The text from the opening brace after <paramref name="signature"/> to its match.</summary>
    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"'{signature}' should be in the source");

        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }

        throw new InvalidOperationException($"unbalanced braces after '{signature}'");
    }

    private static string BaseSource => Source("NhdBaseDevice.cs");

    private static string BaseConstructor =>
        Body(BaseSource, "protected NhdBaseDevice(string key, string name, NhdDeviceProperties config, string modelName)");

    private static string RxConstructor =>
        Body(Source("Nhd150Rx.cs"), "public Nhd150Rx(string key, string name, NhdDeviceProperties config)");

    [Theory]
    [InlineData("PepperDash.Essentials.Plugin.Nhd120Tx")]
    [InlineData("PepperDash.Essentials.Plugin.Nhd150Rx")]
    [InlineData("PepperDash.Essentials.Plugin.NhdCtlPro")]
    public void Endpoint_Implements_IHasFeedback(string typeName)
    {
        var type = AssemblyFixture.PluginAssembly.GetType(typeName);
        type.Should().NotBeNull();

        type!.GetInterfaces().Select(i => i.FullName)
            .Should().Contain("PepperDash.Essentials.Core.IHasFeedback");
    }

    [Theory]
    [InlineData("IsOnline")]
    [InlineData("InputSyncDetected")]
    public void Base_Constructor_Lists_Online_And_Sync(string feedback)
    {
        BaseConstructor.Should().MatchRegex(
            $@"Feedbacks = new FeedbackCollection<Feedback> \{{[^}}]*\b{feedback}\b[^}}]*\}};");
    }

    [Theory]
    [InlineData("HdmiOutResolutionFeedback", "HdmiOutResolution")]
    [InlineData("ActiveLayoutFeedback", "ActiveLayout")]
    public void Decoder_Feedback_Is_Keyed_In_The_Base_And_Listed_By_The_Decoder(string property, string key)
    {
        BaseConstructor.Should().Contain($"{property} = new StringFeedback(\"{key}\"");
        RxConstructor.Should().Contain($"Feedbacks.Add({property});");
    }

    [Fact]
    public void Feedbacks_Are_Only_Added_In_Constructors()
    {
        var baseSource = BaseSource;
        Regex.Matches(baseSource, @"Feedbacks\.Add\(").Count.Should().Be(0,
            "the base builds its list in the constructor initializer");

        var rxSource = Source("Nhd150Rx.cs");
        Regex.Matches(rxSource, @"Feedbacks\.Add\(").Count
            .Should().Be(Regex.Matches(RxConstructor, @"Feedbacks\.Add\(").Count);
    }

    [Theory]
    [InlineData("public void SetHdmiOutResolution(string resolution)", "HdmiOutResolutionFeedback.FireUpdate();")]
    [InlineData("public void SetActivePresetMVLayout(string layoutName, bool inferred = false)", "ActiveLayoutFeedback.FireUpdate();")]
    [InlineData("public void SetActiveCustomMVLayout(string layoutKey, bool inferred = false)", "ActiveLayoutFeedback.FireUpdate();")]
    [InlineData("public void SetOnlineState(bool isOnline)", "ActiveLayoutFeedback.FireUpdate();")]
    [InlineData("public void SetOnlineState(bool isOnline)", "IsOnline.FireUpdate();")]
    [InlineData("public void SetInputSyncState(bool hasSync)", "InputSyncDetected.FireUpdate();")]
    public void Setter_Fires_Its_Feedback(string signature, string fire)
    {
        Body(BaseSource, signature).Should().Contain(fire);
    }
}
