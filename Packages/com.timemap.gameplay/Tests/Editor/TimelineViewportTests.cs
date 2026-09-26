using NUnit.Framework;
using TimeMapGameplay.Editor;

namespace TimeMapGameplay.Tests
{
    public class TimelineViewportTests
    {
        [Test]
        public void TimeAndPixel_AreInverse()
        {
            var viewport = new TimelineViewport(62f);
            Assert.That(viewport.TimeToPixel(3.5f), Is.EqualTo(217f).Within(1e-4f));
            Assert.That(viewport.PixelToTime(viewport.TimeToPixel(7.25f)), Is.EqualTo(7.25f).Within(1e-4f));
        }

        [Test]
        public void Zoom_IsClamped()
        {
            Assert.That(new TimelineViewport(1f).PixelsPerUnit, Is.EqualTo(TimelineViewport.MinPixelsPerUnit));
            Assert.That(new TimelineViewport(10000f).PixelsPerUnit, Is.EqualTo(TimelineViewport.MaxPixelsPerUnit));
        }

        [TestCase(62f, 1f)]
        [TestCase(300f, 0.25f)]
        [TestCase(20f, 5f)]
        public void MajorTickStep_IsSmallestNiceStepWithEnoughSpacing(float pixelsPerUnit, float expected)
        {
            Assert.That(new TimelineViewport(pixelsPerUnit).MajorTickStep(56f), Is.EqualTo(expected));
        }

        [Test]
        public void MinorTickStep_HiddenWhenTooDense()
        {
            Assert.That(new TimelineViewport(62f).MinorTickStep(56f, 10f), Is.EqualTo(0.25f));
            Assert.That(new TimelineViewport(62f).MinorTickStep(56f, 20f), Is.EqualTo(0f));
        }

        [Test]
        public void ScrollOffsetKeepingAnchor_KeepsTimeUnderCursor()
        {
            var zoomed = new TimelineViewport(124f);
            float offset = zoomed.ScrollOffsetKeepingAnchor(anchorTime: 5f, anchorViewportX: 100f);
            Assert.That(zoomed.PixelToTime(offset + 100f), Is.EqualTo(5f).Within(1e-4f));
            Assert.That(zoomed.ScrollOffsetKeepingAnchor(0.1f, 300f), Is.EqualTo(0f));
        }
    }
}
