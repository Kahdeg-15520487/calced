namespace CircuitSimulator.LSP
{
    public class LookupTableInfo
    {
        public string Name { get; set; } = "";
        public int DefinitionLine { get; set; } = 0;
        public int DefinitionColumn { get; set; } = 0;
        public int InputWidth { get; set; } = 0;
        public int OutputWidth { get; set; } = 0;
        public Dictionary<string, string> TruthTable { get; set; } = new Dictionary<string, string>();
    }
}