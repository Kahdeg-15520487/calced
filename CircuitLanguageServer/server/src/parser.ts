export interface Diagnostic {
  message: string;
  line: number;
  column: number;
  length: number;
}

export class CircuitParser {
  private lines: string[] = [];
  private currentLine = 0;
  private diagnostics: Diagnostic[] = [];

  parse(text: string): Diagnostic[] {
    this.lines = text.split('\n');
    this.currentLine = 0;
    this.diagnostics = [];

    try {
      this.parseCircuitFile();
    } catch (error) {
      // Add diagnostic for parse error
      this.addDiagnostic(`Parse error: ${error}`, this.currentLine, 0, 1);
    }

    return this.diagnostics;
  }

  private parseCircuitFile() {
    while (this.currentLine < this.lines.length) {
      const line = this.lines[this.currentLine].trim();
      if (line.startsWith('import')) {
        this.parseImport();
      } else if (line.startsWith('circuit')) {
        this.parseCircuit();
      } else if (line.startsWith('//') || line === '') {
        // Comment or empty line
        this.currentLine++;
      } else {
        this.addDiagnostic('Unexpected token', this.currentLine, 0, line.length);
        this.currentLine++;
      }
    }
  }

  private parseImport() {
    const line = this.lines[this.currentLine];
    const match = line.match(/^import\s+"([^"]+)"/);
    if (!match) {
      this.addDiagnostic('Invalid import statement', this.currentLine, 0, line.length);
    }
    this.currentLine++;
  }

  private parseCircuit() {
    const line = this.lines[this.currentLine];
    const match = line.match(/^circuit\s+(\w+)\s*\{/);
    if (!match) {
      this.addDiagnostic('Invalid circuit declaration', this.currentLine, 0, line.length);
      this.currentLine++;
      return;
    }
    this.currentLine++;

    while (this.currentLine < this.lines.length && !this.lines[this.currentLine].trim().endsWith('}')) {
      const blockLine = this.lines[this.currentLine].trim();
      if (blockLine.startsWith('inputs')) {
        this.parseBlock('inputs');
      } else if (blockLine.startsWith('outputs')) {
        this.parseBlock('outputs');
      } else if (blockLine.startsWith('lookup_tables')) {
        this.parseLookupTables();
      } else if (blockLine.startsWith('gates')) {
        this.parseGates();
      } else if (blockLine.startsWith('connections')) {
        this.parseConnections();
      } else if (blockLine === '' || blockLine.startsWith('//')) {
        this.currentLine++;
      } else {
        this.addDiagnostic('Unknown block', this.currentLine, 0, blockLine.length);
        this.currentLine++;
      }
    }
    if (this.currentLine < this.lines.length) {
      this.currentLine++; // Skip closing }
    }
  }

  private parseBlock(type: string) {
    const line = this.lines[this.currentLine];
    if (!line.includes('{')) {
      this.addDiagnostic(`Invalid ${type} block`, this.currentLine, 0, line.length);
      this.currentLine++;
      return;
    }
    this.currentLine++;
    while (this.currentLine < this.lines.length && !this.lines[this.currentLine].trim().endsWith('}')) {
      // Simple validation
      this.currentLine++;
    }
    if (this.currentLine < this.lines.length) {
      this.currentLine++;
    }
  }

  private parseLookupTables() {
    // Similar to parseBlock
    this.parseBlock('lookup_tables');
  }

  private parseGates() {
    this.parseBlock('gates');
  }

  private parseConnections() {
    this.parseBlock('connections');
  }

  private addDiagnostic(message: string, line: number, column: number, length: number) {
    this.diagnostics.push({ message, line, column, length });
  }
}