using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JY66.SimpleTemplateEngine
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
    public sealed class TemplateAttribute : Attribute
    {
        public TemplateAttribute(string templateKey)
        {
            TemplateKey = templateKey ?? throw new ArgumentNullException(nameof(templateKey));
        }

        public string TemplateKey { get; }
    }

    public interface ITemplateSource
    {
        /// <summary>
        /// Returns the template string for the given key.
        /// Implementations should throw if the key is unknown.
        /// </summary>
        string GetTemplate(string key);
    }

    public static class TemplateRenderer
    {
        /// <summary>
        /// Matches either a nested/list marker (**Member**) or a scalar placeholder
        /// (||Member|| or ||Member:format||). The whole template is scanned exactly once
        /// with this pattern, so text produced by a substitution is never re-scanned.
        /// Placeholders that do not name a public member are left in place.
        /// </summary>
        private static readonly Regex PlaceholderRegex = new Regex(
            @"\*\*(?<nested>\w+)\*\*|\|\|(?<scalar>\w+)(?::(?<format>[^|]+))?\|\|",
            RegexOptions.Compiled);

        /// <summary>
        /// Cache of renderable members (public instance fields and readable, non-indexed
        /// properties) per model type, keyed by member name.
        /// </summary>
        private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, MemberInfo>> _memberCache =
            new ConcurrentDictionary<Type, IReadOnlyDictionary<string, MemberInfo>>();

        /// <summary>
        /// Renders a template for the given model by:
        ///  - Finding the model's [Template] attribute to get a template key
        ///  - Loading the template via ITemplateSource
        ///  - Replacing ||MemberName|| placeholders with formatted values
        ///  - Expanding nested objects and lists using **MemberName** markers
        /// Member values are inserted literally. Placeholder syntax that appears inside
        /// a value is output as-is and is never interpreted.
        /// </summary>
        public static string Render(object model, ITemplateSource templateSource)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (templateSource == null) throw new ArgumentNullException(nameof(templateSource));

            var modelType = model.GetType();
            var templateKey = GetTemplateKey(modelType)
                ?? throw new InvalidOperationException(
                    $"Type '{modelType.FullName}' is missing the [Template] attribute.");

            var template = templateSource.GetTemplate(templateKey);
            if (string.IsNullOrEmpty(template))
            {
                throw new InvalidOperationException(
                    $"Template '{templateKey}' is empty or not configured.");
            }

            return RenderInternal(
                model,
                modelType,
                template,
                templateSource,
                new HashSet<string>(StringComparer.Ordinal),
                templateKey);
        }

        private static string RenderInternal(
            object model,
            Type modelType,
            string template,
            ITemplateSource templateSource,
            HashSet<string> renderStack,
            string currentTemplateKey)
        {
            if (!renderStack.Add(currentTemplateKey))
            {
                throw new InvalidOperationException(
                    $"Detected circular template reference involving template '{currentTemplateKey}'.");
            }

            try
            {
                var members = _memberCache.GetOrAdd(modelType, GetRenderableMembers);

                // Single pass: Regex.Replace walks the original template once and the
                // evaluator's output is appended to the result without being re-matched.
                return PlaceholderRegex.Replace(template, match =>
                {
                    if (match.Groups["nested"].Success)
                    {
                        if (!members.TryGetValue(match.Groups["nested"].Value, out var member))
                        {
                            return match.Value; // e.g. Markdown **bold** that is not a member name
                        }

                        return RenderNested(model, member, templateSource, renderStack);
                    }

                    if (!members.TryGetValue(match.Groups["scalar"].Value, out var scalarMember))
                    {
                        return match.Value; // unknown placeholder is left in place
                    }

                    var memberType = GetMemberType(scalarMember);
                    if (IsListType(memberType))
                    {
                        return match.Value; // lists are only rendered via **MemberName**
                    }

                    var value = GetMemberValue(model, scalarMember);
                    var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;
                    return FormatValue(value, memberType, format) ?? string.Empty;
                });
            }
            finally
            {
                renderStack.Remove(currentTemplateKey);
            }
        }

        private static string RenderNested(
            object model,
            MemberInfo member,
            ITemplateSource templateSource,
            HashSet<string> renderStack)
        {
            var memberType = GetMemberType(member);

            if (IsListType(memberType))
            {
                var itemType = memberType.GetGenericArguments()[0];
                var itemTemplateKey = GetTemplateKey(itemType)
                    ?? throw new InvalidOperationException(
                        $"Member '{member.Name}' does not have an associated template.");

                if (!(GetMemberValue(model, member) is IEnumerable list))
                {
                    return string.Empty; // null list: remove the marker
                }

                var itemTemplate = templateSource.GetTemplate(itemTemplateKey);
                var output = new StringBuilder();
                foreach (var item in list)
                {
                    if (item == null) continue;
                    output.Append(RenderInternal(
                        item,
                        item.GetType(),
                        itemTemplate,
                        templateSource,
                        renderStack,
                        itemTemplateKey));
                }

                return output.ToString();
            }

            var nestedTemplateKey = GetTemplateKey(memberType)
                ?? throw new InvalidOperationException(
                    $"Member '{member.Name}' does not have an associated template.");

            var nestedValue = GetMemberValue(model, member);
            if (nestedValue == null)
            {
                return string.Empty; // null object: remove the marker
            }

            var nestedTemplate = templateSource.GetTemplate(nestedTemplateKey);
            return RenderInternal(
                nestedValue,
                nestedValue.GetType(),
                nestedTemplate,
                templateSource,
                renderStack,
                nestedTemplateKey);
        }

        private static IReadOnlyDictionary<string, MemberInfo> GetRenderableMembers(Type type)
        {
            var members = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                members.TryAdd(field.Name, field);
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    members.TryAdd(property.Name, property);
                }
            }

            return members;
        }

        private static string? GetTemplateKey(Type type)
        {
            var attr = type
                .GetCustomAttributes(typeof(TemplateAttribute), true)
                .Cast<TemplateAttribute>()
                .FirstOrDefault();

            return attr?.TemplateKey;
        }

        private static bool IsListType(Type type)
        {
            if (!type.IsGenericType) return false;
            var generic = type.GetGenericTypeDefinition();

            // Keep it simple: List<T> only
            return generic == typeof(List<>);
        }

        private static Type GetMemberType(MemberInfo member) =>
            member switch
            {
                FieldInfo f => f.FieldType,
                PropertyInfo p => p.PropertyType,
                _ => throw new NotSupportedException($"Unsupported member type: {member.MemberType}")
            };

        private static object? GetMemberValue(object instance, MemberInfo member) =>
            member switch
            {
                FieldInfo f => f.GetValue(instance),
                PropertyInfo p => p.GetValue(instance),
                _ => throw new NotSupportedException($"Unsupported member type: {member.MemberType}")
            };

        private static string? FormatValue(object? value, Type type, string? format = null)
        {
            if (value is null) return string.Empty;

            // Unwrap nullable types
            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            if (!string.IsNullOrEmpty(format))
            {
                if (value is IFormattable formattable)
                {
                    return formattable.ToString(format, CultureInfo.CurrentCulture);
                }

                return string.Format(CultureInfo.CurrentCulture, $"{{0:{format}}}", value);
            }

            if (underlying == typeof(decimal))
            {
                return string.Format(CultureInfo.CurrentCulture, "{0:c}", value);
            }

            if (underlying == typeof(DateTime))
            {
                var dt = (DateTime)value;
                return dt.ToString("MM/dd/yyyy h:mm tt", CultureInfo.CurrentCulture);
            }

            // Fallback to ToString()
            return value.ToString();
        }
    }
}
