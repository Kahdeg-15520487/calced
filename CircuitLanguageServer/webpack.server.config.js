const path = require('path');

module.exports = {
  target: 'node',
  entry: './server/src/server.ts',
  output: {
    path: path.resolve(__dirname, 'out', 'server'),
    filename: 'server.js',
    libraryTarget: 'commonjs2',
    devtoolModuleFilenameTemplate: '../[resource-path]'
  },
  externals: {
    vscode: 'commonjs vscode'
  },
  resolve: {
    extensions: ['.ts', '.js'],
    modules: ['node_modules']
  },
  module: {
    rules: [
      {
        test: /\.ts$/,
        exclude: /node_modules/,
        use: [
          {
            loader: 'ts-loader',
            options: {
              configFile: 'server/tsconfig.json'
            }
          }
        ]
      }
    ]
  }
};