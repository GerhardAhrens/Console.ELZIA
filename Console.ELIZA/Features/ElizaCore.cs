namespace Console.ELIZA
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.Design.Serialization;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;

    public enum ElizaTopic
    {
        None,
        Emotion,
        Family,
        Desire,
        Reason,
        Hobby
    }

    public sealed class ElizaRuleSet
    {
        public List<ElizaRule> Rules { get; set; } = new();
    }

    [DebuggerDisplay("{this.Pattern}")]
    public sealed class ElizaRule
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public string Pattern { get; set; } = string.Empty;
        public int Priority { get; set; }
        public ElizaTopic Topic { get; set; }
        public List<string> Responses { get; set; } = new();
        public int ContextWeight { get; set; } = 1;
        public bool IsActive { get; set; } = true;
    }

    public static class ElizaRuleLoader
    {
        private static JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        public static List<ElizaRule> LoadFromFile(string path)
        {
            if (File.Exists(path) == false)
            {
                throw new FileNotFoundException("Regeldatei nicht gefunden", path);
            }

            var json = File.ReadAllText(path);

            var ruleSet = JsonSerializer.Deserialize<ElizaRuleSet>(json, options);

            if (ruleSet == null || ruleSet.Rules.Count == 0)
            {
                throw new InvalidOperationException("Keine ELIZA-Regeln geladen");
            }

            return ruleSet.Rules;
        }
    }

    public class ElizaContextItem
    {
        public ElizaTopic Topic { get; }
        public string Keyword { get; }
        public int Weight { get; }

        public int TurnsSinceUpdate { get; private set; }
        private DateTime _lastUpdated;

        public ElizaContextItem(ElizaTopic topic, string keyword, int weight)
        {
            Topic = topic;
            Keyword = keyword;
            Weight = Math.Max(1, weight);
            _lastUpdated = DateTime.UtcNow;
        }

        public void IncrementTurn() => TurnsSinceUpdate++;

        public bool IsExpired(int baseMaxTurns, TimeSpan baseTimeout)
        {
            var maxTurns = baseMaxTurns * Weight;
            var timeout = TimeSpan.FromTicks(baseTimeout.Ticks * Weight);

            return
                TurnsSinceUpdate >= maxTurns ||
                DateTime.UtcNow - _lastUpdated > timeout;
        }
    }

    public class ElizaContext
    {
        private readonly Stack<ElizaContextItem> _stack = new();

        public int BaseMaxTurns { get; init; } = 2;
        public TimeSpan BaseTimeout { get; init; } = TimeSpan.FromSeconds(30);

        public ElizaContextItem Current => _stack.FirstOrDefault();

        // 🔥 Neues Thema pushen
        public void Push(ElizaTopic topic, string keyword, int weight)
        {
            _stack.Push(new ElizaContextItem(topic, keyword, weight));
        }

        // 🔁 Turn für alle Kontexte erhöhen
        public void IncrementTurns()
        {
            foreach (var ctx in _stack)
            {
                ctx.IncrementTurn();
            }

            Cleanup();
        }

        // 🧹 Abgelaufene Kontexte entfernen
        public void Cleanup()
        {
            while (_stack.Count > 0 && _stack.Peek().IsExpired(BaseMaxTurns, BaseTimeout))
            {
                _stack.Pop();
            }
        }

        public bool HasContext => _stack.Count > 0;
    }


    internal sealed class ElizaCore
    {
        private readonly List<ElizaRule> _rules;
        private readonly ElizaContext _context;
        private readonly Dictionary<string, string> _reflections;
        private readonly Random _random = new();

        public ElizaCore(string ruleFilePath)
        {
            _rules = ElizaRuleLoader.LoadFromFile(ruleFilePath);

            _context = new ElizaContext
            {
                BaseMaxTurns = 2,
                BaseTimeout = TimeSpan.FromSeconds(30)
            };

            _reflections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ich", "du" },
            { "du", "ich" },
            { "mein", "dein" },
            { "dein", "mein" },
            { "mir", "dir" },
            { "dir", "mir" },
            { "mich", "dich" },
            { "dich", "mich" },
            { "bin", "bist" },
            { "bist", "bin" }
        };
        }

        // ---------------------------------------------------------

        public string Respond(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return GetFallbackResponse();

            input = input.ToLowerInvariant();

            var matches = new List<(ElizaRule Rule, Match Match)>();

            foreach (var rule in _rules)
            {
                var match = Regex.Match(input, rule.Pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    matches.Add((rule, match));
                }
            }

            if (matches.Count > 0)
            {
                var bestPriority = matches.Max(m => m.Rule.Priority);

                var selected = matches
                    .Where(m => m.Rule.Priority == bestPriority)
                    .OrderBy(_ => _random.Next())
                    .First();

                _context.Push(
                    selected.Rule.Topic,
                    selected.Match.Groups.Count > 1
                        ? selected.Match.Groups[1].Value
                        : selected.Match.Value,
                    selected.Rule.ContextWeight
                );

                var template = selected.Rule.Responses[_random.Next(selected.Rule.Responses.Count)];
                var reflected = selected.Match.Groups.Cast<Group>().Select(g => Reflect(g.Value)).ToArray();

                return string.Format(CultureInfo.CurrentCulture, template, reflected);
            }

            // ❌ Kein Treffer → alle Kontexte altern lassen
            _context.IncrementTurns();

            return RespondFromContext();
        }

        // ---------------------------------------------------------

        private string RespondFromContext()
        {
            var ctx = _context.Current;

            if (ctx == null)
            {
                return GetFallbackResponse();
            }

            return ctx.Topic switch
            {
                ElizaTopic.Emotion => $"Du hast vorhin erwähnt, dass du dich {ctx.Keyword} fühlst. Möchtest du darauf zurückkommen?",

                ElizaTopic.Family => "Vorhin ging es um deine Familie. Wie beschäftigt dich das gerade?",

                ElizaTopic.Desire => $"Du wolltest vorhin {ctx.Keyword}. Was hält dich davon ab?",

                _ => GetFallbackResponse()
            };
        }

        // ---------------------------------------------------------

        private string Reflect(string text)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < words.Length; i++)
            {
                if (_reflections.TryGetValue(words[i], out var reflected))
                    words[i] = reflected;
            }

            return string.Join(" ", words);
        }

        // ---------------------------------------------------------

        private string GetFallbackResponse()
        {
            string[] fallback =
            {
            "Erzähl mir mehr davon.",
            "Warum denkst du das?",
            "Wie fühlst du dich dabei?",
            "Das ist interessant. Fahre fort.",
            "Kannst du das näher erklären?",
            "Ist das ein spannends Thema?"
        };

            return fallback[_random.Next(fallback.Length)];
        }
    }
}
