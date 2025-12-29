using System;
using System.Collections.Generic;
using System.Diagnostics;
using JY66.SimpleTemplateEngine;
using JY66.SimpleTemplateEngine.Adapters;
using Xunit;
using Xunit.Abstractions;

namespace JY66.SimpleTemplateEngine.Tests
{
    public class PerformanceTests
    {
        private readonly ITestOutputHelper _output;

        public PerformanceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void RenderTemplate_RepeatedRenders_CompletesQuickly()
        {
            // Arrange
            var templates = new Dictionary<string, string>
            {
                ["Test:Email"] = "Hello ||FirstName|| ||LastName||! Your order #||OrderId|| for ||Amount|| will arrive on ||ShipDate||."
            };
            var source = new DictionaryTemplateSource(templates);

            var model = new TestEmailModel
            {
                FirstName = "John",
                LastName = "Doe",
                OrderId = 12345,
                Amount = 99.99m,
                ShipDate = DateTime.Now
            };

            // Warm up - first render to populate cache
            var warmup = TemplateRenderer.Render(model, source);
            Assert.NotNull(warmup);

            // Act - measure performance of cached renders
            const int iterations = 1000;
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                var result = TemplateRenderer.Render(model, source);
            }

            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Rendered {iterations:N0} templates in {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Average: {(double)stopwatch.ElapsedMilliseconds / iterations:F3}ms per render");
            _output.WriteLine($"Throughput: {iterations * 1000.0 / stopwatch.ElapsedMilliseconds:F0} renders/second");

            // Should complete well under 1 second for 1000 renders with caching
            Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
                $"Expected < 1000ms for {iterations} renders, but took {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void RenderTemplate_ParallelRenders_IsCacheSafe()
        {
            // Arrange
            var templates = new Dictionary<string, string>
            {
                ["Test:Simple"] = "Name: ||Name||, Value: ||Value||"
            };
            var source = new DictionaryTemplateSource(templates);

            var model = new SimpleModel
            {
                Name = "Test",
                Value = 42
            };

            // Act - render in parallel to test thread safety
            const int parallelTasks = 100;
            var stopwatch = Stopwatch.StartNew();

            System.Threading.Tasks.Parallel.For(0, parallelTasks, i =>
            {
                var result = TemplateRenderer.Render(model, source);
                Assert.Contains("Test", result);
                Assert.Contains("42", result);
            });

            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Completed {parallelTasks} parallel renders in {stopwatch.ElapsedMilliseconds}ms");
            Assert.True(stopwatch.ElapsedMilliseconds < 500, 
                "Parallel rendering should be fast and thread-safe");
        }

        [Fact]
        public void RenderTemplate_WithFormatting_BenefitsFromCache()
        {
            // Arrange
            var templates = new Dictionary<string, string>
            {
                ["Test:Formatted"] = "Price: ||Price:c2||, Date: ||Date:yyyy-MM-dd||, Percent: ||Rate:p1||"
            };
            var source = new DictionaryTemplateSource(templates);

            var model = new FormattedModel
            {
                Price = 1234.56m,
                Date = new DateTime(2025, 12, 28),
                Rate = 0.15m
            };

            // Warm up
            TemplateRenderer.Render(model, source);

            // Act
            const int iterations = 500;
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < iterations; i++)
            {
                var result = TemplateRenderer.Render(model, source);
            }

            stopwatch.Stop();

            // Assert
            _output.WriteLine($"Rendered {iterations} formatted templates in {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Average: {(double)stopwatch.ElapsedMilliseconds / iterations:F3}ms per render");

            Assert.True(stopwatch.ElapsedMilliseconds < 500,
                $"Expected < 500ms for {iterations} renders, but took {stopwatch.ElapsedMilliseconds}ms");
        }

        [Template("Test:Email")]
        private class TestEmailModel
        {
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public int OrderId { get; set; }
            public decimal Amount { get; set; }
            public DateTime ShipDate { get; set; }
        }

        [Template("Test:Simple")]
        private class SimpleModel
        {
            public string Name { get; set; }
            public int Value { get; set; }
        }

        [Template("Test:Formatted")]
        private class FormattedModel
        {
            public decimal Price { get; set; }
            public DateTime Date { get; set; }
            public decimal Rate { get; set; }
        }
    }
}
