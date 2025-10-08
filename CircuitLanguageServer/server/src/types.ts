// Circuit information for hover tooltips
export interface GateInfo {
	Type: string;
	DefinitionLine: number;
	DefinitionColumn: number;
}

export interface PortInfo {
	Name: string;
	BitWidth: number;
	DefinitionLine: number;
	DefinitionColumn: number;
}

export interface BlockInfo {
	StartLine: number;
	StartColumn: number;
	EndLine: number;
}

export interface LookupTableInfo {
	Name: string;
	DefinitionLine: number;
	DefinitionColumn: number;
	InputWidth: number;
	OutputWidth: number;
	TruthTable: { [input: string]: string };
}

export interface CircuitInfo {
	Name: string;
	Inputs: PortInfo[];
	Outputs: PortInfo[];
	FilePath: string;
	DefinitionLine: number;
	Level: number;
	Gates: { [name: string]: GateInfo };
	LookupTables: { [name: string]: LookupTableInfo };
	Blocks: { [name: string]: BlockInfo };
}

// Cache for getCircuitInfo results to avoid redundant calls
export interface CircuitInfoCacheEntry {
	contentHash: string;
	basePath: string;
	originalFilePath?: string;
	result: CircuitInfo[] | null;
	timestamp: number;
}

// Example settings for the language server
export interface ExampleSettings {
	maxNumberOfProblems: number;
}

// Connection reference parsing result
export interface ConnectionReference {
	target: 'gate' | 'port' | 'direction';
	gateName: string;
	direction?: 'in' | 'out';
	portName?: string;
}