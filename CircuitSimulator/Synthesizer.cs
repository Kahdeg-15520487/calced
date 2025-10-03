using System.Text;

namespace CircuitSimulator
{
    public class Synthesizer
    {
        private int gateCounter = 0;
        private HashSet<string> inputs = new HashSet<string>();
        private List<string> gates = new List<string>();
        private List<string> connections = new List<string>();

        private string NextGateName()
        {
            return $"gate{gateCounter++}";
        }

        private string ParseExpr(string expr)
        {
            expr = expr.Trim();
            if (expr.Contains('('))
            {
                // Function call
                int openParen = expr.IndexOf('(');
                string op = expr.Substring(0, openParen).ToUpper();
                string argsStr = expr.Substring(openParen + 1, expr.Length - openParen - 2);
                var args = SplitArgs(argsStr);

                // Validate operator
                var validOps = new HashSet<string> { "AND", "OR", "XOR", "NAND", "NOR", "XNOR", "NOT" };
                if (!validOps.Contains(op))
                {
                    throw new Exception($"Unknown operator: {op.ToLower()}");
                }

                if (op == "NOT")
                {
                    if (args.Count != 1) throw new Exception("NOT takes 1 argument");
                    string subExpr = ParseExpr(args[0]);
                    string gateName = NextGateName();
                    gates.Add($"{gateName} = NOT()");
                    connections.Add($"{subExpr} -> {gateName}.in[0]");
                    return $"{gateName}.out";
                }
                else
                {
                    if (args.Count != 2) throw new Exception($"{op.ToLower()} takes 2 arguments");
                    string leftExpr = ParseExpr(args[0]);
                    string rightExpr = ParseExpr(args[1]);
                    string gateName = NextGateName();
                    gates.Add($"{gateName} = {op}()");
                    connections.Add($"{leftExpr} -> {gateName}.in[0]");
                    connections.Add($"{rightExpr} -> {gateName}.in[1]");
                    return $"{gateName}.out";
                }
            }
            else
            {
                // Variable
                if (string.IsNullOrEmpty(expr)) throw new Exception("Empty expression");
                inputs.Add(expr);
                return expr;
            }
        }

        private List<string> SplitArgs(string argsStr)
        {
            var result = new List<string>();
            int level = 0;
            int start = 0;
            for (int i = 0; i < argsStr.Length; i++)
            {
                if (argsStr[i] == '(') level++;
                else if (argsStr[i] == ')') level--;
                else if (argsStr[i] == ',' && level == 0)
                {
                    result.Add(argsStr.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            result.Add(argsStr.Substring(start).Trim());
            return result;
        }

        public string GenerateDSL(string circuitName, string expression)
        {
            string rootExpr = ParseExpr(expression);
            var sb = new StringBuilder();
            sb.AppendLine($"circuit {circuitName} {{");
            sb.AppendLine($"    inputs {{ {string.Join(", ", inputs.OrderBy(x => x))} }}");
            sb.AppendLine("    outputs { result }");
            sb.AppendLine("    gates {");
            foreach (var gate in gates)
            {
                sb.AppendLine($"        {gate}");
            }
            sb.AppendLine("    }");
            sb.AppendLine("    connections {");
            foreach (var conn in connections)
            {
                sb.AppendLine($"        {conn}");
            }
            sb.AppendLine($"        {rootExpr} -> result");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
