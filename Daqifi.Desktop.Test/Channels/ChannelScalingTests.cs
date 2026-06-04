using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Device;
using Moq;
using CoreAnalogChannel = Daqifi.Core.Channel.AnalogChannel;
using CoreChannelDirection = Daqifi.Core.Channel.ChannelDirection;

namespace Daqifi.Desktop.Test.Channels;

/// <summary>
/// Unit coverage for the per-channel math/scaling feature in <see cref="AbstractChannel"/>
/// (issue #556): the NCalc <c>ScaleExpression</c> validation path and the transform applied
/// to each incoming <see cref="DataSample"/> in the <c>ActiveSample</c> setter.
///
/// These are pure, deterministic, hardware-free tests: a channel is wrapped around a Core
/// analog channel with a mocked owner device, an expression is set, and samples are pushed
/// directly into <c>ActiveSample</c>. They pin the exact arithmetic contract (e.g. <c>x * 10</c>
/// yields exactly 10x), every guard branch (inactive / no expression / invalid expression all
/// pass the raw value through untouched), and the graceful-degradation path when evaluation
/// fails on live data — none of which needs the UI or an attached device. The FlaUI integration
/// scenario is intended only as thin end-to-end wiring smoke on top of this.
/// </summary>
[TestClass]
public class ChannelScalingTests
{
    #region Constants
    // x * 10, x + 5, and the sample values below are all exactly representable in double,
    // but assert with a tiny tolerance anyway (convention + guards against NCalc int/double
    // promotion surprises).
    private const double Tolerance = 1e-9;
    #endregion

    #region Helpers
    /// <summary>
    /// Builds a real <see cref="AnalogChannel"/> (the production type) wrapping a Core analog
    /// channel with unity calibration, owned by a mocked device. The owner is only read for two
    /// string properties at construction; the scaling logic under test never touches it.
    /// </summary>
    private static AnalogChannel CreateAnalogChannel()
    {
        var owner = new Mock<IStreamingDevice>().Object;
        var coreChannel = new CoreAnalogChannel(0, 4096)
        {
            Name = "AI0",
            Direction = CoreChannelDirection.Input,
            CalibrationB = 0,
            CalibrationM = 1,
            InternalScaleM = 1,
            PortRange = 5
        };

        return new AnalogChannel(owner, coreChannel);
    }

    private static DataSample Sample(double value) => new() { Value = value };
    #endregion

    #region Expression Validation (ScaleExpression setter)
    [TestMethod]
    public void ScaleExpression_ValidLinearExpression_MarksExpressionValid()
    {
        var channel = CreateAnalogChannel();

        channel.ScaleExpression = "x * 10";

        Assert.IsTrue(channel.HasValidExpression, "A well-formed linear expression should validate.");
        Assert.IsNotNull(channel.Expression, "A valid expression should leave a compiled Expression in place.");
    }

    [TestMethod]
    public void ScaleExpression_ConstantExpressionWithoutX_IsValid()
    {
        // A constant (no x reference) is still a legal transform — it must validate, not error.
        var channel = CreateAnalogChannel();

        channel.ScaleExpression = "42";

        Assert.IsTrue(channel.HasValidExpression, "A constant expression evaluates cleanly and should be valid.");
    }

    [TestMethod]
    public void ScaleExpression_Null_MarksExpressionInvalid()
    {
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x * 10"; // start valid so we can prove it gets cleared

        channel.ScaleExpression = null!; // null! : intentionally exercising the null-handling path

        Assert.IsFalse(channel.HasValidExpression, "A null expression should not be considered valid.");
        Assert.IsNull(channel.Expression, "A null expression should clear the compiled Expression.");
    }

    [TestMethod]
    public void ScaleExpression_EmptyString_MarksExpressionInvalid()
    {
        var channel = CreateAnalogChannel();

        channel.ScaleExpression = string.Empty;

        Assert.IsFalse(channel.HasValidExpression, "An empty expression should not be considered valid.");
        Assert.IsNull(channel.Expression);
    }

    [TestMethod]
    public void ScaleExpression_Whitespace_MarksExpressionInvalid()
    {
        var channel = CreateAnalogChannel();

        channel.ScaleExpression = "   ";

        Assert.IsFalse(channel.HasValidExpression, "A whitespace-only expression should not be considered valid.");
        Assert.IsNull(channel.Expression);
    }

    [TestMethod]
    public void ScaleExpression_SyntacticallyInvalid_MarksInvalidAndDoesNotThrow()
    {
        var channel = CreateAnalogChannel();

        // A trailing operator is a parse error. The setter must swallow it and degrade to
        // "no valid expression", never propagate the exception to the binding.
        channel.ScaleExpression = "x *";

        Assert.IsFalse(channel.HasValidExpression, "A syntactically invalid expression should not validate.");
        Assert.IsNull(channel.Expression, "An invalid expression should not leave a compiled Expression behind.");
    }

    [TestMethod]
    public void ScaleExpression_ClearedAfterValid_MarksInvalid()
    {
        // Regression guard: switching from a valid expression back to empty must fully reset state.
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x * 10";
        Assert.IsTrue(channel.HasValidExpression);

        channel.ScaleExpression = string.Empty;

        Assert.IsFalse(channel.HasValidExpression);
        Assert.IsNull(channel.Expression);
    }

    [TestMethod]
    public void ScaleExpression_CorrectedAfterInvalid_MarksValid()
    {
        // The user fixes a typo: invalid -> valid must re-enable scaling.
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x *";
        Assert.IsFalse(channel.HasValidExpression);

        channel.ScaleExpression = "x * 2";

        Assert.IsTrue(channel.HasValidExpression, "Correcting the expression should re-validate it.");
        Assert.IsNotNull(channel.Expression);
    }
    #endregion

    #region Scaling Transform (ActiveSample setter)
    [TestMethod]
    public void ActiveSample_X10ScalingActive_MultipliesSampleByTen()
    {
        // The headline scenario from issue #556: x * 10 yields exactly 10x the raw sample.
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x * 10";
        channel.IsScalingActive = true;

        channel.ActiveSample = Sample(2.5);

        Assert.AreEqual(25.0, channel.ActiveSample.Value, Tolerance,
            "x * 10 applied to 2.5 should yield 25.");
    }

    [TestMethod]
    public void ActiveSample_AdditiveExpressionActive_AppliesOffset()
    {
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x + 5";
        channel.IsScalingActive = true;

        channel.ActiveSample = Sample(10.0);

        Assert.AreEqual(15.0, channel.ActiveSample.Value, Tolerance,
            "x + 5 applied to 10 should yield 15.");
    }

    [TestMethod]
    public void ActiveSample_MultipleSamples_EachScaledFromItsOwnRawValue()
    {
        // Proves the parameter x is refreshed per sample, not cached from the first one.
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x * 10";
        channel.IsScalingActive = true;

        channel.ActiveSample = Sample(2.5);
        Assert.AreEqual(25.0, channel.ActiveSample.Value, Tolerance);

        channel.ActiveSample = Sample(4.0);
        Assert.AreEqual(40.0, channel.ActiveSample.Value, Tolerance,
            "The second sample should scale from its own raw value, not the first.");
    }

    [TestMethod]
    public void ActiveSample_ScalingInactive_LeavesRawValueUnchanged()
    {
        // Valid expression present, but scaling toggled off: the raw value must pass through.
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x * 10";
        channel.IsScalingActive = false;

        channel.ActiveSample = Sample(2.5);

        Assert.AreEqual(2.5, channel.ActiveSample.Value, Tolerance,
            "With scaling inactive the raw sample value must be untouched.");
    }

    [TestMethod]
    public void ActiveSample_NoExpressionSet_LeavesRawValueUnchanged()
    {
        // Scaling flagged active but no expression was ever set: raw value passes through.
        var channel = CreateAnalogChannel();
        channel.IsScalingActive = true;

        channel.ActiveSample = Sample(2.5);

        Assert.AreEqual(2.5, channel.ActiveSample.Value, Tolerance,
            "With no expression the raw sample value must be untouched.");
    }

    [TestMethod]
    public void ActiveSample_InvalidExpression_LeavesRawValueUnchanged()
    {
        // Acceptance criterion: an invalid expression must not regress the raw data path.
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x *"; // invalid -> HasValidExpression false, Expression null
        channel.IsScalingActive = true;

        channel.ActiveSample = Sample(2.5);

        Assert.IsFalse(channel.HasValidExpression);
        Assert.AreEqual(2.5, channel.ActiveSample.Value, Tolerance,
            "An invalid expression must leave the raw value flowing unchanged.");
    }

    [TestMethod]
    public void ActiveSample_ScalingActive_NotifiesSubscribersWithScaledValue()
    {
        // The transform must be applied BEFORE downstream notification, so loggers and the
        // live plot receive the scaled value — this is what makes the displayed/logged data
        // reflect the expression.
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x * 10";
        channel.IsScalingActive = true;

        DataSample? notified = null;
        channel.OnChannelUpdated += (_, sample) => notified = sample;

        channel.ActiveSample = Sample(2.5);

        Assert.IsNotNull(notified, "Setting ActiveSample should raise OnChannelUpdated.");
        Assert.AreEqual(25.0, notified!.Value, Tolerance,
            "Subscribers should receive the scaled value, not the raw one.");
    }

    [TestMethod]
    public void ActiveSample_NullSample_DoesNotThrow()
    {
        // Defensive: clearing the active sample while scaling is active must be a safe no-op.
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "x * 10";
        channel.IsScalingActive = true;

        channel.ActiveSample = null!; // null! : intentionally exercising the null-handling path

        Assert.IsNull(channel.ActiveSample);
    }

    [TestMethod]
    public void ActiveSample_RuntimeEvaluationError_DisablesScalingAndKeepsRawValue()
    {
        // The ActiveSample catch path exists because validation only calls Evaluate() (with x=1),
        // while the live path additionally does Convert.ToDouble(...). An expression that yields a
        // non-numeric result passes validation but fails per-sample conversion. The channel must
        // degrade gracefully: swallow the error, disable scaling, and keep the raw value intact.
        // NOTE: this catch only fires when evaluation THROWS. A float divide-by-zero such as
        // "100 / (x - 5)" returns +/-Infinity WITHOUT throwing, so it slips past here untouched —
        // that non-finite-result leak is a separate, known gap, not covered by this test.
        var channel = CreateAnalogChannel();
        channel.ScaleExpression = "'not a number'"; // string literal: valid to evaluate, not convertible
        channel.IsScalingActive = true;
        Assert.IsTrue(channel.HasValidExpression,
            "Precondition: a string-literal expression passes the evaluate-only validation.");

        channel.ActiveSample = Sample(2.5);

        Assert.IsFalse(channel.HasValidExpression,
            "A runtime evaluation/conversion failure should disable the expression.");
        Assert.AreEqual(2.5, channel.ActiveSample.Value, Tolerance,
            "A runtime failure must leave the raw sample value uncorrupted.");
    }
    #endregion
}
