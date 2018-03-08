#! /usr/bin/env node

var path = require('path');
var fs = require('fs');
var spawn = require('child_process').spawn;
var fork = require('child_process').fork;
var commandExists = require('command-exists');
var args = process.argv;

function main() {
    var bin = path.resolve(path.join(path.dirname(__filename), '..', 'bin'));
    var funcProc = spawn(bin + '/func', args.slice(2), {
        stdio: [process.stdin, process.stdout, process.stderr, 'pipe']
    });

    funcProc.on('exit', function (code) {
        process.exit(code);
    });
}

main();