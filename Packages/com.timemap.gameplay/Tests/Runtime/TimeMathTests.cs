using NUnit.Framework;

namespace TimeMapGameplay.Tests
{
    public class TimeMathTests
    {
        [TestCase(3.1f, 0.25f, 3.0f)]
        [TestCase(3.13f, 0.25f, 3.25f)]
        [TestCase(-0.1f, 0.25f, 0f)]
        [TestCase(7.77f, 0f, 7.77f)]
        public void Snap_RoundsToNearestStep(float value, float step, float expected)
        {
            Assert.That(TimeMath.Snap(value, step), Is.EqualTo(expected).Within(1e-5f));
        }

        [TestCase(0f, "0:00")]
        [TestCase(3.5f, "3:30")]
        [TestCase(11.75f, "11:45")]
        [TestCase(1.999f, "2:00")]
        public void Format_Hours_UsesHoursAndMinutes(float time, string expected)
        {
            Assert.That(TimeMath.Format(time), Is.EqualTo(expected));
        }

        [Test]
        public void Format_OtherUnits_UsesPlainNumber()
        {
            Assert.That(TimeMath.Format(12f, AxisUnit.Quests), Is.EqualTo("12"));
            Assert.That(TimeMath.Format(2.5f, AxisUnit.Sessions), Is.EqualTo("2.5"));
        }

        [Test]
        public void ClampStart_KeepsClipInsideAxis()
        {
            Assert.That(TimeMath.ClampStart(-1f, 2f, 20f), Is.EqualTo(0f));
            Assert.That(TimeMath.ClampStart(19f, 2f, 20f), Is.EqualTo(18f));
            Assert.That(TimeMath.ClampStart(5f, 2f, 20f), Is.EqualTo(5f));
        }

        [Test]
        public void ClampDuration_RespectsMinimumAndAxisEnd()
        {
            Assert.That(TimeMath.ClampDuration(0f, 5f, 20f, 0.25f), Is.EqualTo(0.25f));
            Assert.That(TimeMath.ClampDuration(30f, 5f, 20f, 0.25f), Is.EqualTo(15f));
        }
    }
}
