using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TimeMapGameplay.Editor
{
    /// <summary>Marks a <see cref="TrackDrawer"/> as the drawer for a track type (and its subclasses).</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class CustomTrackDrawerAttribute : Attribute
    {
        public CustomTrackDrawerAttribute(Type trackType) => TrackType = trackType;

        public Type TrackType { get; }
    }

    /// <summary>Everything a drawer needs to build and lay out a lane.</summary>
    public sealed class TrackDrawerContext
    {
        public TrackDrawerContext(TimelineView view, TrackBase track)
        {
            View = view;
            Track = track;
        }

        public TimelineView View { get; }
        public TrackBase Track { get; }
        public ProgressionTimeline Timeline => View.Timeline;
        public TimelineViewport Viewport => View.Viewport;
        public float Length => View.Timeline.Axis.Length;
    }

    /// <summary>
    /// Draws the lane of a track. Declare a subclass with <see cref="CustomTrackDrawerAttribute"/> to customise a
    /// project track type; the most specific drawer for the track's class hierarchy is used.
    /// Drawers are shared per track type, so keep per-lane state in the lane element.
    /// </summary>
    public abstract class TrackDrawer
    {
        public virtual float LaneHeight => TimelineView.LaneHeight;

        /// <summary>Creates the lane (called on every rebuild).</summary>
        public abstract VisualElement CreateLane(TrackDrawerContext context);

        /// <summary>Positions lane content after zoom or data changes. Items are positioned by the view itself.</summary>
        public virtual void Layout(VisualElement lane, TrackDrawerContext context)
        {
        }
    }

    /// <summary>Default drawer for clip and marker tracks: draggable items plus a lane context menu.</summary>
    [CustomTrackDrawer(typeof(TrackBase))]
    public class ItemTrackDrawer : TrackDrawer
    {
        public override VisualElement CreateLane(TrackDrawerContext context)
        {
            var lane = new VisualElement();
            lane.AddToClassList("tmg-lane");
            context.View.AttachLaneMenu(lane, context.Track);
            foreach (var item in context.Track.Items)
            {
                if (item == null) continue;
                var element = context.View.CreateItemElement(item, context.Track);
                lane.Add(element);
            }
            return lane;
        }

        public override void Layout(VisualElement lane, TrackDrawerContext context)
        {
            foreach (var element in lane.Children().OfType<ItemElement>())
                UpdateItem(element, context);
        }

        /// <summary>Hook to decorate an item (tags, icons) after each layout.</summary>
        protected virtual void UpdateItem(ItemElement element, TrackDrawerContext context)
        {
        }
    }

    /// <summary>Fabula clips show their chronology index as in the concept ("#3").</summary>
    [CustomTrackDrawer(typeof(FabulaTrack))]
    public sealed class FabulaTrackDrawer : ItemTrackDrawer
    {
        protected override void UpdateItem(ItemElement element, TrackDrawerContext context)
            => element.SetTag(element.Item is FabulaEvent e ? $"#{e.ChronoIndex}" : null);
    }

    /// <summary>Skills and upgrades show their required level.</summary>
    [CustomTrackDrawer(typeof(SkillTrack))]
    [CustomTrackDrawer(typeof(UpgradeTrack))]
    public sealed class LevelGatedTrackDrawer : ItemTrackDrawer
    {
        protected override void UpdateItem(ItemElement element, TrackDrawerContext context)
            => element.SetTag(element.Item is ILevelGated g && g.MinLevel > 0 ? $"ур.{g.MinLevel}" : null);
    }

    [CustomTrackDrawer(typeof(CurveTrack))]
    public sealed class CurveTrackDrawer : TrackDrawer
    {
        public override float LaneHeight => CurveLane.Height;

        public override VisualElement CreateLane(TrackDrawerContext context)
        {
            var curve = (CurveTrack)context.Track;
            var lane = new CurveLane(context.View, curve);
            lane.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                if (evt.target != lane) return;
                float t = context.View.SnappedTimeAt(evt.localMousePosition.x);
                evt.menu.AppendAction($"Добавить ключ в {context.View.FormatTime(t)}", _ => context.View.Operations.AddCurveKey(curve, t));
            });
            return lane;
        }

        public override void Layout(VisualElement lane, TrackDrawerContext context)
            => ((CurveLane)lane).Layout(context.Viewport, context.Length);
    }

    /// <summary>Finds the most specific drawer for a track type.</summary>
    public static class TrackDrawers
    {
        static Dictionary<Type, Type> _drawerTypes;
        static readonly Dictionary<Type, TrackDrawer> Instances = new();

        static Dictionary<Type, Type> DrawerTypes
        {
            get
            {
                if (_drawerTypes != null) return _drawerTypes;
                _drawerTypes = new Dictionary<Type, Type>();
                // Built-in drawers first so that project drawers for the same track type override them.
                var drawers = TypeCache.GetTypesWithAttribute<CustomTrackDrawerAttribute>()
                    .Where(t => typeof(TrackDrawer).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                    .OrderBy(t => t.Assembly == typeof(TrackDrawer).Assembly ? 0 : 1);
                foreach (var drawer in drawers)
                foreach (CustomTrackDrawerAttribute attribute in drawer.GetCustomAttributes(typeof(CustomTrackDrawerAttribute), false))
                    _drawerTypes[attribute.TrackType] = drawer;
                return _drawerTypes;
            }
        }

        public static TrackDrawer For(Type trackType)
        {
            for (var t = trackType; t != null; t = t.BaseType)
            {
                if (!DrawerTypes.TryGetValue(t, out var drawerType)) continue;
                if (!Instances.TryGetValue(drawerType, out var drawer))
                {
                    drawer = (TrackDrawer)Activator.CreateInstance(drawerType);
                    Instances[drawerType] = drawer;
                }
                return drawer;
            }
            Debug.LogError($"[TimeMapGameplay] No TrackDrawer for {trackType.Name}.");
            return For(typeof(TrackBase));
        }
    }
}
