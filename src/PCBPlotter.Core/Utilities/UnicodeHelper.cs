using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PCBPlotter.Core.Utilities
{
    /// <summary>
    /// Helper class for handling Unicode and ANSI text conversion
    /// </summary>
    public static class UnicodeHelper
    {
        private static readonly Dictionary<char, string> UnicodeToAnsiMap = new Dictionary<char, string>
        {
            // Common diacritical marks
            { 'á', "a" }, { 'à', "a" }, { 'ä', "a" }, { 'â', "a" }, { 'ã', "a" }, { 'å', "a" },
            { 'Á', "A" }, { 'À', "A" }, { 'Ä', "A" }, { 'Â', "A" }, { 'Ã', "A" }, { 'Å', "A" },
            { 'é', "e" }, { 'è', "e" }, { 'ë', "e" }, { 'ê', "e" },
            { 'É', "E" }, { 'È', "E" }, { 'Ë', "E" }, { 'Ê', "E" },
            { 'í', "i" }, { 'ì', "i" }, { 'ï', "i" }, { 'î', "i" },
            { 'Í', "I" }, { 'Ì', "I" }, { 'Ï', "I" }, { 'Î', "I" },
            { 'ó', "o" }, { 'ò', "o" }, { 'ö', "o" }, { 'ô', "o" }, { 'õ', "o" }, { 'ø', "o" },
            { 'Ó', "O" }, { 'Ò', "O" }, { 'Ö', "O" }, { 'Ô', "O" }, { 'Õ', "O" }, { 'Ø', "O" },
            { 'ú', "u" }, { 'ù', "u" }, { 'ü', "u" }, { 'û', "u" },
            { 'Ú', "U" }, { 'Ù', "U" }, { 'Ü', "U" }, { 'Û', "U" },
            { 'ý', "y" }, { 'ÿ', "y" },
            { 'Ý', "Y" },
            { 'ñ', "n" }, { 'Ñ', "N" },
            { 'ç', "c" }, { 'Ç', "C" },

            // Special characters
            { 'ß', "ss" },
            { 'æ', "ae" }, { 'Æ', "AE" },
            { 'œ', "oe" }, { 'Œ', "OE" },
            { 'þ', "th" }, { 'Þ', "TH" },
            { 'ð', "d" }, { 'Ð', "D" },

            // Greek letters commonly used in electronics
            { 'μ', "u" }, // micro
            { 'Ω', "Ohm" }, // ohm
            { 'Δ', "Delta" },
            { 'Σ', "Sigma" },
            { 'π', "pi" },

            // Common symbols
            { '°', "deg" },
            { '±', "+-" },
            { '×', "x" },
            { '÷', "/" },
            { '·', "." },
            { '€', "EUR" },
            { '£', "GBP" },
            { '¥', "JPY" },
            { '©', "(C)" },
            { '®', "(R)" },
            { '™', "(TM)" },
            { '²', "2" },
            { '³', "3" },

            // Smart quotes and dashes
            { '"', "\"" }, { '"', "\"" },
            { ''', "'" }, { ''', "'" },
            { '–', "-" }, { '—', "-" },
            { '…', "..." },

            // Fractions
            { '½', "1/2" },
            { '¼', "1/4" },
            { '¾', "3/4" },
        };

        /// <summary>
        /// Sanitizes a string for ANSI compatibility by replacing Unicode characters
        /// </summary>
        public static string SanitizeToAnsi(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                if (c <= 127)
                {
                    // Standard ASCII
                    result.Append(c);
                }
                else if (UnicodeToAnsiMap.TryGetValue(c, out string replacement))
                {
                    // Known replacement
                    result.Append(replacement);
                }
                else if (c >= 128 && c <= 255)
                {
                    // Extended ASCII - keep as is
                    result.Append(c);
                }
                else
                {
                    // Unknown Unicode - replace with underscore
                    result.Append('_');
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Sanitizes a file path for systems that don't support Unicode paths
        /// </summary>
        public static string SanitizePathToAnsi(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Keep drive letters and path separators
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var sanitizedParts = parts.Select(part =>
            {
                if (part.Length == 2 && part[1] == ':')
                    return part; // Drive letter

                return SanitizeToAnsi(part);
            });

            return string.Join(Path.DirectorySeparatorChar.ToString(), sanitizedParts);
        }

        /// <summary>
        /// Checks if a string contains non-ANSI characters
        /// </summary>
        public static bool ContainsNonAnsi(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            return input.Any(c => c > 255);
        }

        /// <summary>
        /// Checks if a path contains non-ANSI characters
        /// </summary>
        public static bool PathContainsNonAnsi(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Skip drive letter check
            int startIndex = 0;
            if (path.Length >= 2 && path[1] == ':')
                startIndex = 2;

            for (int i = startIndex; i < path.Length; i++)
            {
                char c = path[i];
                if (c > 255 && c != Path.DirectorySeparatorChar && c != Path.AltDirectorySeparatorChar)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a temporary ANSI-compatible path copy of a file
        /// </summary>
        public static string CreateAnsiTempCopy(string sourcePath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Source file not found", sourcePath);

            // Generate temp path with sanitized filename
            string fileName = Path.GetFileName(sourcePath);
            string sanitizedFileName = SanitizeToAnsi(fileName);
            string tempPath = Path.Combine(Path.GetTempPath(), sanitizedFileName);

            // Add random suffix to avoid collisions
            string baseName = Path.GetFileNameWithoutExtension(tempPath);
            string extension = Path.GetExtension(tempPath);
            tempPath = Path.Combine(Path.GetTempPath(),
                baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + extension);

            File.Copy(sourcePath, tempPath, true);
            return tempPath;
        }

        /// <summary>
        /// Gets the appropriate encoding for export based on settings
        /// </summary>
        public static Encoding GetExportEncoding(bool forceAnsi = false)
        {
            if (forceAnsi)
            {
                // Use Windows-1252 for maximum ANSI compatibility
                return Encoding.GetEncoding(1252);
            }

            // Default to UTF-8 with BOM for modern systems
            return new UTF8Encoding(true);
        }

        /// <summary>
        /// Writes text to a file with appropriate encoding
        /// </summary>
        public static void WriteTextFile(string path, string content, bool forceAnsi = false)
        {
            var encoding = GetExportEncoding(forceAnsi);

            if (forceAnsi)
            {
                // Sanitize content for ANSI
                content = SanitizeToAnsi(content);
            }

            File.WriteAllText(path, content, encoding);
        }

        /// <summary>
        /// Sanitizes reference designators to ANSI-compatible format
        /// </summary>
        public static string SanitizeReference(string reference)
        {
            if (string.IsNullOrEmpty(reference))
                return reference;

            // Remove any non-alphanumeric characters except dash, underscore, and period
            var result = new StringBuilder();
            foreach (var c in reference)
            {
                if ((c >= 'A' && c <= 'Z') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') ||
                    c == '-' || c == '_' || c == '.')
                {
                    result.Append(c);
                }
                else if (UnicodeToAnsiMap.TryGetValue(c, out string replacement))
                {
                    result.Append(replacement);
                }
                // Skip other characters
            }

            return result.ToString();
        }

        /// <summary>
        /// Sanitizes part numbers to ANSI-compatible format
        /// </summary>
        public static string SanitizePartNumber(string partNumber)
        {
            if (string.IsNullOrEmpty(partNumber))
                return partNumber;

            return SanitizeToAnsi(partNumber)
                .Replace(" ", "_")
                .Replace(",", "_")
                .Replace(";", "_");
        }
    }
}
