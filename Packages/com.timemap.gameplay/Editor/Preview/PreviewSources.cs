using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeMapGameplay.Editor
{
    /// <summary>Picture and text of a bound asset, for slice cards and the inspector.</summary>
    public sealed class PreviewData
    {
        public Texture Image;
        public string Title;
        public string Description;
    }

    /// <summary>
    /// Adapter that knows how to preview project assets (quests, items, locations...).
    /// Implementations with a public parameterless constructor are discovered automatically; the highest
    /// <see cref="Priority"/> that <see cref="CanPreview"/> wins, and empty fields fall back to lower-priority sources.
    /// </summary>
    public interface IPreviewSource
    {
        int Priority { get; }
        bool CanPreview(Object asset);
        PreviewData GetPreview(Object asset);
    }

    public static class PreviewSources
    {
        static List<IPreviewSource> _sources;

        static IReadOnlyList<IPreviewSource> Sources => _sources ??= TypeCache.GetTypesDerivedFrom<IPreviewSource>()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (IPreviewSource)Activator.CreateInstance(t))
            .OrderByDescending(s => s.Priority)
            .ToList();

        /// <summary>Merged preview of <paramref name="asset"/>, or null when unbound.</summary>
        public static PreviewData Get(Object asset)
        {
            if (asset == null) return null;
            var result = new PreviewData();
            foreach (var source in Sources)
            {
                if (!source.CanPreview(asset)) continue;
                PreviewData preview;
                try
                {
                    preview = source.GetPreview(asset);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    continue;
                }
                if (preview == null) continue;
                result.Image ??= preview.Image;
                result.Title ??= string.IsNullOrEmpty(preview.Title) ? null : preview.Title;
                result.Description ??= string.IsNullOrEmpty(preview.Description) ? null : preview.Description;
                if (result.Image != null && result.Title != null && result.Description != null) break;
            }
            result.Title ??= asset.name;
            return result;
        }
    }

    /// <summary>Sprites and textures preview as themselves.</summary>
    public sealed class ImagePreviewSource : IPreviewSource
    {
        public int Priority => 100;
        public bool CanPreview(Object asset) => asset is Sprite or Texture;

        public PreviewData GetPreview(Object asset) => new()
        {
            Image = asset is Sprite sprite ? sprite.texture : (Texture)asset
        };
    }

    /// <summary>
    /// Convention-based preview for ScriptableObjects and components: reads fields/properties named like
    /// icon/sprite/image, title/displayName/name, description/text/summary.
    /// </summary>
    public sealed class ConventionPreviewSource : IPreviewSource
    {
        static readonly string[] ImageNames = { "icon", "sprite", "image", "art", "picture", "portrait", "preview" };
        static readonly string[] TitleNames = { "title", "displayname", "name", "label" };
        static readonly string[] TextNames = { "description", "desc", "text", "summary", "synopsis", "lore" };

        public int Priority => 50;
        public bool CanPreview(Object asset) => asset is ScriptableObject or Component;

        public PreviewData GetPreview(Object asset)
        {
            var members = Members(asset.GetType()).ToList();
            object Find(string[] names, Func<object, bool> accept)
            {
                foreach (var name in names)
                foreach (var (memberName, getter) in members)
                {
                    if (Normalize(memberName) != name) continue;
                    object value;
                    try { value = getter(asset); }
                    catch { continue; }
                    // Empty Unity object fields loaded from disk are "fake null": not null for C#, null for Unity.
                    if (value is Object unityObject ? unityObject != null : value != null)
                        if (accept(value)) return value;
                }
                return null;
            }

            var image = Find(ImageNames, v => v is Sprite or Texture);
            return new PreviewData
            {
                Image = image is Sprite s ? s.texture : image as Texture,
                Title = Find(TitleNames, v => v is string str && str.Length > 0 && !ReferenceEquals(v, asset.name)) as string,
                Description = Find(TextNames, v => v is string str && str.Length > 0) as string
            };
        }

        static string Normalize(string name) => (name.StartsWith("m_") ? name.Substring(2) : name).TrimStart('_').ToLowerInvariant();

        static IEnumerable<(string name, Func<object, object> getter)> Members(Type type)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (var t = type; t != null && t != typeof(ScriptableObject) && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (var field in t.GetFields(flags | BindingFlags.DeclaredOnly))
                    yield return (field.Name, field.GetValue);
                foreach (var property in t.GetProperties(flags | BindingFlags.DeclaredOnly))
                    if (property.CanRead && property.GetIndexParameters().Length == 0)
                        yield return (property.Name, property.GetValue);
            }
        }
    }

    /// <summary>Fallback: Unity's asset preview or mini thumbnail.</summary>
    public sealed class UnityPreviewSource : IPreviewSource
    {
        public int Priority => 0;
        public bool CanPreview(Object asset) => true;

        public PreviewData GetPreview(Object asset) => new()
        {
            Image = UnityEditor.AssetPreview.GetAssetPreview(asset) ?? UnityEditor.AssetPreview.GetMiniThumbnail(asset)
        };
    }
}
