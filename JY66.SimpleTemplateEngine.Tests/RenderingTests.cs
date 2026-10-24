using System;
using System.Collections.Generic;
using JY66.SimpleTemplateEngine;
using JY66.SimpleTemplateEngine.Adapters;
using Xunit;

namespace JY66.SimpleTemplateEngine.Tests
{
    public class RenderingTests
    {
        private static DictionaryTemplateSource Source(params (string Key, string Template)[] templates)
        {
            var dict = new Dictionary<string, string>();
            foreach (var (key, template) in templates) dict[key] = template;
            return new DictionaryTemplateSource(dict);
        }

        // ---- Injection: placeholder syntax inside a value must never be interpreted ----

        [Fact]
        public void ScalarValue_ContainingScalarPlaceholder_IsOutputLiterally()
        {
            var source = Source(("Email", "Dear ||CustomerName||, welcome."));
            var model = new EmailModel { CustomerName = "Bob ||Secret||", Secret = "SSN-123" };

            var result = TemplateRenderer.Render(model, source);

            Assert.Equal("Dear Bob ||Secret||, welcome.", result);
            Assert.DoesNotContain("SSN-123", result);
        }

        [Fact]
        public void ScalarValue_ContainingNestedMarker_IsOutputLiterally()
        {
            var source = Source(
                ("Email", "Dear ||CustomerName||, welcome."),
                ("Account", "ACCT:||Number||"));
            var model = new EmailModel
            {
                CustomerName = "Bob **Account**",
                Account = new AccountModel { Number = "9999" }
            };

            var result = TemplateRenderer.Render(model, source);

            Assert.Equal("Dear Bob **Account**, welcome.", result);
            Assert.DoesNotContain("9999", result);
        }

        [Fact]
        public void NestedRenderOutput_ContainingParentPlaceholder_IsNotExpandedByParent()
        {
            var source = Source(
                ("Email", "**Account** for ||CustomerName||"),
                ("Account", "ACCT:||Number||"));
            var model = new EmailModel
            {
                CustomerName = "Bob",
                Secret = "SSN-123",
                Account = new AccountModel { Number = "||Secret||" }
            };

            var result = TemplateRenderer.Render(model, source);

            Assert.Equal("ACCT:||Secret|| for Bob", result);
        }

        [Fact]
        public void ListItemValue_ContainingPlaceholder_IsOutputLiterally()
        {
            var source = Source(
                ("Email", "||CustomerName||:**Lines**"),
                ("Line", "[||Description||]"));
            var model = new EmailModel
            {
                CustomerName = "Bob",
                Secret = "SSN-123",
                Lines = new List<LineModel> { new LineModel { Description = "||Secret||" } }
            };

            var result = TemplateRenderer.Render(model, source);

            Assert.Equal("Bob:[||Secret||]", result);
        }

        [Fact]
        public void MemberOrder_DoesNotAffectInjectionSafety()
        {
            // Secret is declared before and after CustomerName in these two models.
            var source = Source(("First", "||Name||"), ("Second", "||Name||"));

            var first = TemplateRenderer.Render(new SecretFirst { Secret = "S", Name = "||Secret||" }, source);
            var second = TemplateRenderer.Render(new SecretLast { Name = "||Secret||", Secret = "S" }, source);

            Assert.Equal("||Secret||", first);
            Assert.Equal("||Secret||", second);
        }

        // ---- Existing behaviour that the single-pass rewrite must preserve ----

        [Fact]
        public void NestedObjectAndList_RenderInPlace()
        {
            var source = Source(
                ("Email", "Hi ||CustomerName||\n**Account**\n**Lines**"),
                ("Account", "ACCT:||Number||"),
                ("Line", "- ||Description||\n"));
            var model = new EmailModel
            {
                CustomerName = "Bob",
                Account = new AccountModel { Number = "1" },
                Lines = new List<LineModel>
                {
                    new LineModel { Description = "A" },
                    null!,
                    new LineModel { Description = "B" }
                }
            };

            var result = TemplateRenderer.Render(model, source);

            Assert.Equal("Hi Bob\nACCT:1\n- A\n- B\n", result);
        }

        [Fact]
        public void NullNestedObjectAndNullList_RemoveMarkers()
        {
            var source = Source(("Email", "[**Account**][**Lines**]"));
            var model = new EmailModel { Account = null, Lines = null };

            Assert.Equal("[][]", TemplateRenderer.Render(model, source));
        }

        [Fact]
        public void UnknownPlaceholders_AreLeftInPlace()
        {
            var source = Source(("Email", "||Nope|| **bold** ||customername|| ||CustomerName||"));
            var model = new EmailModel { CustomerName = "Bob" };

            Assert.Equal("||Nope|| **bold** ||customername|| Bob", TemplateRenderer.Render(model, source));
        }

        [Fact]
        public void ListMember_IsNotRenderedAsScalar()
        {
            var source = Source(("Email", "||Lines||"));
            var model = new EmailModel { Lines = new List<LineModel>() };

            Assert.Equal("||Lines||", TemplateRenderer.Render(model, source));
        }

        [Fact]
        public void InlineFormat_IsApplied()
        {
            var source = Source(("Email", "||Amount:n2|| ||When:yyyy-MM-dd||"));
            var model = new EmailModel { Amount = 1234.5m, When = new DateTime(2025, 1, 2) };

            var result = TemplateRenderer.Render(model, source);

            Assert.Contains("1", result);
            Assert.EndsWith("2025-01-02", result);
        }

        [Fact]
        public void PublicField_IsRendered()
        {
            var source = Source(("Email", "||FieldValue||"));
            var model = new EmailModel { FieldValue = "f" };

            Assert.Equal("f", TemplateRenderer.Render(model, source));
        }

        [Fact]
        public void NestedMember_WithoutTemplateAttribute_Throws()
        {
            var source = Source(("Email", "**Plain**"));
            var model = new EmailModel { Plain = new PlainModel() };

            var ex = Assert.Throws<InvalidOperationException>(() => TemplateRenderer.Render(model, source));
            Assert.Contains("Plain", ex.Message);
        }

        [Fact]
        public void CircularReference_Throws()
        {
            var source = Source(("Email", "**Account**"), ("Account", "**Owner**"));
            var account = new AccountModel();
            var model = new EmailModel { Account = account };
            account.Owner = model;

            var ex = Assert.Throws<InvalidOperationException>(() => TemplateRenderer.Render(model, source));
            Assert.Contains("circular", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ModelWithoutTemplateAttribute_Throws()
        {
            var source = Source(("Email", "x"));

            Assert.Throws<InvalidOperationException>(() => TemplateRenderer.Render(new PlainModel(), source));
        }

        // ---- Test models ----

        [Template("Email")]
        private class EmailModel
        {
            public string? FieldValue;
            public string? CustomerName { get; set; }
            public string? Secret { get; set; }
            public decimal Amount { get; set; }
            public DateTime When { get; set; }
            public AccountModel? Account { get; set; }
            public List<LineModel>? Lines { get; set; }
            public PlainModel? Plain { get; set; }
        }

        [Template("Account")]
        private class AccountModel
        {
            public string? Number { get; set; }
            public EmailModel? Owner { get; set; }
        }

        [Template("Line")]
        private class LineModel
        {
            public string? Description { get; set; }
        }

        private class PlainModel
        {
            public string? X { get; set; }
        }

        [Template("First")]
        private class SecretFirst
        {
            public string? Secret { get; set; }
            public string? Name { get; set; }
        }

        [Template("Second")]
        private class SecretLast
        {
            public string? Name { get; set; }
            public string? Secret { get; set; }
        }
    }
}
