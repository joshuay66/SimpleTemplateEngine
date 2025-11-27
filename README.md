# SimpleTemplateEngine

A lightweight, dependency-free C# templating engine that turns plain objects into formatted text.  
Designed for email bodies, subjects, notifications, and any scenario where you want to build text  
from simple models without pulling in heavy template engines.

✔ Attribute-based  
✔ Zero runtime dependencies  
✔ Plug-in template sources (JSON, configuration, DB, files, etc.)  
✔ Recursive list rendering  
✔ Safe, straightforward, reflection-based field & property substitution  

---

## 📦 Installation

### Using .NET CLI

```bash
dotnet add package 
```

(If unpublished yet, substitute with a local project reference.)

---

## 🚀 Quick Start

### 1. Define a model and attach a template key

```csharp
using SimpleTemplateEngine;

[Template("Emails:Welcome:Body")]
public class WelcomeEmailModel
{
    public string FirstName { get; set; }
    public DateTime SignupDate { get; set; }
}
```

### 2. Provide a template source

If using `IConfiguration`, the library includes an adapter:

```csharp
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var source = new ConfigurationTemplateSource(config);
```

### 3. Render the template

```csharp
var model = new WelcomeEmailModel
{
    FirstName = "Josh",
    SignupDate = DateTime.UtcNow
};

string result = TemplateRenderer.Render(model, source);
Console.WriteLine(result);
```

### 4. Add the template to configuration

```json
"Emails": {
  "Welcome": {
    "Body": "Hi ||FirstName||, thanks for signing up on ||SignupDate||!"
  }
}
```

**Output:**

```
Hi Josh, thanks for signing up on 11/26/2025 4:15 PM!
```

---

## 🧩 Placeholder Rules

Placeholders match *public properties or public fields* on your model:

```
||PropertyName||
```

Example:

```csharp
public string AuthorizationCode { get; set; }
```

Template:

```
Your code is: ||AuthorizationCode||
```

---

## 🧮 Built-in Formatting

| Type        | Output Format                                     |
|-------------|---------------------------------------------------|
| `decimal`   | Currency (`$1,234.56`)                            |
| `DateTime`  | `MM/dd/yyyy h:mm tt`                              |
| Others      | `ToString()`                                      |

---

## 📚 List Rendering (Recursive Templates)

You can render child items by embedding a special marker inside a parent template:

```
**ChildTemplateKey**
```

### Example

#### Models

```csharp
[Template("Orders:Summary")]
public class OrderSummary
{
    public string CustomerName { get; set; }
    public List<OrderLine> Lines { get; set; } = new();
}

[Template("Orders:Line")]
public class OrderLine
{
    public string Description { get; set; }
    public decimal Amount { get; set; }
}
```

#### Templates

```json
"Orders": {
  "Summary": "Order for ||CustomerName||:\n**Orders:Line**",
  "Line": "- ||Description||: ||Amount||\n"
}
```

**Output:**

```
Order for Josh:
- Widget A: $12.95
- Widget B: $49.00
```

---

## 🔌 Providing Your Own Template Source

The engine works with any backing store:

- Database
- Filesystem
- Embedded resources
- Remote config
- Your own dictionary

Create your own provider:

```csharp
public class DictionaryTemplateSource : ITemplateSource
{
    private readonly Dictionary<string,string> _templates;

    public DictionaryTemplateSource(Dictionary<string,string> templates)
        => _templates = templates;

    public string GetTemplate(string key) => _templates[key];
}
```

Then use it:

```csharp
var source = new DictionaryTemplateSource(new()
{
    ["Test"] = "Hello ||Name||"
});
```

---

## 🧪 Unit Testing

Inject a dictionary-based template source during tests:

```csharp
var src = new DictionaryTemplateSource(new()
{
    ["MyTemplate"] = "Value: ||Field||"
});
```

---

## 🏗 Project Goals

- Provide a simple, predictable templating engine for lightweight scenarios.
- Avoid the complexity of Razor, Scriban, Fluid, etc.
- Keep the API clean, small, and dependency-free.
- Work in console apps, background services, serverless functions, and microservices.

---

## 🔮 Roadmap

- Inline formatting (`||Amount:c2||`, `||Date:yyyy-MM-dd||`)
- Basic conditionals
- Template caching
- File-based template provider

---

## 📄 License

MIT License — free for personal and commercial use.

---

## 🤝 Contributing

Pull requests are welcome!

- Add tests
- Improve docs
- Add template sources
- Expand formatting or features

---

## ⭐ Support

If you find this useful, consider starring the repo on GitHub!
