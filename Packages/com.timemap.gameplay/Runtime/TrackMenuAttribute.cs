using System;

namespace TimeMapGameplay
{
    /// <summary>Display name of a track type in the "Add Track" menu. Types without it use their class name.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TrackMenuAttribute : Attribute
    {
        public TrackMenuAttribute(string path) => Path = path;

        public string Path { get; }
    }
}
