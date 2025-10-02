const fs = require('fs');
const path = require('path');

// Create bin directory
const binDir = path.join(__dirname, '..', 'bin');
if (!fs.existsSync(binDir)) {
    fs.mkdirSync(binDir, { recursive: true });
}

// Copy CircuitSimulator executable and dependencies
const simulatorSrc = path.join(__dirname, '..', '..', 'CircuitSimulator', 'bin', 'Debug', 'net8.0');
const simulatorDest = binDir;

if (fs.existsSync(simulatorSrc)) {
    // Copy all files from the CircuitSimulator output directory
    const files = fs.readdirSync(simulatorSrc);
    files.forEach(file => {
        const srcFile = path.join(simulatorSrc, file);
        const destFile = path.join(simulatorDest, file);
        
        if (fs.statSync(srcFile).isFile()) {
            fs.copyFileSync(srcFile, destFile);
            console.log(`Copied: ${file}`);
        }
    });
    console.log('CircuitSimulator binaries copied successfully!');
} else {
    console.error(`CircuitSimulator binaries not found at: ${simulatorSrc}`);
    console.error('Please build CircuitSimulator first with: dotnet build CircuitSimulator');
    process.exit(1);
}