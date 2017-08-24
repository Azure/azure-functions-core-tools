#! /usr/bin/env node

var path = require('path');
var fs = require('fs');
var spawn = require('child_process').spawn;
var fork = require('child_process').fork;
var commandExists = require('command-exists');
var args = process.argv;

function main() {
    commandExists('dotnet', function (err, commandExists) {
        if (commandExists) {
            var bin = path.join(path.dirname(fs.realpathSync(__filename)), '../bin');
            var funcProc = spawn('dotnet', [bin + '/Azure.Functions.Cli.dll',].concat(args.slice(2)), { stdio: [process.stdin, process.stdout, process.stderr, 'pipe'] });
            funcProc.on('exit', function (code) {
                process.exit(code);
            });
        } else {
            console.error("This requires dotnet cli to be on the path. Make sure to install .NET Core SDK 2.0 https://www.microsoft.com/net/core");
        }
    });
}

main();