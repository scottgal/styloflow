using StyloFlow.WorkflowBuilder.Atoms.Shapers;

namespace StyloFlow.WorkflowBuilder.Tests;

public class SignalShapersTests
{
    #region SignalClampShaper Tests

    [Fact]
    public async Task SignalClampShaper_ClampsValueToRange()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["min"] = 0.0,
            ["max"] = 1.0
        });
        // Emit to a signal name that GetNumericSignal() looks for
        ctx.Signals.Emit("sentiment.score", 1.5, "source");

        // Act
        await SignalClampShaper.ExecuteAsync(ctx);

        // Assert
        var clamped = ctx.Signals.Get<double>("clamp.value");
        clamped.Should().Be(1.0);
    }

    [Fact]
    public async Task SignalClampShaper_ClampsNegativeToMin()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["min"] = 0.0,
            ["max"] = 1.0
        });
        ctx.Signals.Emit("sentiment.score", -0.5, "source");

        // Act
        await SignalClampShaper.ExecuteAsync(ctx);

        // Assert
        var clamped = ctx.Signals.Get<double>("clamp.value");
        clamped.Should().Be(0.0);
    }

    [Fact]
    public async Task SignalClampShaper_PassesThroughInRange()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["min"] = 0.0,
            ["max"] = 1.0
        });
        ctx.Signals.Emit("sentiment.score", 0.5, "source");

        // Act
        await SignalClampShaper.ExecuteAsync(ctx);

        // Assert
        var clamped = ctx.Signals.Get<double>("clamp.value");
        clamped.Should().Be(0.5);
    }

    [Fact]
    public async Task SignalClampShaper_NoOp_WhenNoSignal()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["min"] = 0.0,
            ["max"] = 1.0
        });
        // Don't emit any signal

        // Act
        await SignalClampShaper.ExecuteAsync(ctx);

        // Assert - should not emit anything
        ctx.Signals.Has("clamp.value").Should().BeFalse();
    }

    #endregion

    #region SignalFilterShaper Tests

    [Fact]
    public async Task SignalFilterShaper_PassesWhenConditionMet()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["operator"] = "gte",
            ["threshold"] = 0.5
        });
        ctx.Signals.Emit("sentiment.score", 0.7, "source");

        // Act
        await SignalFilterShaper.ExecuteAsync(ctx);

        // Assert
        var passed = ctx.Signals.Get<double?>("filter.passed");
        passed.Should().NotBeNull();
        passed.Should().Be(0.7);
    }

    [Fact]
    public async Task SignalFilterShaper_BlocksWhenConditionNotMet()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["operator"] = "gte",
            ["threshold"] = 0.5
        });
        ctx.Signals.Emit("sentiment.score", 0.3, "source");

        // Act
        await SignalFilterShaper.ExecuteAsync(ctx);

        // Assert
        var blocked = ctx.Signals.Get<bool>("filter.blocked");
        blocked.Should().BeTrue();
    }

    [Fact]
    public async Task SignalFilterShaper_LessThanOperator()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["operator"] = "lt",
            ["threshold"] = 0.5
        });
        ctx.Signals.Emit("sentiment.score", 0.3, "source");

        // Act
        await SignalFilterShaper.ExecuteAsync(ctx);

        // Assert
        var passed = ctx.Signals.Get<double?>("filter.passed");
        passed.Should().Be(0.3);
    }

    #endregion

    #region SignalQuantizerShaper Tests

    [Fact]
    public async Task SignalQuantizerShaper_QuantizesToSteps()
    {
        // Arrange - 4 steps means values 0, 0.333, 0.666, 1.0
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["steps"] = 4,
            ["min"] = 0.0,
            ["max"] = 1.0
        });
        ctx.Signals.Emit("sentiment.score", 0.37, "source");

        // Act
        await SignalQuantizerShaper.ExecuteAsync(ctx);

        // Assert - 0.37 should round to step 1 = 0.333...
        var quantized = ctx.Signals.Get<double>("quantize.value");
        quantized.Should().BeApproximately(0.333, 0.01);
    }

    [Fact]
    public async Task SignalQuantizerShaper_RoundsUpCorrectly()
    {
        // Arrange - 5 steps means values 0, 0.25, 0.5, 0.75, 1.0
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["steps"] = 5,
            ["min"] = 0.0,
            ["max"] = 1.0
        });
        ctx.Signals.Emit("sentiment.score", 0.63, "source");

        // Act
        await SignalQuantizerShaper.ExecuteAsync(ctx);

        // Assert - 0.63 should round to step 3 = 0.75
        var quantized = ctx.Signals.Get<double>("quantize.value");
        quantized.Should().BeApproximately(0.75, 0.01);
    }

    [Fact]
    public async Task SignalQuantizerShaper_EmitsStepIndex()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["steps"] = 5,
            ["min"] = 0.0,
            ["max"] = 1.0
        });
        ctx.Signals.Emit("sentiment.score", 0.5, "source");

        // Act
        await SignalQuantizerShaper.ExecuteAsync(ctx);

        // Assert
        var step = ctx.Signals.Get<int>("quantize.step");
        step.Should().Be(2); // Middle step
    }

    #endregion

    #region SignalAttenuverterShaper Tests

    [Fact]
    public async Task SignalAttenuverterShaper_Scales()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["scale"] = 2.0,
            ["offset"] = 0.0,
            ["clip"] = false
        });
        ctx.Signals.Emit("sentiment.score", 0.5, "source");

        // Act
        await SignalAttenuverterShaper.ExecuteAsync(ctx);

        // Assert
        var result = ctx.Signals.Get<double>("atten.value");
        result.Should().Be(1.0);
    }

    [Fact]
    public async Task SignalAttenuverterShaper_AppliesOffset()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["scale"] = 1.0,
            ["offset"] = 0.1,
            ["clip"] = false
        });
        ctx.Signals.Emit("sentiment.score", 0.5, "source");

        // Act
        await SignalAttenuverterShaper.ExecuteAsync(ctx);

        // Assert
        var result = ctx.Signals.Get<double>("atten.value");
        result.Should().BeApproximately(0.6, 0.001);
    }

    [Fact]
    public async Task SignalAttenuverterShaper_ClipsWhenEnabled()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["scale"] = 3.0,
            ["offset"] = 0.0,
            ["clip"] = true
        });
        ctx.Signals.Emit("sentiment.score", 0.5, "source");

        // Act
        await SignalAttenuverterShaper.ExecuteAsync(ctx);

        // Assert - 0.5 * 3.0 = 1.5, clipped to 1.0
        var result = ctx.Signals.Get<double>("atten.value");
        result.Should().Be(1.0);
        var clipped = ctx.Signals.Get<bool>("atten.clipped");
        clipped.Should().BeTrue();
    }

    #endregion

    #region SignalComparatorShaper Tests

    [Fact]
    public async Task SignalComparatorShaper_ReturnsTrue_WhenGreaterThanThreshold()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["operator"] = "gt",
            ["threshold"] = 5.0
        });
        ctx.Signals.Emit("sentiment.score", 10.0, "source");

        // Act
        await SignalComparatorShaper.ExecuteAsync(ctx);

        // Assert
        var result = ctx.Signals.Get<bool>("compare.result");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SignalComparatorShaper_ReturnsFalse_WhenLessThanThreshold()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["operator"] = "gt",
            ["threshold"] = 7.0
        });
        ctx.Signals.Emit("sentiment.score", 3.0, "source");

        // Act
        await SignalComparatorShaper.ExecuteAsync(ctx);

        // Assert
        var result = ctx.Signals.Get<bool>("compare.result");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SignalComparatorShaper_EmitsDifference()
    {
        // Arrange
        var ctx = TestHelpers.CreateTestContext(new Dictionary<string, object>
        {
            ["operator"] = "gt",
            ["threshold"] = 5.0
        });
        ctx.Signals.Emit("sentiment.score", 8.0, "source");

        // Act
        await SignalComparatorShaper.ExecuteAsync(ctx);

        // Assert
        var diff = ctx.Signals.Get<double>("compare.difference");
        diff.Should().Be(3.0); // 8.0 - 5.0
    }

    #endregion
}
