#! /usr/bin/env node

const path = require('path');
const fs = require('fs');
const spawn = require('child_process').spawn;
const fork = require('child_process').fork;
const commandExists = require('command-exists');
const args = process.argv;

function main() {
    const bin = path.resolve(path.join(path.dirname(__filename), '..', 'bin'));
    const funcProc = spawn(bin + '/func', args.slice(2), {
        stdio: [process.stdin, process.stdout, process.stderr, 'pipe']
    });

    funcProc.on('exit', code => {
        process.exit(code);
    });
}

main();