using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgenticWorkflowPoC.Api.Services
{
    public enum GroundingResult
    {
        Grounded,
        Ambiguous,
        Ungrounded
    }

    /// <summary>
    /// Anti-hallucination checks: every critical value the model extracted must be literally
    /// present in what the user wrote. When the text is ambiguous, the system refuses to guess.
    /// </summary>
    public static partial class IntentGrounding
    {
        public static bool IsAccountGrounded(string userIntent, string normalizedAccount) =>
            FinancialFormats.NormalizeAccount(userIntent).Contains(normalizedAccount, System.StringComparison.Ordinal);

        /// <summary>
        /// Checks that <paramref name="amount"/> appears as a numeric literal in the intent, accepting
        /// both '.' and ',' decimal conventions. A literal such as "12.500" or "1,500" (a single separator
        /// followed by exactly three digits) is ambiguous between decimal and thousands notation.
        /// </summary>
        public static GroundingResult GroundAmount(string userIntent, decimal amount)
        {
            var ambiguousMatch = false;
            foreach (Match literal in NumericLiteral().Matches(userIntent))
            {
                var interpretations = Interpret(literal.Value);
                if (!interpretations.Contains(amount))
                {
                    continue;
                }

                if (interpretations.Count == 1)
                {
                    return GroundingResult.Grounded;
                }

                ambiguousMatch = true;
            }

            return ambiguousMatch ? GroundingResult.Ambiguous : GroundingResult.Ungrounded;
        }

        private static IReadOnlyList<decimal> Interpret(string literal)
        {
            var s = GroupingWhitespace().Replace(literal, string.Empty);
            var dots = s.Count(c => c == '.');
            var commas = s.Count(c => c == ',');

            if (dots == 0 && commas == 0)
            {
                return Parse(s);
            }

            if (dots > 0 && commas > 0)
            {
                // Both present: the last one is the decimal separator, the other groups thousands.
                var decimalSeparator = s.LastIndexOf('.') > s.LastIndexOf(',') ? '.' : ',';
                var thousandsSeparator = decimalSeparator == '.' ? ',' : '.';
                if (s.Count(c => c == decimalSeparator) > 1)
                {
                    return [];
                }

                return Parse(s.Replace(thousandsSeparator.ToString(), string.Empty).Replace(decimalSeparator, '.'));
            }

            var separator = dots > 0 ? '.' : ',';
            var count = dots + commas;
            if (count > 1)
            {
                // Repeated single separator can only be grouping: 1.234.567 / 1,234,567.
                return Parse(s.Replace(separator.ToString(), string.Empty));
            }

            var digitsAfter = s.Length - s.IndexOf(separator) - 1;
            var asDecimal = Parse(s.Replace(separator, '.'));
            if (digitsAfter != 3)
            {
                return asDecimal;
            }

            var asThousands = Parse(s.Replace(separator.ToString(), string.Empty));
            return [.. asDecimal, .. asThousands];
        }

        private static IReadOnlyList<decimal> Parse(string invariantNumber) =>
            decimal.TryParse(invariantNumber, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                ? [value]
                : [];

        // '.' and ',' may join any digit group (decimal or thousands). Whitespace and apostrophes only
        // join a group of exactly three digits (thousands grouping: "12 500", "1'000"), so adjacent
        // numbers such as IBAN blocks followed by an amount ("... 0130 00 250 EUR") are not merged.
        [GeneratedRegex(@"\d+(?:[.,]\d+|['\u00A0\u202F ]\d{3}(?!\d))*", RegexOptions.CultureInvariant)]
        private static partial Regex NumericLiteral();

        [GeneratedRegex(@"['   ]", RegexOptions.CultureInvariant)]
        private static partial Regex GroupingWhitespace();
    }
}
