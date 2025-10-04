namespace CircuitSimulator.LSP
{
    public class PortInfo
    {
        public string Name { get; set; } = "";
        public int BitWidth { get; set; } = 1;
        public int DefinitionLine { get; set; } = 0;
        public int DefinitionColumn { get; set; } = 0;
    }
}
