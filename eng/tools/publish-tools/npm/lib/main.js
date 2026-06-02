#! /usr/bin/env node

const path = require('path');
const fs = require('fs');
const { spawn, spawnSync } = require('child_process');
const args = process.argv;

function installIfNeeded() {
    const bin = path.resolve(path.join(path.dirname(__filename), '..', 'bin'));
    const funcBin = path.join(bin, process.platform === 'win32' ? 'func.exe' : 'func');

    if (!fs.existsSync(funcBin)) {
        console.log('Azure Functions Core Tools binary not found. Running first-time setup...');
        const installScript = path.resolve(path.join(path.dirname(__filename), 'install.js'));
        const result = spawnSync(process.execPath, [installScript], { stdio: 'inherit' });
        if (result.status !== 0) {
            process.exit(result.status || 1);
        }
    }
}

function main() {
    installIfNeeded();

    const bin = path.resolve(path.join(path.dirname(__filename), '..', 'bin'));
    const funcProc = spawn(bin + '/func', args.slice(2), {
        stdio: [process.stdin, process.stdout, process.stderr, 'pipe']
    });

    funcProc.on('exit', code => {
        process.exit(code);
    });

    const exitHandler = (code) => {
        funcProc.kill(code);
    }

    process.on('SIGINT', exitHandler);
    process.on('SIGTERM', exitHandler);
}

main();