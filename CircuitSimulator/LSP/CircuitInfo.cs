namespace CircuitSimulator.LSP
{
    public class CircuitInfo
    {
        public string Name { get; set; } = "";
        public List<PortInfo> Inputs { get; set; } = new List<PortInfo>();
        public List<PortInfo> Outputs { get; set; } = new List<PortInfo>();
        public string FilePath { get; set; } = "";
        public int DefinitionLine { get; set; } = 0;
        public Dictionary<string, GateInfo> Gates { get; set; } = new Dictionary<string, GateInfo>();
        public Dictionary<string, LookupTableInfo> LookupTables { get; set; } = new Dictionary<string, LookupTableInfo>();
        public Dictionary<string, BlockInfo> Blocks { get; set; } = new Dictionary<string, BlockInfo>();
    }
}