namespace InovaGed.Application.Classification
{
    public sealed class RuleBasedDocumentTypeSuggester
    {
        private readonly List<(string Pattern, Guid TypeId, decimal Weight, string Label)> _rules;

        public RuleBasedDocumentTypeSuggester(IEnumerable<(string pattern, Guid typeId, decimal weight, string label)> rules)
        {
            _rules = rules.ToList();
        }

        public (Guid? typeId, decimal score, List<string> signals) Suggest(string? fileName, string? folderName, string? title)
        {
            var hay = $"{fileName} {folderName} {title}".ToLowerInvariant();
            decimal best = 0m;
            Guid? bestId = null;
            var bestSignals = new List<string>();

            foreach (var r in _rules)
            {
                if (hay.Contains(r.Pattern.ToLowerInvariant()))
                {
                    var score = r.Weight;
                    if (score > best)
                    {
                        best = score;
                        bestId = r.TypeId;
                        bestSignals = new List<string> { $"Regra: {r.Label} (match '{r.Pattern}', peso {r.Weight:0.00})" };
                    }
                    else if (bestId == r.TypeId)
                    {
                        bestSignals.Add($"Regra: {r.Label} (match '{r.Pattern}', peso {r.Weight:0.00})");
                    }
                }
            }

            return (bestId, best, bestSignals);
        }
    }

}
