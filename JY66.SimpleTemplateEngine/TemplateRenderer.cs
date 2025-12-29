using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Globalization;
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
        /// Cache for compiled regex patterns to improve performance on repeated renders.
        /// Key: member name, Value: compiled regex pattern for that member.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Regex> _regexCache = new ConcurrentDictionary<string, Regex>();

        /// <summary>
        /// Renders a template for the given model by:
        ///  - Finding the model's [Template] attribute to get a template key
        ///  - Loading the template via ITemplateSource
        ///  - Replacing ||MemberName|| placeholders with formatted values
        ///  - Expanding list members using **ChildTemplateKey** markers
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

            // Get public instance fields and properties
            var fields = modelType
                .GetFields(BindingFlags.Instance | BindingFlags.Public);
            var properties = modelType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanRead);

            try
            {
                // Handle lists and nested templates (fields + properties)
                foreach (var member in fields.Cast<MemberInfo>().Concat(properties))
                {
                    var memberType = GetMemberType(member);
                    var placeholder = $"**{member.Name}**";

                    if (IsListType(memberType))
                    {
                        if (template.Contains(placeholder, StringComparison.Ordinal))
                        {
                            var itemType = memberType.GetGenericArguments()[0];
                            var itemTemplateKey = GetTemplateKey(itemType)
                                ?? throw new InvalidOperationException(
                                    $"Member '{member.Name}' does not have an associated template.");

                            var list = GetMemberValue(model, member) as IEnumerable;
                            if (list != null)
                            {
                                var listOutput = "";
                                foreach (var item in list)
                                {
                                    if (item == null) continue;
                                    var itemTemplate = templateSource.GetTemplate(itemTemplateKey);
                                    var renderedItem = RenderInternal(
                                        item,
                                        item.GetType(),
                                        itemTemplate,
                                        templateSource,
                                        renderStack,
                                        itemTemplateKey);
                                    listOutput += renderedItem;
                                }

                                template = template.Replace(placeholder, listOutput);
                            }
                            else
                            {
                                // remove placeholder if null
                                template = template.Replace(placeholder, string.Empty);
                            }
                        }

                        // Don't treat list members as scalar placeholders
                        continue;
                    }

                    if (template.Contains(placeholder, StringComparison.Ordinal))
                    {
                        var nestedTemplateKey = GetTemplateKey(memberType)
                            ?? throw new InvalidOperationException(
                                $"Member '{member.Name}' does not have an associated template.");

                        var nestedValue = GetMemberValue(model, member);
                        if (nestedValue == null)
                        {
                            template = template.Replace(placeholder, string.Empty);
                        }
                        else
                        {
                            var nestedTemplate = templateSource.GetTemplate(nestedTemplateKey);
                            var nestedRendered = RenderInternal(
                                nestedValue,
                                nestedValue.GetType(),
                                nestedTemplate,
                                templateSource,
                                renderStack,
                                nestedTemplateKey);
                            template = template.Replace(placeholder, nestedRendered);
                        }

                        // Don't treat nested template placeholders as scalars
                        continue;
                    }

                    // Scalar members - use cached regex pattern
                    var regex = _regexCache.GetOrAdd(member.Name, name =>
                    {
                        var pattern = $@"\|\|{Regex.Escape(name)}(?::([^|]+))?\|\|";
                        return new Regex(pattern, RegexOptions.Compiled);
                    });

                    template = regex.Replace(template, match =>
                    {
                        var value = GetMemberValue(model, member);
                        var format = match.Groups[1].Success ? match.Groups[1].Value : null;
                        var formatted = FormatValue(value, memberType, format);
                        return formatted ?? string.Empty;
                    });
                }

                return template;
            }
            finally
            {
                renderStack.Remove(currentTemplateKey);
            }
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

            // Keep it simple: List<T> only (matches your original behavior)
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
