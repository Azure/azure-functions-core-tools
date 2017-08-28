#! /usr/bin/env node

var path = require('path');
var fs = require('fs');
var spawn = require('child_process').spawn;
var fork = require('child_process').fork;
var args = process.argv;

function main() {
    var bin = path.join(path.dirname(fs.realpathSync(__filename)), '../bin');
    
    var funcProc = spawn('dotnet', [bin + '/Azure.Functions.Cli.dll',].concat(args.slice(2)), { stdio : [process.stdin, process.stdout, process.stderr, 'pipe']});
    funcProc.on('exit', function(code) {
        process.exit(code);
    });
}

main();