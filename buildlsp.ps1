dotnet build .\CircuitSimulator
Push-Location .\CircuitLanguageServer
npm run build
code --install-extension .\circuit-language-server-0.0.1.vsix
Pop-Location