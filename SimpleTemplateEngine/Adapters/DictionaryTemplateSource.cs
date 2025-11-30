
using JY66.SimpleTemplateEngine;
using System.Collections.Generic;

namespace JY66.SimpleTemplateEngine.Adapters
{
        public class DictionaryTemplateSource : ITemplateSource
    {
        private readonly Dictionary<string,string> _templates;

        public DictionaryTemplateSource(Dictionary<string,string> templates)
            => _templates = templates;

        public string GetTemplate(string key) => _templates[key];
    }    
}
