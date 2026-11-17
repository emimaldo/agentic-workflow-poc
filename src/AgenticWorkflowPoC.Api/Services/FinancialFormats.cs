using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgenticWorkflowPoC.Api.Services
{
    /// <summary>Deterministic format invariants for financial identifiers (no LLM involved).</summary>
    public static partial class FinancialFormats
    {
        /// <summary>ISO 4217 minor units for the currencies this PoC understands.</summary>
        private static readonly FrozenDictionary<string, int> MinorUnits = new Dictionary<string, int>
        {
            ["EUR"] = 2,
            ["USD"] = 2,
            ["GBP"] = 2,
            ["CHF"] = 2,
            ["CAD"] = 2,
            ["AUD"] = 2,
            ["SEK"] = 2,
            ["NOK"] = 2,
            ["DKK"] = 2,
            ["PLN"] = 2,
            ["MXN"] = 2,
            ["ARS"] = 2,
            ["BRL"] = 2,
            ["JPY"] = 0,
            ["KRW"] = 0,
            ["CLP"] = 0,
            ["BHD"] = 3,
            ["KWD"] = 3
        }.ToFrozenDictionary(System.StringComparer.Ordinal);

        public static bool TryGetMinorUnits(string currency, out int minorUnits) =>
            MinorUnits.TryGetValue(currency, out minorUnits);

        /// <summary>True when <paramref name="amount"/> has no more decimals than the currency allows.</summary>
        public static bool HasValidScale(decimal amount, int minorUnits) =>
            decimal.Round(amount, minorUnits) == amount;

        /// <summary>
        /// Validates a normalized (upper-case, no whitespace) IBAN: structure + ISO 13616 mod-97 checksum.
        /// </summary>
        public static bool IsValidIban(string iban)
        {
            if (!IbanShape().IsMatch(iban))
            {
                return false;
            }

            // Move the first four characters to the end and convert letters to numbers (A=10 ... Z=35).
            var rearranged = string.Concat(iban.AsSpan(4), iban.AsSpan(0, 4));
            var remainder = 0;
            foreach (var c in rearranged)
            {
                if (char.IsDigit(c))
                {
                    remainder = (remainder * 10 + (c - '0')) % 97;
                }
                else
                {
                    var value = c - 'A' + 10;
                    remainder = (remainder * 100 + value) % 97;
                }
            }

            return remainder == 1;
        }

        public static string NormalizeAccount(string account) =>
            string.Concat(account.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

        [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$", RegexOptions.CultureInvariant)]
        private static partial Regex IbanShape();
    }
}
