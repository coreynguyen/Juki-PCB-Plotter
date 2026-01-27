using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PCBPlotter.Core.Utilities
{
    /// <summary>
    /// Utility class for expanding reference designator ranges like "C1-5" into discrete references
    /// </summary>
    public static class ReferenceExpander
    {
        // Safety limits to prevent runaway expansion
        public const int MaxRangeSpan = 1000;      // Max difference between low and high number
        public const int MaxRefsPerField = 5000;   // Max references from a single field
        public const int MaxRefNumber = 99999;     // Max reference number allowed

        /// <summary>
        /// Result of reference expansion
        /// </summary>
        public class ExpansionResult
        {
            public List<string> References { get; set; } = new List<string>();
            public List<string> Warnings { get; set; } = new List<string>();
            public bool HasErrors { get; set; }
            public string OriginalInput { get; set; }
        }

        /// <summary>
        /// Expands a reference string that may contain ranges into discrete references.
        /// Handles formats like: "C1-5", "C 1-5", "C1 - 5", "C1, C2, C3", "C1 C2 C3", etc.
        /// </summary>
        public static ExpansionResult ExpandReferences(string input)
        {
            var result = new ExpansionResult { OriginalInput = input };

            if (string.IsNullOrWhiteSpace(input))
                return result;

            // Normalize the input
            string normalized = input.Trim().ToUpperInvariant();

            // Skip if it looks like "PCB" only
            if (normalized == "PCB" || normalized.StartsWith("PCB "))
            {
                result.References.Add("PCB");
                return result;
            }

            // Remove content in parentheses (often comments like "(do not install)")
            normalized = RemoveParenthesesContent(normalized);

            // Replace common separators with spaces
            normalized = normalized.Replace(",", " ");
            normalized = normalized.Replace(";", " ");
            normalized = normalized.Replace("~", "-"); // Some use ~ for ranges

            // Collapse multiple spaces
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // Split by spaces
            var tokens = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            if (tokens.Count == 0)
                return result;

            string lastPrefix = "";
            int i = 0;

            while (i < tokens.Count && result.References.Count < MaxRefsPerField)
            {
                string token = tokens[i].Trim();

                if (string.IsNullOrEmpty(token))
                {
                    i++;
                    continue;
                }

                // Check for standalone dash (range indicator between tokens)
                if (token == "-" && i > 0 && i < tokens.Count - 1)
                {
                    // Handle "C1 - 5" format
                    string prevRef = result.References.LastOrDefault();
                    if (prevRef != null)
                    {
                        var prevParts = ParseReference(prevRef);
                        var nextParts = ParseReference(tokens[i + 1]);

                        if (prevParts.HasValue && nextParts.HasValue)
                        {
                            // Remove the previous ref, we'll expand the range
                            result.References.RemoveAt(result.References.Count - 1);

                            string prefix = prevParts.Value.Prefix;
                            int lowNum = prevParts.Value.Number;
                            int highNum = nextParts.Value.Number;

                            // If next token is just a number, use previous prefix
                            if (string.IsNullOrEmpty(nextParts.Value.Prefix))
                            {
                                // highNum is correct
                            }
                            else if (nextParts.Value.Prefix != prefix)
                            {
                                // Different prefixes, treat as separate refs
                                result.References.Add(prevRef);
                                i++;
                                continue;
                            }

                            ExpandRange(result, prefix, lowNum, highNum);
                            i += 2; // Skip the dash and next token
                            continue;
                        }
                    }
                    i++;
                    continue;
                }

                // Check if token contains a range (e.g., "C1-5" or "1-5")
                if (token.Contains("-"))
                {
                    ProcessRangeToken(result, token, ref lastPrefix);
                }
                else
                {
                    // Regular reference or number
                    var parts = ParseReference(token);
                    if (parts.HasValue)
                    {
                        if (!string.IsNullOrEmpty(parts.Value.Prefix))
                        {
                            lastPrefix = parts.Value.Prefix;
                            result.References.Add(parts.Value.Prefix + parts.Value.Number);
                        }
                        else if (!string.IsNullOrEmpty(lastPrefix))
                        {
                            // Just a number, use last known prefix
                            result.References.Add(lastPrefix + parts.Value.Number);
                        }
                        else
                        {
                            // Can't determine prefix, skip or warn
                            result.Warnings.Add(string.Format("Cannot determine prefix for '{0}'", token));
                        }
                    }
                    else if (Regex.IsMatch(token, @"^[A-Z]+$"))
                    {
                        // Just letters, might be prefix for next token
                        lastPrefix = token;
                    }
                }

                i++;
            }

            if (result.References.Count >= MaxRefsPerField)
            {
                result.Warnings.Add(string.Format("Expansion stopped at {0} references (safety limit)", MaxRefsPerField));
                result.HasErrors = true;
            }

            // Remove duplicates and sort
            result.References = result.References.Distinct().ToList();
            result.References.Sort(ReferenceComparer);

            return result;
        }

        private static void ProcessRangeToken(ExpansionResult result, string token, ref string lastPrefix)
        {
            // Split by dash
            int dashIndex = token.IndexOf('-');
            string left = token.Substring(0, dashIndex);
            string right = token.Substring(dashIndex + 1);

            var leftParts = ParseReference(left);
            var rightParts = ParseReference(right);

            if (!leftParts.HasValue)
            {
                // Left side invalid, try using lastPrefix
                if (int.TryParse(left, out int leftNum) && !string.IsNullOrEmpty(lastPrefix))
                {
                    leftParts = (lastPrefix, leftNum);
                }
            }

            if (!leftParts.HasValue)
            {
                result.Warnings.Add(string.Format("Cannot parse range: '{0}'", token));
                return;
            }

            string prefix = leftParts.Value.Prefix;
            if (!string.IsNullOrEmpty(prefix))
                lastPrefix = prefix;
            else
                prefix = lastPrefix;

            int lowNum = leftParts.Value.Number;
            int highNum;

            if (rightParts.HasValue)
            {
                highNum = rightParts.Value.Number;
                // If right side has different prefix, might be manufacturer part number
                if (!string.IsNullOrEmpty(rightParts.Value.Prefix) && rightParts.Value.Prefix != prefix)
                {
                    // Suspicious - could be like "B-352543534" (mfr part number)
                    if (rightParts.Value.Number > MaxRefNumber || (highNum - lowNum) > MaxRangeSpan)
                    {
                        result.Warnings.Add(string.Format("Suspicious range skipped (possible part number?): '{0}'", token));
                        return;
                    }
                }
            }
            else if (int.TryParse(right, out highNum))
            {
                // Just a number on right side
            }
            else
            {
                result.Warnings.Add(string.Format("Cannot parse range end: '{0}'", token));
                return;
            }

            ExpandRange(result, prefix, lowNum, highNum);
        }

        private static void ExpandRange(ExpansionResult result, string prefix, int lowNum, int highNum)
        {
            // Safety checks
            if (lowNum > highNum)
            {
                // Swap if reversed
                int temp = lowNum;
                lowNum = highNum;
                highNum = temp;
            }

            if (highNum > MaxRefNumber)
            {
                result.Warnings.Add(string.Format("Range end {0} exceeds max ({1}), skipping", highNum, MaxRefNumber));
                result.HasErrors = true;
                return;
            }

            int span = highNum - lowNum;
            if (span > MaxRangeSpan)
            {
                result.Warnings.Add(string.Format("Range {0}{1}-{2} span ({3}) exceeds max ({4}), skipping",
                    prefix, lowNum, highNum, span, MaxRangeSpan));
                result.HasErrors = true;
                return;
            }

            for (int n = lowNum; n <= highNum && result.References.Count < MaxRefsPerField; n++)
            {
                result.References.Add(prefix + n);
            }
        }

        private static (string Prefix, int Number)? ParseReference(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            // Extract alpha prefix and numeric suffix
            var match = Regex.Match(token, @"^([A-Z]*)(\d+)$");
            if (match.Success)
            {
                string prefix = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out int number))
                {
                    return (prefix, number);
                }
            }

            return null;
        }

        private static string RemoveParenthesesContent(string input)
        {
            // Remove content in parentheses, but be careful
            // If there are numbers outside parentheses, keep those
            var result = new StringBuilder();
            int depth = 0;

            foreach (char c in input)
            {
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth > 0) depth--;
                }
                else if (depth == 0)
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Custom comparer for sorting references naturally (C1, C2, C10 not C1, C10, C2)
        /// </summary>
        public static int ReferenceComparer(string a, string b)
        {
            var aParts = ParseReference(a);
            var bParts = ParseReference(b);

            if (!aParts.HasValue && !bParts.HasValue)
                return string.Compare(a, b, StringComparison.Ordinal);
            if (!aParts.HasValue)
                return 1;
            if (!bParts.HasValue)
                return -1;

            int prefixCompare = string.Compare(aParts.Value.Prefix, bParts.Value.Prefix, StringComparison.Ordinal);
            if (prefixCompare != 0)
                return prefixCompare;

            return aParts.Value.Number.CompareTo(bParts.Value.Number);
        }

        /// <summary>
        /// Validates that all BOM references exist in placements
        /// </summary>
        public static ValidationResult ValidateBomAgainstPlacements(
            IEnumerable<string> bomReferences,
            IEnumerable<string> placementReferences)
        {
            var bomSet = new HashSet<string>(bomReferences.Select(r => r.ToUpperInvariant()));
            var placementSet = new HashSet<string>(placementReferences.Select(r => r.ToUpperInvariant()));

            var result = new ValidationResult();

            // References in BOM but not in placements
            result.OrphanedBomRefs = bomSet.Except(placementSet).OrderBy(r => r, Comparer<string>.Create(ReferenceComparer)).ToList();

            // References in placements but not in BOM
            result.OrphanedPlacementRefs = placementSet.Except(bomSet).OrderBy(r => r, Comparer<string>.Create(ReferenceComparer)).ToList();

            // Matched references
            result.MatchedRefs = bomSet.Intersect(placementSet).OrderBy(r => r, Comparer<string>.Create(ReferenceComparer)).ToList();

            return result;
        }

        public class ValidationResult
        {
            public List<string> OrphanedBomRefs { get; set; } = new List<string>();
            public List<string> OrphanedPlacementRefs { get; set; } = new List<string>();
            public List<string> MatchedRefs { get; set; } = new List<string>();

            public bool HasOrphans => OrphanedBomRefs.Count > 0 || OrphanedPlacementRefs.Count > 0;

            public override string ToString()
            {
                return string.Format("Matched: {0}, In BOM only: {1}, In Placements only: {2}",
                    MatchedRefs.Count, OrphanedBomRefs.Count, OrphanedPlacementRefs.Count);
            }
        }
    }
}
