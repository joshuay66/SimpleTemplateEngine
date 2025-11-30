using System;
using Microsoft.Extensions.Configuration;
using JY66.SimpleTemplateEngine;

public sealed class ConfigurationTemplateSource : ITemplateSource
{
    private readonly IConfiguration _config;

    public ConfigurationTemplateSource(IConfiguration config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public string GetTemplate(string key)
    {
        var value = _config[key];
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"Config does not contain a non-empty template for key '{key}'.");

        return value;
    }
}
