using System.Collections.Generic;
using JY66.SimpleTemplateEngine;
using JY66.SimpleTemplateEngine.Adapters;
using Xunit;

namespace JY66.SimpleTemplateEngine.Tests
{
    public class CacheBehaviorTests
    {
        [Fact]
        public void RegexCache_DifferentProperties_UseDifferentCacheEntries()
        {
            // Arrange
            var templates = new Dictionary<string, string>
            {
                ["Test:Model1"] = "Value: ||PropertyA||",
                ["Test:Model2"] = "Value: ||PropertyB||"
            };
            var source = new DictionaryTemplateSource(templates);

            var model1 = new ModelWithPropertyA { PropertyA = "ValueA" };
            var model2 = new ModelWithPropertyB { PropertyB = "ValueB" };

            // Act
            var result1 = TemplateRenderer.Render(model1, source);
            var result2 = TemplateRenderer.Render(model2, source);

            // Assert - both should render correctly (cache doesn't interfere)
            Assert.Equal("Value: ValueA", result1);
            Assert.Equal("Value: ValueB", result2);
        }

        [Fact]
        public void Cache_MultipleRendersOfSameTemplate_ProduceConsistentResults()
        {
            // Arrange
            var templates = new Dictionary<string, string>
            {
                ["Test:User"] = "User: ||Username||, Email: ||Email||"
            };
            var source = new DictionaryTemplateSource(templates);

            var model = new UserModel
            {
                Username = "johndoe",
                Email = "john@example.com"
            };

            // Act - render multiple times
            var result1 = TemplateRenderer.Render(model, source);
            var result2 = TemplateRenderer.Render(model, source);
            var result3 = TemplateRenderer.Render(model, source);

            // Assert - all should be identical
            Assert.Equal(result1, result2);
            Assert.Equal(result2, result3);
            Assert.Contains("johndoe", result1);
            Assert.Contains("john@example.com", result1);
        }

        [Fact]
        public void Cache_WithDifferentModelValues_RendersCorrectly()
        {
            // Arrange
            var templates = new Dictionary<string, string>
            {
                ["Test:Counter"] = "Count: ||Count||"
            };
            var source = new DictionaryTemplateSource(templates);

            // Act - render with different values
            var result1 = TemplateRenderer.Render(new CounterModel { Count = 1 }, source);
            var result2 = TemplateRenderer.Render(new CounterModel { Count = 2 }, source);
            var result3 = TemplateRenderer.Render(new CounterModel { Count = 3 }, source);

            // Assert - cache should not affect different values
            Assert.Equal("Count: 1", result1);
            Assert.Equal("Count: 2", result2);
            Assert.Equal("Count: 3", result3);
        }

        [Fact]
        public void Cache_WithNullValues_HandlesCorrectly()
        {
            // Arrange
            var templates = new Dictionary<string, string>
            {
                ["Test:Nullable"] = "Value: ||Value||, Name: ||Name||"
            };
            var source = new DictionaryTemplateSource(templates);

            var model = new NullableModel
            {
                Value = null,
                Name = null
            };

            // Act
            var result = TemplateRenderer.Render(model, source);

            // Assert
            Assert.Equal("Value: , Name: ", result);
        }

        [Template("Test:Model1")]
        private class ModelWithPropertyA
        {
            public string PropertyA { get; set; }
        }

        [Template("Test:Model2")]
        private class ModelWithPropertyB
        {
            public string PropertyB { get; set; }
        }

        [Template("Test:User")]
        private class UserModel
        {
            public string Username { get; set; }
            public string Email { get; set; }
        }

        [Template("Test:Counter")]
        private class CounterModel
        {
            public int Count { get; set; }
        }

        [Template("Test:Nullable")]
        private class NullableModel
        {
            public string Value { get; set; }
            public string Name { get; set; }
        }
    }
}
