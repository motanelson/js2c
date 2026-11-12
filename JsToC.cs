using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace JsToC
{
    internal class Program
    {
        // Main entry point:
        // JsToC.exe input.js output.c
        private static int Main(string[] args)
        {
            Console.Title = "JS to C Transpiler";
            Console.BackgroundColor= ConsoleColor.White;
            Console.ForegroundColor= ConsoleColor.Black;
            Console.Clear();
            if (args.Length < 2)
            {
                Console.WriteLine("JS -> C Transpiler");
                Console.WriteLine();
                Console.WriteLine("Usage:");
                Console.WriteLine("  JsToC.exe <input.js> <output.c>");
                Console.WriteLine();
                Console.WriteLine("Example:");
                Console.WriteLine("  JsToC.exe entrada.js saida.c");
                return 1;
            }

            string inputFile = args[0];
            string outputFile = args[1];

            try
            {
                if (!File.Exists(inputFile))
                {
                    Console.Error.WriteLine("Error: input file not found: " + inputFile);
                    return 1;
                }

                string jsCode = File.ReadAllText(inputFile, Encoding.UTF8);
                string cCode = ConvertJsToC(jsCode);

                string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputFile));
                if (!String.IsNullOrEmpty(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                File.WriteAllText(outputFile, cCode, new UTF8Encoding(false));

                Console.WriteLine("JS -> C Transpiler");
                Console.WriteLine("------------------");
                Console.WriteLine("Input : " + inputFile);
                Console.WriteLine("Output: " + outputFile);
                Console.WriteLine("Status: Conversion successful");
                Console.WriteLine("JS: " + CountLines(jsCode) + " lines | C: " + CountLines(cCode) + " lines");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 1;
            }
        }

        // Main conversion engine.
        // This follows the original JavaScript application's line-based conversion logic.
        private static string ConvertJsToC(string jsCode)
        {
            if (String.IsNullOrWhiteSpace(jsCode))
                return "";

            string[] lines = jsCode.Split('\n');
            List<string> functionDefs = new List<string>();
            List<string> mainStatements = new List<string>();

            bool inFunction = false;
            List<string> currentFunction = new List<string>();
            int braceDepth = 0;

            foreach (string line in lines)
            {
                string trimmed = line.Trim();

                int openBraces = CountMatches(line, @"\{");
                int closeBraces = CountMatches(line, @"\}");

                // Check if starting a function.
                if (Regex.IsMatch(trimmed, @"^function\s+([a-zA-Z0-9_$]+)\s*\((.*)\)"))
                {
                    inFunction = true;
                    braceDepth += openBraces - closeBraces;
                    currentFunction.Add(TransformFunctionHeader(trimmed));

                    if (braceDepth == 0 && openBraces > 0)
                    {
                        inFunction = false;
                        functionDefs.Add(String.Join("\n", currentFunction));
                        currentFunction.Clear();
                    }

                    continue;
                }

                if (inFunction)
                {
                    braceDepth += openBraces - closeBraces;
                    currentFunction.Add(TransformStatement(line));

                    if (braceDepth <= 0)
                    {
                        inFunction = false;
                        functionDefs.Add(String.Join("\n", currentFunction));
                        currentFunction.Clear();
                    }

                    continue;
                }

                // Top-level statements go into main().
                if (trimmed.Length > 0)
                    mainStatements.Add("    " + TransformStatement(trimmed));
            }

            StringBuilder cProgram = new StringBuilder();

            cProgram.AppendLine("#include <stdio.h>");
            cProgram.AppendLine("#include <stdlib.h>");
            cProgram.AppendLine("#include <stdbool.h>");
            cProgram.AppendLine("#include <string.h>");
            cProgram.AppendLine();

            if (functionDefs.Count > 0)
            {
                cProgram.Append(String.Join("\n\n", functionDefs));
                cProgram.AppendLine();
                cProgram.AppendLine();
            }

            cProgram.AppendLine("int main(int argc, char *argv[]) {");

            if (mainStatements.Count > 0)
                cProgram.Append(String.Join("\n", mainStatements));

            cProgram.AppendLine();
            cProgram.AppendLine("    return 0;");
            cProgram.Append("}");

            return cProgram.ToString();
        }

        // Transform statements: variables, print calls and loops.
        private static string TransformStatement(string statement)
        {
            string result = statement;

            // Map print(), message.log() and console.log() to printf().
            result = Regex.Replace(
                result,
                @"(?:console\.log|message\.log|print)\s*\((.*?)\);?",
                delegate(Match match)
                {
                    return TransformPrintCall(match.Groups[1].Value) + ";";
                });

            // Strings: let s = "hello" -> const char* s = "hello"
            result = Regex.Replace(
                result,
                @"(?:let|const|var)\s+([a-zA-Z0-9_$]+)\s*=\s*(['""`])(.*?)\2;?",
                "const char* $1 = \"$3\";");

            // Integers: let x = 42
            result = Regex.Replace(
                result,
                @"(?:let|const|var)\s+([a-zA-Z0-9_$]+)\s*=\s*(-?\d+);?",
                "int $1 = $2;");

            // Floating-point values: let x = 3.14
            result = Regex.Replace(
                result,
                @"(?:let|const|var)\s+([a-zA-Z0-9_$]+)\s*=\s*(-?\d+\.\d+);?",
                "double $1 = $2;");

            // Booleans: let b = true / false
            result = Regex.Replace(
                result,
                @"(?:let|const|var)\s+([a-zA-Z0-9_$]+)\s*=\s*(true|false);?",
                "bool $1 = $2;");

            // Generic fallback for variable declarations.
            result = Regex.Replace(
                result,
                @"(?:let|var)\s+([a-zA-Z0-9_$]+);?",
                "int $1 = 0;");

            // For loops: for (let i = ...) -> for (int i = ...)
            result = Regex.Replace(
                result,
                @"for\s*\(\s*(?:let|var)\s+",
                "for (int ");

            return result;
        }

        // Format printf arguments.
        private static string TransformPrintCall(string argsContent)
        {
            if (String.IsNullOrWhiteSpace(argsContent))
                return "printf(\"\\n\")";

            // Check if format specifiers are already provided.
            if (argsContent.Contains("%s") ||
                argsContent.Contains("%d") ||
                argsContent.Contains("%f"))
            {
                int firstComma = argsContent.IndexOf(',');

                if (firstComma != -1)
                {
                    string fmt = argsContent.Substring(0, firstComma).Trim();
                    string rest = argsContent.Substring(firstComma + 1).Trim();

                    // Add a newline to the format string.
                    if (fmt.EndsWith("\"", StringComparison.Ordinal))
                        fmt = fmt.Substring(0, fmt.Length - 1) + "\\n\"";

                    return "printf(" + fmt + ", " + rest + ")";
                }
            }

            List<string> rawArgs = SplitArguments(argsContent);
            List<string> formatTokens = new List<string>();
            List<string> expressions = new List<string>();

            foreach (string rawArg in rawArgs)
            {
                string arg = rawArg.Trim();

                if (Regex.IsMatch(arg, @"^[""'].*[""']$"))
                {
                    // Raw string literal.
                    string cleanString = Regex.Replace(arg, @"^[""']|[""']$", "");
                    formatTokens.Add(cleanString);
                }
                else if (Regex.IsMatch(arg, @"^-?\d+$"))
                {
                    formatTokens.Add("%d");
                    expressions.Add(arg);
                }
                else if (Regex.IsMatch(arg, @"^-?\d+\.\d+$"))
                {
                    formatTokens.Add("%f");
                    expressions.Add(arg);
                }
                else
                {
                    // Default variable or expression type.
                    formatTokens.Add("%d");
                    expressions.Add(arg);
                }
            }

            string formatString = String.Join(" ", formatTokens) + "\\n";

            if (expressions.Count > 0)
                return "printf(\"" + formatString + "\", " + String.Join(", ", expressions) + ")";

            return "printf(\"" + formatString + "\")";
        }

        // Transform function foo(a, b) into int foo(int a, int b).
        private static string TransformFunctionHeader(string line)
        {
            Match match = Regex.Match(
                line,
                @"^function\s+([a-zA-Z0-9_$]+)\s*\((.*?)\)");

            if (!match.Success)
                return line;

            string name = match.Groups[1].Value;
            string parameters = match.Groups[2].Value;
            string typedParameters;

            if (parameters.Trim().Length > 0)
            {
                string[] parts = parameters.Split(',');
                List<string> typed = new List<string>();

                foreach (string part in parts)
                    typed.Add("int " + part.Trim());

                typedParameters = String.Join(", ", typed);
            }
            else
            {
                typedParameters = "void";
            }

            return "int " + name + "(" + typedParameters + ")";
        }

        // Split comma-separated arguments, ignoring commas inside quoted strings.
        private static List<string> SplitArguments(string str)
        {
            List<string> result = new List<string>();
            StringBuilder current = new StringBuilder();

            bool inQuote = false;
            char quoteChar = '\0';

            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];

                if ((c == '"' || c == '\'') &&
                    (i == 0 || str[i - 1] != '\\'))
                {
                    if (!inQuote)
                    {
                        inQuote = true;
                        quoteChar = c;
                    }
                    else if (quoteChar == c)
                    {
                        inQuote = false;
                    }
                }

                if (c == ',' && !inQuote)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
                result.Add(current.ToString());

            return result;
        }

        private static int CountMatches(string text, string pattern)
        {
            return Regex.Matches(text, pattern).Count;
        }

        private static int CountLines(string text)
        {
            if (text.Length == 0)
                return 0;

            return text.Split('\n').Length;
        }
    }
}
