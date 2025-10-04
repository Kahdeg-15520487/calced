namespace CircuitSimulator.LSP
{
    public class PortInfo
    {
        public string Name { get; set; } = "";
        public int BitWidth { get; set; } = 1;
        public int DefinitionLine { get; set; } = 0;
        public int DefinitionColumn { get; set; } = 0;
    }

    public class CircuitInfo
    {
        public string Name { get; set; } = "";
        public List<PortInfo> Inputs { get; set; } = new List<PortInfo>();
        public List<PortInfo> Outputs { get; set; } = new List<PortInfo>();
        public string FilePath { get; set; } = "";
        public int DefinitionLine { get; set; } = 0;
        public Dictionary<string, GateInfo> Gates { get; set; } = new Dictionary<string, GateInfo>();

        public Dictionary<string, BlockInfo> Blocks { get; set; } = new Dictionary<string, BlockInfo>();
    }
}
