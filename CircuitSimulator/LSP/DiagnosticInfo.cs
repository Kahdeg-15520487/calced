namespace CircuitSimulator.LSP
{
    public class DiagnosticInfo
    {
        public string Message { get; set; } = "";
        public int Line { get; set; }
        public int Column { get; set; }
        public int Length { get; set; }
        public string Severity { get; set; } = "error";
    }
}
